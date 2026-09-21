using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// The pilot's persistent state: credits, hold, fuel, hull and refit levels, the market and which zone you are in.
/// Ported from `state` in belt-runner-3d.html. Saved as JSON under Application.persistentDataPath with the browser
/// save's field names, so a future importer can read the localStorage save in.
public static class State
{
    public static string SavePath
    {
        get { return Path.Combine(Application.persistentDataPath, "belt-runner-save.json"); }
    }

    public static float credits = 60f;
    public static Dictionary<string, float> cargo = new Dictionary<string, float>();
    public static Dictionary<string, float> store = new Dictionary<string, float>();
    public static float fuel = 100f;
    public static float hull = 50f;
    public static float shield = Data.SHIELD_MAX;
    public static float sinceHit = 99f;   // seconds since the last hit, for the shield's recharge
    public static float shipFuel = 1200f;   // the cargo ship's fuel supply, which the ship's tank fills from while docked
    public static float parts = 120f;       // repair parts aboard the cargo ship, one per hull point mended while docked
    public static int rockets = -1;         // seeker rockets aboard; -1 (a fresh pilot, an older save) means a full magazine, filled on the pad
    public static int flares = -1;          // flares aboard (2026-09-19, the countermeasure to the outpost's seekers); -1 likewise
    public static int raid = 0;             // the raid contract (2026-09-19): 0 open, 1 accepted, 2 the outpost is down (paid if it was accepted)
    public static int raids = 0;            // outposts put down under contract, all told: the reward grows with it
    public static float raidAt = 0f;        // play time at which a rebuilt outpost and a fresh contract post
    public static bool raidStation = false; // the cargo ship holds station off the outpost (it is put back there on load)
    public static Dictionary<string, int> up = new Dictionary<string, int>();
    public static Dictionary<string, int> depot = new Dictionary<string, int>();   // cargo ship upgrades: the mast dish and the collector drones
    public static float droneUnits = 0f;                                          // ore the collectors have stowed, all told
    public static Dictionary<string, float> market = new Dictionary<string, float>();
    public static Dictionary<string, float> marketNext = new Dictionary<string, float>();
    public static float marketT = 0f;
    public static string zoneId = "kessler";
    public static int tut = 0;
    public static float mined = 0f;
    public static float earned = 0f;
    public static float time = 0f;
    public static bool soundOn = true;
    public static float volume = 1f;
    public static float hudScale = 1f;         // Settings: HUD size
    public static float brightness = 1f;       // Settings: the frame's exposure, 0.5 to 1.5
    public static bool musicOn = true;         // Settings: the soundtrack, apart from the sound effects
    public static float musicVolume = 1f;
    public static int display = 1;   // 0 full screen (exclusive), 1 borderless (a full-screen window), 2 windowed
    public static bool controlsShown = true;   // C hides the flight controls list; remembered in the save
    public static bool cockpitView;            // P: the first-person cockpit view (2026-09-20); remembered in the save
    public static bool hasSave = false;
    public static bool sandbox = false;   // a test session (-combat): the save file is never written

    static bool _init;

    public static void Init()
    {
        if (_init) return;
        _init = true;
        foreach (var k in Data.ORE_KEYS)
        {
            cargo[k] = 0f;
            store[k] = 0f;
            market[k] = 1f;
            marketNext[k] = 1f;
        }
        foreach (var k in Data.UPGRADE_KEYS) up[k] = 0;
        foreach (var k in Data.DEPOT_KEYS) depot[k] = 0;
        Load();
    }

    /// The shield at the fitted refit, and its recharge rate (empty to full in three seconds at any level).
    public static float ShieldMax { get { return Stat("shield").hp; } }
    public static float ShieldRate { get { return ShieldMax / 3f; } }

    /// The level entry of a refit, e.g. Stat("engine").max
    public static Data.Level Stat(string key)
    {
        return Data.UPGRADES[key].levels[up[key]];
    }

