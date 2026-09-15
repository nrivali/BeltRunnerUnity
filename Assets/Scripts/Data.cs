using System.Collections.Generic;
using UnityEngine;

/// Static game data, ported one for one from belt-runner-3d.html (v0.9.120). Distances are the browser game's world
/// units, and a readout metre is half a unit (METRE), exactly as the HTML shows it.
public static class Data
{
    public const float METRE = 0.5f;
    public const int STACK = 100;
    public const float WORLD_SCALE = 100f;
    public const float PLANET_SCALE = 250f;
    public const float WORLD_EDGE_BASE = 11800f;
    public const float GRAVITY_SURFACE = 55f;
    public const float FUEL_BURN = 0.55f;
    public const float OVER_BURN = 1.1f;
    public const float SHIP_SCALE = 3f;
    public const float SHIP_R = 16f * SHIP_SCALE;
    public const float PULSE_CD = 5f;
    public const float PULSE_TIME = 2.6f;
    public const float SHIELD_MAX = 50f;      // the ship's shield: soaks damage first, recharges after SHIELD_WAIT quiet seconds
    public const float SHIELD_WAIT = 10f;
    public const float SHIELD_RATE = SHIELD_MAX / 3f;   // points a second: empty to full in three seconds
    public const string VERSION = "0.9.120-unity";
    /// The cargo ship: its orbit round the planet (inside the ring belt), fuel supply and repair-parts store, and 50-slot storage.
    public const float DEPOT_ORBIT = 925000f;
    public const float STATION_SPEED = 102f;
    public const float CARGO_FUEL_CAP = 2500f;
    public const int PARTS_CAP = 400;
    public const int STORE_SLOTS = 50;
    public const float DOCK_RANGE = 4500f;   // E within this of the carrier hands the ship to approach control (2,250 m on the readout)
    /// The colony market: fuel and repair parts for the cargo ship, and how prices drift.
    public const float CARGO_FUEL_PRICE = 0.6f;
    public const float PARTS_PRICE = 2f;
    public const float MARKET_PERIOD = 90f;
    /// Where the cargo ship holds station off Meridian Colony (true world coordinates) and the heading it holds, nose toward the hub.
    public static readonly Vector3 HOLD_PARK = new Vector3(-40000f, 14000f, 56000f);
    public static readonly Vector3 HOLD_DIR = new Vector3(40000f, -9000f, -56000f);
    /// Meridian Colony proportions (the HTML COLONY constants).
    public const float COL_R = 52000f, COL_RING_W = 3000f, COL_RING_H = 2600f, COL_R2 = 30000f, COL_RING2_W = 1800f, COL_RING2_H = 1800f, COL_HUB = 7000f, COL_CORE_R = 2600f, COL_CORE_H = 24000f, COL_PAD_Y = 15000f, COL_PAD_R = 5200f, COL_BERTH_Y = 12000f, COL_TERM_X = 4200f, COL_TERM_Y = 1500f, COL_TERM_Z = 3200f, COL_BERTH_Z = 6230f;

    public class Ore
    {
        public string key, name, rarity, zone;
        public Color color;
        public float price;
        public int hardness, unlock;
        public Ore(string key, string name, string hex, float price, int hardness, int unlock, string rarity, string zone = null)
        {
            this.key = key; this.name = name; this.price = price; this.hardness = hardness; this.unlock = unlock; this.rarity = rarity; this.zone = zone;
            color = Hex(hex);
        }
    }

    public static readonly Ore[] ORES =
    {
        new Ore("iron",     "Iron",        "#A3ABBC", 4,   1, 2, "Common"),
        new Ore("copper",   "Copper",      "#D0834C", 9,   1, 1, "Uncommon"),
        new Ore("gold",     "Gold",        "#F2C94C", 28,  2, 3, "Rare"),
        new Ore("platinum", "Platinum",    "#35D6C2", 70,  3, 4, "Epic"),
        new Ore("crystal",  "Voidcrystal", "#B98CFF", 160, 4, 5, "Ultra rare"),
        new Ore("cobalt",   "Cobalt",      "#3F7FE0", 16,  1, 2, "Exclusive", "kessler"),
        new Ore("beryl",    "Beryl",       "#2FBF71", 45,  2, 3, "Exclusive", "kessler"),
    };
    public static readonly string[] ORE_KEYS = { "iron", "copper", "gold", "platinum", "crystal", "cobalt", "beryl" };
    public const float EXPORT_BONUS = 1.5f;

