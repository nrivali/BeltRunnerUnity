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
    public static float hull = 100f;
    public static float shipFuel = 1200f;   // the cargo ship's fuel supply, which the ship's tank fills from while docked
    public static float parts = 120f;       // repair parts aboard the cargo ship, one per hull point mended while docked
    public static Dictionary<string, int> up = new Dictionary<string, int>();
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
    public static bool hasSave = false;

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
        Load();
    }

    /// The level entry of a refit, e.g. Stat("engine").max
    public static Data.Level Stat(string key)
    {
        return Data.UPGRADES[key].levels[up[key]];
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
            marketT = 90f;
            foreach (var k in Data.ORE_KEYS) marketNext[k] = UnityEngine.Random.Range(0.8f, 1.25f);
        }
        foreach (var k in Data.ORE_KEYS) market[k] += (marketNext[k] - market[k]) * Mathf.Min(1f, dt * 0.08f);
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
        Save();
        msg = u.name + " refit to Lv" + (i + 2);
        return true;
    }

    // ---- the save file: the browser's field names, written by JsonUtility through a mirror of the save object
    [Serializable] public class Bag { public float iron, copper, gold, platinum, crystal, cobalt, beryl; }
    [Serializable] public class Ups { public int laser, cargo, engine, tank, scanner, range, hull, thrusters, overcharge; }
    [Serializable] public class Settings { public bool sound = true; public float volume = 1f; }
    [Serializable]
    public class SaveData
    {
        public float credits, fuel, hull, mined, earned, time, shipFuel = 1200f, parts = 120f;
        public Bag cargo, store, market;
        public Ups up;
        public string zone;
        public int tut;
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
        var s = new SaveData
        {
            credits = credits, fuel = fuel, hull = hull, mined = mined, earned = earned, time = time, shipFuel = shipFuel, parts = parts,
            cargo = ToBag(cargo), store = ToBag(store), market = ToBag(market),
            up = new Ups { laser = up["laser"], cargo = up["cargo"], engine = up["engine"], tank = up["tank"], scanner = up["scanner"], range = up["range"], hull = up["hull"], thrusters = up["thrusters"], overcharge = up["overcharge"] },
            zone = zoneId, tut = tut, settings = new Settings { sound = soundOn, volume = volume },
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
        shipFuel = Mathf.Min(Data.CARGO_FUEL_CAP, s.shipFuel);
        parts = Mathf.Min(Data.PARTS_CAP, s.parts);
        mined = s.mined;
        earned = s.earned;
        time = s.time;
        zoneId = string.IsNullOrEmpty(s.zone) ? "kessler" : s.zone;
        tut = s.tut;
        FromBag(s.cargo, cargo);
        FromBag(s.store, store);
        FromBag(s.market, market);
        foreach (var k in Data.ORE_KEYS) marketNext[k] = market[k];
        if (s.up != null)
        {
            up["laser"] = s.up.laser; up["cargo"] = s.up.cargo; up["engine"] = s.up.engine; up["tank"] = s.up.tank; up["scanner"] = s.up.scanner;
            up["range"] = s.up.range; up["hull"] = s.up.hull; up["thrusters"] = s.up.thrusters; up["overcharge"] = s.up.overcharge;
            foreach (var k in Data.UPGRADE_KEYS) up[k] = Mathf.Clamp(up[k], 0, Data.UPGRADES[k].levels.Length - 1);
        }
        if (s.settings != null) { soundOn = s.settings.sound; volume = s.settings.volume; }
        hasSave = true;
        return true;
    }

    /// A fresh pilot: the browser's resetSave. The settings survive; the save file goes.
    public static void Reset()
    {
        credits = 60f;
        fuel = 100f;
        hull = 100f;
        shipFuel = 1200f;
        parts = 120f;
        foreach (var k in Data.ORE_KEYS) { cargo[k] = 0f; store[k] = 0f; market[k] = 1f; marketNext[k] = 1f; }
        foreach (var k in Data.UPGRADE_KEYS) up[k] = 0;
        marketT = 0f;
        zoneId = "kessler";
        tut = 0;
        mined = 0f;
        earned = 0f;
        time = 0f;
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (Exception) { }
        hasSave = false;
    }
}