    /// Damage to the ship: the shield soaks it first, the hull takes the rest; the shield's recharge timer restarts.
    public static void Damage(float dmg)
    {
        sinceHit = 0f;
        float toShield = Mathf.Min(shield, dmg);
        bool had = shield > 0f;
        shield -= toShield;
        hull = Mathf.Max(0f, hull - (dmg - toShield));
        if (had && shield <= 0f) Audio.Play("shield_down");   // the shield stripped: the pilot hears it go
    }

    /// The shield recharges once ten seconds have passed without a hit.
    public static void TickShield(float dt)
    {
        sinceHit += dt;
        bool wasOut = shield <= 0f;
        if (sinceHit >= Data.SHIELD_WAIT && shield < ShieldMax) shield = Mathf.Min(ShieldMax, shield + ShieldRate * dt);
        if (wasOut && shield > 0f) Audio.Play("shield_up");   // the recharge starting from nothing: the shield coming back
    }

    // ---- the hold: slots of STACK units, one ore per slot
    public static int CargoSlots()
    {
        return Stat("cargo").slots;
    }

    static int Stacks(float units)
    {
        return Mathf.CeilToInt(units / Data.STACK - 1e-6f);
    }

    public static int UsedSlots()
    {
        int n = 0;
        foreach (var k in Data.ORE_KEYS) n += Stacks(cargo[k]);
        return n;
    }

    public static int FreeSlots()
    {
        return Mathf.Max(0, CargoSlots() - UsedSlots());
    }

    public static float CargoTotal()
    {
        float t = 0f;
        foreach (var k in Data.ORE_KEYS) t += cargo[k];
        return t;
    }

    public static float CargoCapacity()
    {
        return CargoSlots() * Data.STACK;
    }

    /// Units of ore `ore` that still fit: the part-filled stack tops up first, then empty slots.
    public static float CargoRoom(string ore)
    {
        float have = cargo[ore];
        return (Stacks(have) * Data.STACK - have) + FreeSlots() * Data.STACK;
    }

    public static float AddCargo(string ore, float units)
    {
        float took = Mathf.Min(units, CargoRoom(ore));
        cargo[ore] += took;
        mined += took;
        return took;
    }

    // ---- the cargo ship's storage: the same slot rules, 50 slots
    public static int StoreUsed()
    {
        int n = 0;
        foreach (var k in Data.ORE_KEYS) n += Stacks(store[k]);
        return n;
    }

    public static int StoreFree()
    {
        return Mathf.Max(0, Data.STORE_SLOTS - StoreUsed());
    }

    public static float StoreRoom(string ore)
    {
        float have = store[ore];
        return (Stacks(have) * Data.STACK - have) + StoreFree() * Data.STACK;
    }

    public static float StoreTotal()
    {
        float t = 0f;
        foreach (var k in Data.ORE_KEYS) t += store[k];
        return t;
    }

    /// Everything in the hold into the storage. Returns the units moved.
    public static float StowAll()
    {
        float moved = 0f;
        foreach (var k in Data.ORE_KEYS)
        {
            float u = Mathf.Min(cargo[k], StoreRoom(k));
            if (u > 0.01f)
            {
                cargo[k] -= u;
                store[k] += u;
                moved += u;
                if (cargo[k] < 0.01f) cargo[k] = 0f;
            }
        }
        return moved;
    }

    /// Everything in the storage back into the hold, as far as it fits. Returns the units moved.
    public static float TakeAll()
    {
        float moved = 0f;
        foreach (var k in Data.ORE_KEYS)
        {
            float u = Mathf.Min(store[k], CargoRoom(k));
            if (u > 0.01f)
            {
                store[k] -= u;
                cargo[k] += u;
                moved += u;
                if (store[k] < 0.01f) store[k] = 0f;
            }
        }
        return moved;
    }

    // ---- stacks: the inventory grids' view of a bag, most valuable ore first, full stacks before part stacks
    public class Stack { public string k; public float u; }