    public static int OreIndex(string key)
    {
        for (int i = 0; i < ORE_KEYS.Length; i++) if (ORE_KEYS[i] == key) return i;
        return -1;
    }

    /// One refit level: only the fields that level defines are meaningful (the HTML's per-key level objects).
    public class Level
    {
        public float rate, thrust, max, cap, range, reach, hp, mult;
        public int slots;
    }

    public class Upgrade
    {
        public string name;
        public Level[] levels;
        public float[] costs;
    }

    static Level L(float rate = 0, int slots = 0, float thrust = 0, float max = 0, float cap = 0, float range = 0, float reach = 0, float hp = 0, float mult = 0)
    {
        return new Level { rate = rate, slots = slots, thrust = thrust, max = max, cap = cap, range = range, reach = reach, hp = hp, mult = mult };
    }

    public static readonly Dictionary<string, Upgrade> UPGRADES = new Dictionary<string, Upgrade>
    {
        { "laser", new Upgrade { name = "Mining laser", levels = new[] { L(rate: 3), L(rate: 5), L(rate: 8), L(rate: 12), L(rate: 18) }, costs = new float[] { 350, 1400, 5000, 16000 } } },
        { "cargo", new Upgrade { name = "Cargo hold", levels = new[] { L(slots: 4), L(slots: 6), L(slots: 8), L(slots: 11), L(slots: 14), L(slots: 18) }, costs = new float[] { 200, 700, 2400, 7500, 20000 } } },
        { "engine", new Upgrade { name = "Engines", levels = new[] { L(thrust: 164, max: 250), L(thrust: 219, max: 320), L(thrust: 281, max: 400), L(thrust: 359, max: 490), L(thrust: 461, max: 610) }, costs = new float[] { 300, 1100, 3500, 10000 } } },
        { "tank", new Upgrade { name = "Fuel tank", levels = new[] { L(cap: 100), L(cap: 160), L(cap: 250), L(cap: 400), L(cap: 600) }, costs = new float[] { 150, 600, 2000, 6000 } } },
        { "scanner", new Upgrade { name = "Scanner", levels = new[] { L(range: 28000), L(range: 46000), L(range: 74000), L(range: 135000) }, costs = new float[] { 400, 1800, 6000 } } },
        { "range", new Upgrade { name = "Laser range", levels = new[] { L(reach: 2500), L(reach: 3500), L(reach: 5000), L(reach: 7000) }, costs = new float[] { 2500, 9000, 25000 } } },
        { "hull", new Upgrade { name = "Hull plating", levels = new[] { L(hp: 50), L(hp: 80), L(hp: 125), L(hp: 200), L(hp: 300) }, costs = new float[] { 250, 900, 3000, 9000 } } },
        { "thrusters", new Upgrade { name = "Afterburner", levels = new[] { L(mult: 1), L(mult: 2), L(mult: 3), L(mult: 4), L(mult: 5) }, costs = new float[] { 800, 3000, 9000, 24000 } } },
        { "overcharge", new Upgrade { name = "Laser overcharge", levels = new[] { L(mult: 1f), L(mult: 1.5f), L(mult: 2f), L(mult: 2.5f), L(mult: 3f) }, costs = new float[] { 600, 2200, 7000, 18000 } } },
        // the autocannon (combat), fitted from the start: rate = shots a second, reach = range in world units, mult = damage a shot
        { "gun", new Upgrade { name = "Autocannon", levels = new[] { L(rate: 6, reach: 10000, mult: 8), L(rate: 7, reach: 10000, mult: 12), L(rate: 8, reach: 10000, mult: 18), L(rate: 10, reach: 10000, mult: 26) }, costs = new float[] { 900, 3200, 9000 } } },
    };
    public static readonly string[] UPGRADE_KEYS = { "laser", "cargo", "engine", "tank", "scanner", "range", "hull", "thrusters", "overcharge", "gun" };