    public static List<Stack> Stacks(Dictionary<string, float> bag)
    {
        var keys = new List<string>();
        foreach (var k in Data.ORE_KEYS) if (bag[k] > 0.5f) keys.Add(k);
        keys.Sort((a, b) => Price(b).CompareTo(Price(a)));
        var outList = new List<Stack>();
        foreach (var k in keys)
        {
            float left = bag[k];
            while (left > 0.5f)
            {
                float u = Mathf.Min(Data.STACK, left);
                outList.Add(new Stack { k = k, u = u });
                left -= u;
            }
        }
        return outList;
    }

    /// Drop one stack of `k` (up to `units`) into space. Returns the units dropped.
    public static float Jettison(string k, float units)
    {
        float u = Mathf.Min(units, cargo[k]);
        cargo[k] -= u;
        if (cargo[k] < 0.01f) cargo[k] = 0f;
        Save();
        return u;
    }

    /// One stack of `k` from the hold into the storage, as far as it fits. Returns the units moved.
    public static float StowStack(string k, float units)
    {
        float u = Mathf.Min(units, Mathf.Min(cargo[k], StoreRoom(k)));
        if (u < 0.01f) return 0f;
        cargo[k] -= u;
        store[k] += u;
        if (cargo[k] < 0.01f) cargo[k] = 0f;
        return u;
    }

    /// One stack of `k` from the storage back into the hold, as far as it fits. Returns the units moved.
    public static float TakeStack(string k, float units)
    {
        float u = Mathf.Min(units, Mathf.Min(store[k], CargoRoom(k)));
        if (u < 0.01f) return 0f;
        store[k] -= u;
        cargo[k] += u;
        if (store[k] < 0.01f) store[k] = 0f;
        return u;
    }

    public static float Price(string k)
    {
        var o = Data.ORES[Data.OreIndex(k)];
        return o.price * market[k] * (o.zone != null ? Data.EXPORT_BONUS : 1f);
    }

    public static float ValueOf(Dictionary<string, float> bag)
    {
        float v = 0f;
        foreach (var k in Data.ORE_KEYS) v += bag[k] * Price(k);
        return v;
    }

    public static void TickMarket(float dt)
    {
        marketT -= dt;
        if (marketT <= 0f)
        {
            marketT = Data.MARKET_PERIOD;
            foreach (var k in Data.ORE_KEYS) marketNext[k] = UnityEngine.Random.Range(0.8f, 1.25f);
        }
        foreach (var k in Data.ORE_KEYS) market[k] += (marketNext[k] - market[k]) * Mathf.Min(1f, dt * 0.08f);
    }

    /// Sell the given ores from the hold and/or the storage. Returns the units sold and the credits made.
    public static float Sell(string[] keys, bool fromHold, bool fromStore, out float credits)
    {
        float cr = 0f, units = 0f;
        foreach (var k in keys)
        {
            float u = 0f;
            if (fromHold) { u += cargo[k]; cargo[k] = 0f; }
            if (fromStore) { u += store[k]; store[k] = 0f; }
            if (u > 0.01f) { cr += u * Price(k); units += u; }
        }
        if (units >= 0.5f)
        {
            State.credits += cr;
            earned += cr;
            Save();
        }
        credits = cr;
        return units;
    }

    /// Fill the cargo ship fuel supply at the colony, as far as credits go. Returns the units bought; msg says why not.
    public static float RefuelCargoShip(out float cost, out bool partial, out string msg)
    {
        float want = Data.CARGO_FUEL_CAP - shipFuel;
        float u = Mathf.Min(want, credits / Data.CARGO_FUEL_PRICE);
        cost = 0f; partial = false; msg = "";
        if (want < 0.5f) { msg = "Fuel supply is already full"; return 0f; }
        if (u < 1f) { msg = "Not enough credits for fuel"; return 0f; }
        shipFuel += u;
        cost = u * Data.CARGO_FUEL_PRICE;
        credits = Mathf.Max(0f, credits - cost);
        partial = u < want - 0.5f;
        Save();
        return u;
    }

    public static float BuyParts(out float cost, out bool partial, out string msg)
    {
        float want = Data.PARTS_CAP - parts;
        float n = Mathf.Min(want, Mathf.Floor(credits / Data.PARTS_PRICE));
        cost = 0f; partial = false; msg = "";
        if (want < 0.5f) { msg = "Repair parts store is already full"; return 0f; }
        if (n < 1f) { msg = "Not enough credits for repair parts"; return 0f; }
        parts += n;
        cost = n * Data.PARTS_PRICE;
        credits -= cost;
        partial = n < want - 0.5f;
        Save();
        return n;
    }

    // ---- the workshop (2026-09-20, the user's: "something else you can use resources for besides just selling them"):
    // ore turned into supplies and fittings. Docked, a recipe draws on the hold and the storage; in flight the hold alone,
    // and only the field recipes (the ones marked inFlight) are live.
    public class Recipe { public string id, name, effect; public string[] ores; public float[] units; public bool inFlight; }
    public static readonly Recipe[] RECIPES =
    {
        new Recipe { id = "fuel",    name = "Reactor fuel",     ores = new[] { "iron", "copper" },       units = new[] { 10f, 2f }, effect = "+80 fuel supply aboard the cargo ship", inFlight = false },
        new Recipe { id = "parts",   name = "Repair parts",     ores = new[] { "iron" },                 units = new[] { 8f },      effect = "+20 repair parts aboard the cargo ship", inFlight = false },
        new Recipe { id = "patch",   name = "Hull patch",       ores = new[] { "iron", "copper" },       units = new[] { 6f, 2f },  effect = "+20 hull, here and now", inFlight = true },
        new Recipe { id = "flares",  name = "Flare pack",       ores = new[] { "copper" },               units = new[] { 4f },      effect = "+2 flares, up to 8 aboard", inFlight = true },
        new Recipe { id = "rockets", name = "Rocket reload",    ores = new[] { "copper", "iron" },       units = new[] { 8f, 3f },  effect = "+2 seeker rockets, up to the magazine", inFlight = true },
        new Recipe { id = "lens",    name = "Voidcrystal lens", ores = new[] { "crystal", "platinum" },  units = new[] { 3f, 5f },  effect = "the laser cuts 25% faster, for good (up to 4 lenses)", inFlight = false },
    };
    public const int LENS_MAX = 4, FLARES_MAX = 8;
    public static int lens;   // Voidcrystal lenses fitted: each adds a quarter to the laser's cut rate (Ship.TickLaser); saved
    public static int FlaresAboard { get { return flares < 0 ? Ship.FLARES : flares; } }
    public static int RocketsAboard { get { return rockets < 0 ? Mathf.RoundToInt(Stat("rocket").slots) : rockets; } }

    public static float HaveOre(string k, bool docked) { return cargo[k] + (docked ? store[k] : 0f); }

    /// Ore out of the hold first, then the storage when docked.
    static void TakeOre(string k, float units, bool docked)
    {
        float fromHold = Mathf.Min(units, cargo[k]);
        cargo[k] -= fromHold;
        if (cargo[k] < 0.01f) cargo[k] = 0f;
        if (docked && units - fromHold > 0.01f)
        {
            store[k] = Mathf.Max(0f, store[k] - (units - fromHold));
            if (store[k] < 0.01f) store[k] = 0f;
        }
    }