    /// Cargo ship upgrades, bought at the services panel; they work whether or not you are docked. Level 0 = not
    /// installed. The dish on the mast breaks rocks near the carrier and leaves their ore adrift; collector drones fly
    /// out from the hangar, gather loose ore and stow it in the cargo ship storage.
    public class DepotLevel { public float range, rate, cap, speed; public int ships; }
    public class DepotUpgrade { public string name; public DepotLevel[] levels; public float[] costs; }
    public static readonly Dictionary<string, DepotUpgrade> DEPOT_UPGRADES = new Dictionary<string, DepotUpgrade>
    {
        { "laser", new DepotUpgrade { name = "Cargo ship mining laser", levels = new DepotLevel[] { null, new DepotLevel { range = 12000f, rate = 2f }, new DepotLevel { range = 20000f, rate = 4f }, new DepotLevel { range = 32000f, rate = 7f } }, costs = new float[] { 5000, 14000, 40000 } } },
        { "collectors", new DepotUpgrade { name = "Collector drones", levels = new DepotLevel[] { null, new DepotLevel { ships = 1, cap = 120f, speed = 260f, range = 20000f }, new DepotLevel { ships = 2, cap = 180f, speed = 300f, range = 35000f }, new DepotLevel { ships = 3, cap = 260f, speed = 340f, range = 60000f } }, costs = new float[] { 8000, 20000, 45000 } } },
    };
    public static readonly string[] DEPOT_KEYS = { "laser", "collectors" };
    public const float TURRET_PITCH_MIN = -0.5f;
    public const float TURRET_PITCH_MAX = 1.45f;
    public const float TURRET_SLEW = 0.45f;   // rad/s; yaw is unlimited

    public static string DescribeDepot(string key, int i)
    {
        var L = DEPOT_UPGRADES[key].levels[i];
        if (L == null) return "not installed";
        if (key == "laser") return Fm(L.range) + " m reach · " + L.rate + " dmg/s";
        return L.ships + " drone" + (L.ships > 1 ? "s" : "") + " · " + Mathf.RoundToInt(L.cap) + " hold · " + Fm(L.range) + " m range";
    }

    /// Fuel burn multiplier for an afterburner setting (0.8 x mult squared: x3.2 at x2, x20 at x5).
    public static float BurnMult(float m)
    {
        return m > 1f ? 0.8f * m * m : 1f;
    }

    /// What a refit level gives, for the services panel (the HTML's describe()).
    public static string Describe(string key, int i)
    {
        var L = UPGRADES[key].levels[i];
        switch (key)
        {
            case "laser": return L.rate + " dmg/s";
            case "cargo": return L.slots + " slots";
            case "engine": return Mathf.RoundToInt(L.thrust * METRE) + " thrust · " + Mathf.RoundToInt(L.max * METRE) + " top speed";
            case "thrusters": return L.mult > 1 ? "×" + L.mult + " speed on Shift · ×" + BurnMult(L.mult) + " fuel burn" : "not fitted";
            case "gun": return L.reach > 0f ? Mathf.RoundToInt(L.mult) + " dmg × " + L.rate + " /s · " + Fm(L.reach) + " m range" : "not fitted";
            case "overcharge": return L.mult > 1f ? "×" + L.mult + " laser damage · " + (OVER_BURN * L.mult).ToString("0.0") + " fuel/s while cutting" : "not fitted";
            case "tank": return L.cap + " fuel";
            case "scanner": return Fm(L.range) + " m scan";
            case "range": return Fm(L.reach) + " m laser reach";
            case "hull": return L.hp + " hull points";
        }
        return "";
    }

    /// A belt: radii in base units out from the planet's surface (Belt.Build scales them), a rock count, size and
    /// amount ranges, a vertical spread, and ore weights.
    public class BeltDef
    {
        public string name;
        public float rMin, rMax, spread, amountMult = 1f;
        public int count;
        public float size0, size1, amount0, amount1;
        public Dictionary<string, float> ores;
        public BeltDef Clone()
        {
            return (BeltDef)MemberwiseClone();
        }
    }