    /// Whether a recipe can be made now; `why` says what is short or full when it cannot.
    public static bool CanMake(Recipe r, bool docked, out string why)
    {
        why = "";
        if (!r.inFlight && !docked) { why = "on the pad only"; return false; }
        var missing = new System.Text.StringBuilder();
        for (int i = 0; i < r.ores.Length; i++)
        {
            float have = HaveOre(r.ores[i], docked);
            if (have + 0.01f < r.units[i]) { if (missing.Length > 0) missing.Append(", "); missing.Append(Data.Fmt(r.units[i] - have)).Append(" more ").Append(Data.ORES[Data.OreIndex(r.ores[i])].name); }
        }
        if (missing.Length > 0) { why = "needs " + missing; return false; }
        switch (r.id)
        {
            case "fuel": if (shipFuel >= Data.CARGO_FUEL_CAP - 0.5f) { why = "the fuel supply is full"; return false; } break;
            case "parts": if (parts >= Data.PARTS_CAP - 0.5f) { why = "the parts store is full"; return false; } break;
            case "patch": if (hull >= Stat("hull").hp - 0.5f) { why = "the hull is whole"; return false; } break;
            case "flares": if (FlaresAboard >= FLARES_MAX) { why = "the flare racks are full"; return false; } break;
            case "rockets": if (RocketsAboard >= Mathf.RoundToInt(Stat("rocket").slots)) { why = "the magazine is full"; return false; } break;
            case "lens": if (lens >= LENS_MAX) { why = "no room for another lens"; return false; } break;
        }
        return true;
    }

    /// Makes the recipe: the ore taken, the result applied, the save written. Returns the toast.
    public static string Make(Recipe r, bool docked)
    {
        string why;
        if (!CanMake(r, docked, out why)) return "Cannot make " + r.name.ToLowerInvariant() + " · " + why;
        for (int i = 0; i < r.ores.Length; i++) TakeOre(r.ores[i], r.units[i], docked);
        string got;
        switch (r.id)
        {
            case "fuel": { float add = Mathf.Min(80f, Data.CARGO_FUEL_CAP - shipFuel); shipFuel += add; got = "+" + Data.Fmt(add) + " fuel supply · " + Data.Fmt(shipFuel) + " aboard"; break; }
            case "parts": { float add = Mathf.Min(20f, Data.PARTS_CAP - parts); parts += add; got = "+" + Data.Fmt(add) + " repair parts · " + Data.Fmt(parts) + " aboard"; break; }
            case "patch": { float max = Stat("hull").hp; float add = Mathf.Min(20f, max - hull); hull += add; got = "+" + Data.Fmt(add) + " hull · " + Mathf.RoundToInt(hull) + " / " + Mathf.RoundToInt(max); break; }
            case "flares": { flares = Mathf.Min(FLARES_MAX, FlaresAboard + 2); got = flares + " flares aboard"; break; }
            case "rockets": { int mag = Mathf.RoundToInt(Stat("rocket").slots); rockets = Mathf.Min(mag, RocketsAboard + 2); got = rockets + " / " + mag + " rockets aboard"; break; }
            case "lens": { lens++; got = "the laser cuts " + (25 * lens) + "% faster · " + lens + (lens == 1 ? " lens" : " lenses") + " fitted"; break; }
            default: got = ""; break;
        }
        Save();
        return "Made " + r.name.ToLowerInvariant() + " · " + got;
    }

    /// Storage room for any ore at all (a drone only goes out if there is somewhere to put what it brings back).
    public static bool StoreAnyRoom()
    {
        if (StoreFree() > 0) return true;
        foreach (var k in Data.ORE_KEYS)
        {
            if (store[k] > 0.5f && Stacks(store[k]) * Data.STACK - store[k] > 0.5f) return true;
        }
        return false;
    }

    /// Buy the next level of a cargo ship upgrade.
    public static bool BuyDepot(string key, out string msg)
    {
        var u = Data.DEPOT_UPGRADES[key];
        int i = depot[key];
        if (i >= u.costs.Length) { msg = u.name + " is fully upgraded"; return false; }
        float c = u.costs[i];
        if (credits < c) { msg = "Not enough credits"; return false; }
        credits -= c;
        depot[key] = i + 1;
        Save();
        msg = u.name + " " + (i == 0 ? "installed" : "upgraded to Lv" + (i + 1));
        return true;
    }

    /// Buy the next level of a refit. Returns whether it went through, and a message for the toast.
    public static bool Buy(string key, out string msg)
    {
        var u = Data.UPGRADES[key];
        int i = up[key];
        if (i >= u.costs.Length) { msg = u.name + " is fully upgraded"; return false; }
        float c = u.costs[i];
        if (credits < c) { msg = "Not enough credits"; return false; }
        credits -= c;
        up[key] = i + 1;
        if (key == "hull") hull = Stat("hull").hp;
        if (key == "shield") shield = ShieldMax;
        Save();
        msg = u.name + " upgraded to Lv" + (i + 2);
        return true;
    }

    /// Take a level off a refit (the combat test's minus button): the price of that level comes back.
    public static bool Downgrade(string key, out string msg)
    {
        var u = Data.UPGRADES[key];
        int i = up[key];
        if (i <= 0) { msg = u.name + " is at the base level"; return false; }
        up[key] = i - 1;
        credits += u.costs[i - 1];
        if (key == "hull") hull = Mathf.Min(hull, Stat("hull").hp);
        Save();
        msg = u.name + " back to Lv" + i;
        return true;
    }

    // ---- the save file: the browser's field names, written by JsonUtility through a mirror of the save object
    [Serializable] public class Bag { public float iron, copper, gold, platinum, crystal, cobalt, beryl; }
    [Serializable] public class Ups { public int laser, cargo, engine, tank, scanner, range, hull, shield, thrusters, overcharge, gun, rocket; }
    [Serializable] public class Dep { public int laser, collectors; }
    [Serializable] public class Settings { public bool sound = true; public float volume = 1f; public bool music = true; public float music_volume = 1f; public float hud = 1f; public bool controls = true; public int display = 1; public float brightness = 1f; public bool cockpit; }
    [Serializable]
    public class SaveData
    {
        public float credits, fuel, hull, mined, earned, time, shipFuel = 1200f, parts = 120f, shield = Data.SHIELD_MAX;
        public Bag cargo, store, market;
        public Ups up;
        public Dep depot;
        public float droneUnits;
        public string zone;
        public int tut;
        public int rockets = -1;
        public int flares = -1;
        public int lens;
        public int raid, raids;
        public float raidAt;
        public bool raidStation;
        public Settings settings;
    }

    static Bag ToBag(Dictionary<string, float> d)
    {
        return new Bag { iron = d["iron"], copper = d["copper"], gold = d["gold"], platinum = d["platinum"], crystal = d["crystal"], cobalt = d["cobalt"], beryl = d["beryl"] };
    }

    static void FromBag(Bag b, Dictionary<string, float> d)
    {
        if (b == null) return;
        d["iron"] = b.iron; d["copper"] = b.copper; d["gold"] = b.gold; d["platinum"] = b.platinum; d["crystal"] = b.crystal; d["cobalt"] = b.cobalt; d["beryl"] = b.beryl;
    }