    public static readonly BeltDef[] BASE_BELTS =
    {
        new BeltDef { name = "Inner belt", rMin = 600f,  rMax = 3600f,  count = 1000, size0 = 18, size1 = 62, amount0 = 35, amount1 = 110, spread = 650f },
        new BeltDef { name = "Mid belt",   rMin = 3800f, rMax = 7000f,  count = 925,  size0 = 20, size1 = 68, amount0 = 45, amount1 = 150, spread = 900f },
        new BeltDef { name = "Outer belt", rMin = 7200f, rMax = 10500f, count = 800,  size0 = 22, size1 = 74, amount0 = 60, amount1 = 210, spread = 1150f },
    };
    /// The ring belt sits at a fixed distance from the planet's centre (700 km above Ferron's surface), 50 km wide and thin.
    public static readonly BeltDef RING_BELT = new BeltDef
    {
        name = "Ring belt", rMin = 900000f, rMax = 950000f, count = 5600, size0 = 16, size1 = 54, amount0 = 30, amount1 = 95, spread = 9000f,
        ores = new Dictionary<string, float> { { "iron", 0.48f }, { "copper", 0.3f }, { "gold", 0.12f }, { "platinum", 0.06f }, { "crystal", 0.04f } }
    };

    public class Zone
    {
        public string id, name, tag, planetName, colony;
        public bool hub, central;
        public float density, amountMult, planetR;
        public float danger;   // raider holds by the rich pockets: their number, health, aim and pay scale with it
        public Dictionary<string, float>[] belts;
        public Color tint, accent, bg;
        public Vector3 sunDir, planetPos;
        public Vector2 map;
    }

    public static readonly Zone ZONE_KESSLER = new Zone
    {
        id = "kessler", name = "Kessler Belt", hub = false, map = new Vector2(46, 34), accent = Hex("#F2A33A"), danger = 0.5f,
        tag = "The home belt. Picked over, safe, and never far from a refuel.",
        density = 5f, amountMult = 1f,
        belts = new[]
        {
            new Dictionary<string, float> { { "iron", 0.55f }, { "copper", 0.25f }, { "cobalt", 0.2f } },
            new Dictionary<string, float> { { "copper", 0.3f }, { "gold", 0.4f }, { "platinum", 0.1f }, { "cobalt", 0.1f }, { "beryl", 0.1f } },
            new Dictionary<string, float> { { "gold", 0.15f }, { "platinum", 0.4f }, { "crystal", 0.25f }, { "beryl", 0.2f } },
        },
        planetName = "Ferron", planetR = 900f, tint = Hex("#7E5F4B"), central = true, planetPos = Vector3.zero,
        sunDir = new Vector3(0.55f, 0.42f, -0.72f), bg = Hex("#070912"),
    };
    /// The Hub: Meridian Colony at the origin, no belts and no central gravity; the homeworld hangs below the colony lanes.
    public static readonly Zone ZONE_HUB = new Zone
    {
        id = "hub", name = "The Hub", hub = true, colony = "Meridian Colony", map = new Vector2(50, 57), accent = Hex("#6BD69A"),
        tag = "Meridian Colony above a blue ocean world. Green continents, white clouds, and familiar lights on the night side: a home to return to, with safe lanes and the sector ore market.",
        density = 0f, amountMult = 1f,
        belts = new[] { new Dictionary<string, float>(), new Dictionary<string, float>(), new Dictionary<string, float>() },
        planetName = "Meridian", planetR = 1280f, tint = Hex("#3C86B5"), central = false, planetPos = new Vector3(150000f, -430000f, -360000f),
        sunDir = new Vector3(-0.85f, 0.45f, 0.10f), bg = Hex("#080a14"),
    };
    public static readonly Zone[] ZONES = { ZONE_HUB, ZONE_KESSLER };

    /// Light-years between two zones, from their chart positions.
    public static float ZoneLy(Zone a, Zone b)
    {
        return Mathf.Round(Vector2.Distance(a.map, b.map) * 0.12f * 10f) / 10f;
    }

    public static Zone ZoneById(string id)
    {
        foreach (var z in ZONES) if (z.id == id) return z;
        return ZONE_KESSLER;
    }

    public static Color Hex(string hex)
    {
        Color c;
        if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.magenta;
        return c;
    }

    /// A number with thousands separators.
    public static string Fmt(float n)
    {
        var s = Mathf.RoundToInt(n).ToString();
        var neg = s.StartsWith("-");
        if (neg) s = s.Substring(1);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(s[i]);
        }
        return (neg ? "-" : "") + sb;
    }

    /// A readout distance: world units to metres, with thousands separators.
    public static string Fm(float n)
    {
        return Fmt(n * METRE);
    }
}