    public static void Save()
    {
        if (sandbox) return;
        var s = new SaveData
        {
            credits = credits, fuel = fuel, hull = hull, shield = shield, mined = mined, earned = earned, time = time, shipFuel = shipFuel, parts = parts,
            cargo = ToBag(cargo), store = ToBag(store), market = ToBag(market),
            up = new Ups { laser = up["laser"], cargo = up["cargo"], engine = up["engine"], tank = up["tank"], scanner = up["scanner"], range = up["range"], hull = up["hull"], thrusters = up["thrusters"], overcharge = up["overcharge"], gun = up["gun"], rocket = up["rocket"], shield = up["shield"] },
            depot = new Dep { laser = depot["laser"], collectors = depot["collectors"] }, droneUnits = droneUnits,
            zone = zoneId, tut = tut, rockets = rockets, flares = flares, lens = lens, raid = raid, raids = raids, raidAt = raidAt, raidStation = raidStation, settings = new Settings { sound = soundOn, volume = volume, music = musicOn, music_volume = musicVolume, hud = hudScale, controls = controlsShown, display = display, brightness = brightness, cockpit = cockpitView },
        };
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(s));
            hasSave = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("save failed: " + e.Message);
        }
    }

    public static bool Load()
    {
        if (!File.Exists(SavePath)) return false;
        SaveData s;
        try { s = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
        catch (Exception) { return false; }
        if (s == null) return false;
        credits = s.credits;
        fuel = s.fuel;
        hull = s.hull;
        shield = Mathf.Max(0f, s.shield);   // clipped to the fitted shield once the refits are in, below
        shipFuel = Mathf.Min(Data.CARGO_FUEL_CAP, s.shipFuel);
        parts = Mathf.Min(Data.PARTS_CAP, s.parts);
        mined = s.mined;
        earned = s.earned;
        time = s.time;
        zoneId = string.IsNullOrEmpty(s.zone) ? "kessler" : s.zone;
        tut = s.tut;
        rockets = s.rockets;
        flares = s.flares;
        lens = s.lens;
        raid = s.raid; raids = s.raids; raidAt = s.raidAt; raidStation = s.raidStation;
        FromBag(s.cargo, cargo);
        FromBag(s.store, store);
        FromBag(s.market, market);
        foreach (var k in Data.ORE_KEYS) marketNext[k] = market[k];
        if (s.up != null)
        {
            // an older save's hull was 100 at the first plating; it is 50 now
            up["laser"] = s.up.laser; up["cargo"] = s.up.cargo; up["engine"] = s.up.engine; up["tank"] = s.up.tank; up["scanner"] = s.up.scanner;
            up["range"] = s.up.range; up["hull"] = s.up.hull; up["thrusters"] = s.up.thrusters; up["overcharge"] = s.up.overcharge; up["gun"] = s.up.gun; up["rocket"] = s.up.rocket; up["shield"] = s.up.shield;
            foreach (var k in Data.UPGRADE_KEYS) up[k] = Mathf.Clamp(up[k], 0, Data.UPGRADES[k].levels.Length - 1);
        }
        shield = Mathf.Min(shield, ShieldMax);
        if (s.depot != null)
        {
            depot["laser"] = Mathf.Clamp(s.depot.laser, 0, Data.DEPOT_UPGRADES["laser"].costs.Length);
            depot["collectors"] = Mathf.Clamp(s.depot.collectors, 0, Data.DEPOT_UPGRADES["collectors"].costs.Length);
        }
        droneUnits = s.droneUnits;
        hull = Mathf.Min(hull, Stat("hull").hp);
        if (s.settings != null) { display = Mathf.Clamp(s.settings.display, 0, 2); soundOn = s.settings.sound; volume = s.settings.volume; musicOn = s.settings.music; musicVolume = s.settings.music_volume; hudScale = s.settings.hud > 0f ? s.settings.hud : 1f; controlsShown = s.settings.controls; cockpitView = s.settings.cockpit; brightness = s.settings.brightness > 0f ? Mathf.Clamp(s.settings.brightness, 0.5f, 1.5f) : 1f; }
        hasSave = true;
        return true;
    }

    /// A fresh pilot: the browser's resetSave. The settings survive; the save file goes.
    public static void Reset()
    {
        credits = 60f;
        fuel = 100f;
        hull = Stat("hull").hp;
        shield = Data.SHIELD_MAX;
        sinceHit = 99f;
        shipFuel = 1200f;
        parts = 120f;
        foreach (var k in Data.ORE_KEYS) { cargo[k] = 0f; store[k] = 0f; market[k] = 1f; marketNext[k] = 1f; }
        foreach (var k in Data.UPGRADE_KEYS) up[k] = 0;
        foreach (var k in Data.DEPOT_KEYS) depot[k] = 0;
        droneUnits = 0f;
        marketT = 0f;
        zoneId = "kessler";
        rockets = -1;
        flares = -1;
        lens = 0;
        raid = 0; raids = 0; raidAt = 0f; raidStation = false;
        tut = 0;
        mined = 0f;
        earned = 0f;
        time = 0f;
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (Exception) { }
        hasSave = false;
    }
}
