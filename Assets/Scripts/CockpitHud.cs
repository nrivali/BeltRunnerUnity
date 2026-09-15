using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// The flight console: one authored metal housing, with crisp live UGUI instruments above it.
/// No extra camera, lights, render textures or per-frame belt-wide searches are needed.
public sealed class CockpitHud
{
    public RectTransform root, status, flight, target;
    public float Height { get; private set; }
    public bool Visible { get { return root != null && root.gameObject.activeInHierarchy; } }
    readonly Hud hud;
    readonly RectTransform scanner, cargo, commands;
    readonly RectTransform[] panels;
    readonly float[] centers = { .115f, .276f, .5f, .729f, .89f };
    readonly Ui.Gauge hull, shield, fuel, thrust, load, integrity;
    readonly Text speed, mode, zone, heading, carrierRange, systems, radarRange, radarState;
    readonly Text targetName, targetInfo, targetState, weapon, weaponState, cargoValue, cargoRows, droneValue, droneState;
    readonly Text credits, chargeLabel;
    readonly Scope scope;
    readonly Attitude attitude;
    readonly Diagram targetDiagram, shipDiagram;
    readonly List<Button> flightButtons = new List<Button>();
    float refresh;
    static readonly Color Ink = Data.Hex("#061014"), White = Data.Hex("#eee8d6"), Dim = Data.Hex("#8daeb5"), Cyan = Data.Hex("#75dbe8"), Amber = Data.Hex("#ffd38b");

    public CockpitHud(RectTransform parent, Hud owner)
    {
        hud = owner;
        root = Ui.Rect("Cockpit console", parent, Ui.BC, Ui.BC, Vector2.zero, new Vector2(1280, 300));
        root.anchorMin = Ui.BL; root.anchorMax = Ui.BR; root.sizeDelta = new Vector2(0, 300);
        var surface = Ui.Stretch("Machined housing", root).gameObject.AddComponent<RawImage>();
        surface.texture = Resources.Load<Texture2D>("Cockpit/console");
        surface.raycastTarget = true; // the housing itself catches clicks, not the world behind it
        if (surface.texture == null) Debug.LogWarning("cockpit: missing Resources/Cockpit/console");
        status = Panel("Ship status", 188); scanner = Panel("Scanner", 156);
        flight = Panel("Flight instruments", 346); target = Panel("Target", 160); cargo = Panel("Cargo and drones", 178);
        panels = new[] { status, scanner, flight, target, cargo };
        Title(status, "SHIP STATUS", 188);
        hull = Gauge(status, "HULL", 22, 188, Cyan);
        shield = Gauge(status, "SHIELD", 55, 188, Cyan);
        fuel = Gauge(status, "FUEL", 88, 188, Amber);
        Rule(status, 121, 188);
        shipDiagram = Graphic<Diagram>(status, "Ship diagnostic", 0, 127, 63, 55);
        shipDiagram.kind = 1; shipDiagram.tint = Cyan;
        systems = Text(status, "", 9, 73, 127, 115, 50, Dim);
        Title(scanner, "LOCAL SCANNER", 156);
        scope = Graphic<Scope>(scanner, "Local contacts", 7, 19, 142, 142);
        radarRange = Text(scanner, "", 10, 0, 162, 156, 14, White);
        radarState = Text(scanner, "", 9, 0, 178, 156, 12, Dim);
        zone = Text(flight, "", 12, 0, 0, 346, 17, Cyan);
        mode = Text(flight, "", 9, 0, 18, 346, 12, Dim);
        Rule(flight, 33, 346);
        speed = Text(flight, "000", 32, 4, 35, 200, 42, White, "mono_semi");
        Text(flight, "m/s", 12, 132, 54, 38, 16, Dim);
        thrust = Gauge(flight, "THROTTLE", 44, 126, Amber, 220);
        attitude = Graphic<Attitude>(flight, "Flight director", 26, 83, 294, 80);
        heading = Text(flight, "", 10, 0, 167, 346, 14, White, "mono", TextAnchor.UpperCenter);
        carrierRange = Text(flight, "", 9, 0, 184, 346, 12, Dim, "mono", TextAnchor.UpperCenter);
        Title(target, "TARGET", 160);
        targetDiagram = Graphic<Diagram>(target, "Target scan", 0, 27, 60, 63);
        targetName = Text(target, "", 12, 67, 26, 93, 34, Amber, "display");
        targetName.horizontalOverflow = HorizontalWrapMode.Wrap;
        targetInfo = Text(target, "", 10, 67, 64, 93, 34, White);
        targetState = Text(target, "", 9, 0, 100, 160, 14, Dim);
        integrity = Gauge(target, "INTEGRITY", 117, 160, Amber);
        Rule(target, 153, 160);
        weapon = Text(target, "", 10, 0, 159, 160, 14, Dim);
        weaponState = Text(target, "", 10, 0, 177, 160, 18, Ui.GREEN);
        Title(cargo, "CARGO", 178);
        cargoValue = Text(cargo, "", 17, 0, 22, 178, 23, White, "mono_semi");
        load = Gauge(cargo, "HOLD", 49, 178, Amber);
        cargoRows = Text(cargo, "", 10, 0, 83, 178, 36, Dim);
        Rule(cargo, 121, 178);
        Text(cargo, "COLLECTOR DRONES", 10, 0, 129, 150, 14, Dim);
        droneValue = Text(cargo, "", 20, 0, 146, 60, 27, White, "mono_semi");
        droneState = Text(cargo, "", 10, 63, 150, 115, 35, Ui.GREEN);
        commands = Ui.Rect("Command rail", root, Ui.BC, Ui.BC, Vector2.zero, new Vector2(580, 34));
        Command("NAV [N]", 0, 90, () => hud.ToggleMap());
        Command("INVENTORY [I]", 96, 126, () => hud.ToggleInventory());
        Command("LOCK [Q]", 228, 95, () => hud.CockpitLock(), true);
        Command("DOCK [E]", 329, 92, () => { if (hud.ship.docked) hud.ship.DepositAll(); else hud.ship.StartApproach(); }, true);
        chargeLabel = Command("OVERCHARGE [G]", 427, 153, () => hud.ship.ToggleOvercharge(), true);
        var plate = Ui.Rect("Nameplate", root, Ui.BC, Ui.MID, Vector2.zero, new Vector2(105, 27));
        Text(plate, "BELT RUNNER", 11, 0, 0, 105, 15, White, "display", TextAnchor.UpperCenter);
        Text(plate, "MINING OPERATIONS", 7, 0, 16, 105, 10, Dim, "mono", TextAnchor.UpperCenter);
        plates.Add(plate);
        var balance = Ui.Rect("Balance plate", root, Ui.BC, Ui.MID, Vector2.zero, new Vector2(95, 28));
        credits = Text(balance, "", 10, 0, 0, 95, 15, Amber, "mono", TextAnchor.UpperCenter);
        Text(balance, "C  FLIGHT CONTROLS", 7, 0, 17, 95, 10, Dim, "mono", TextAnchor.UpperCenter);
        plates.Add(balance);
    }

    readonly List<RectTransform> plates = new List<RectTransform>();
    RectTransform Panel(string name, float w) { return Ui.Rect(name, root, Ui.TL, Ui.TC, Vector2.zero, new Vector2(w, 196)); }
    static Text Text(RectTransform p, string text, int size, float x, float y, float w, float h, Color c, string font = "mono", TextAnchor align = TextAnchor.UpperLeft)
    {
        var t = Ui.Label(p, text, font, size, c, align);
        Ui.At(t.rectTransform, Ui.TL, Ui.TL, new Vector2(x, -y), new Vector2(w, h));
        return t;
    }
    static void Title(RectTransform p, string name, float w) { Text(p, name, 11, 0, 0, w, 16, Dim, "display"); }
    static void Rule(RectTransform p, float y, float w) { Ui.Fill(Ui.Rect("Rule", p, Ui.TL, Ui.TL, new Vector2(0, -y), new Vector2(w, 1)), Ui.A(Cyan, .22f)); }
    static T Graphic<T>(RectTransform p, string name, float x, float y, float w, float h) where T : MaskableGraphic
    {
        var g = Ui.Rect(name, p, Ui.TL, Ui.TL, new Vector2(x, -y), new Vector2(w, h)).gameObject.AddComponent<T>();
        g.raycastTarget = false; return g;
    }
    static Ui.Gauge Gauge(RectTransform p, string name, float y, float w, Color c, float x = 0)
    {
        var g = Ui.Gauge.Make(p, name, c, x, -y, w, false, 7);
        g.name.color = Dim; g.name.fontSize = 10; g.value.fontSize = 11;
        return g;
    }
    Text Command(string name, float x, float w, Action action, bool flightOnly = false)
    {
        var rt = Ui.Rect(name, commands, Ui.TL, Ui.TL, new Vector2(x, 0), new Vector2(w, 34));
        var face = rt.gameObject.AddComponent<Ui.Pane>();
        face.top = Data.Hex("#252a2b"); face.bot = Ink; face.border = Data.Hex("#7c694b"); face.brackets = false; face.cut = 4;
        face.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = face;
        b.onClick.AddListener(() => { if (!hud.MenuVisible && !hud.ship.InCinematic) { Audio.Play("ui_click"); action(); } });
        var t = Text(rt, name, 11, 0, 0, w, 34, Amber, "display", TextAnchor.MiddleCenter);
        Ui.Fill(Ui.Rect("Backlight", rt, Ui.BL, Ui.BL, new Vector2(7, 2), new Vector2(w - 14, 1)), Ui.A(Amber, .75f));
        if (flightOnly) flightButtons.Add(b);
        return t;
    }

    public bool Contains(Vector2 pointer) { return Visible && RectTransformUtility.RectangleContainsScreenPoint(root, pointer, null); }
    public void Layout(Vector2 canvas, bool visible)
    {
        root.gameObject.SetActive(visible);
        Height = visible ? Mathf.Min(330f, canvas.y * .43f) : 0f;
        root.sizeDelta = new Vector2(0, Height);
        if (!visible) return;
        float scale = Mathf.Min(canvas.x / 1280f, Height / 350f);
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].anchoredPosition = new Vector2(canvas.x * centers[i], -Height * .117f);
            panels[i].localScale = Vector3.one * scale;
        }
        commands.anchoredPosition = new Vector2(0, Height * .113f);
        commands.localScale = Vector3.one * scale;
        plates[0].anchoredPosition = new Vector2(-canvas.x * .299f, Height * .151f);
        plates[1].anchoredPosition = new Vector2(canvas.x * .295f, Height * .151f);
        foreach (var p in plates) p.localScale = Vector3.one * scale;
    }

    public void Tick(float dt, Ship s, Belt belt, CargoShip carrier, Data.Zone z, Game game)
    {
        if (!Visible) return;
        refresh -= dt;
        if (refresh > 0f) return; // Instruments sample at 10 Hz; the world already owns all simulation state.
        refresh = .1f;
        float hf = State.hull / State.Stat("hull").hp, sf = State.shield / Data.SHIELD_MAX, ff = State.fuel / State.Stat("tank").cap;
        hull.Show(hf, Mathf.CeilToInt(hf * 100) + "%", hf < .25f ? Ui.RED : Cyan, hf < .25f);
        shield.Show(sf, Mathf.CeilToInt(sf * 100) + "%" + (sf < 1 && State.sinceHit >= Data.SHIELD_WAIT ? " +" : ""), sf < .25f ? Amber : Cyan);
        fuel.Show(ff, Mathf.FloorToInt(ff * 100) + "%", ff < .2f ? Ui.RED : Amber);
        systems.text = "ENGINE    " + (s.disabled ? "OFF" : "OK") + "\nTHRUST    " + (s.drifting ? "CUT" : s.thrusting ? "ON" : "IDLE") + "\nSHIELD    " + (sf >= 1 ? "FULL" : State.sinceHit >= Data.SHIELD_WAIT ? "REGEN" : "WAIT") + "\nLIGHT     " + (s.torchOn ? "ON" : "OFF");
        shipDiagram.tint = hf < .25f ? Ui.RED : Cyan; shipDiagram.SetVerticesDirty();
        speed.text = Mathf.RoundToInt(s.Speed * Data.METRE).ToString("000");
        thrust.Show(s.throttle, Mathf.RoundToInt(s.throttle * 100) + "%", s.braking ? Cyan : Amber);
        zone.text = z.name.ToUpperInvariant();
        mode.text = s.disabled ? "SYSTEMS OFFLINE" : s.docked ? "DOCKED / CARGO OPERATIONS" : s.drifting ? "DRIFT / ENGINE CUT" : s.braking ? "RETRO THRUST / DECELERATING" : s.afterburning ? "AFTERBURNER ACTIVE" : s.lockKind != "" ? "TARGET LOCK / AUTO STEER" : "LOCAL SPACE / MANUAL FLIGHT";
        mode.color = s.disabled ? Ui.RED : s.afterburning ? Amber : Dim;
        float hdg = Mathf.Repeat(Mathf.Atan2(s.Forward.x, s.Forward.z) * Mathf.Rad2Deg, 360);
        heading.text = "HEADING " + Mathf.RoundToInt(hdg).ToString("000") + "°     " + (s.lockKind != "" ? "LOCK ASSIST" : "MANUAL");
        int threats = game.raiders != null ? game.raiders.threat : 0;
        carrierRange.text = "CARGO SHIP " + (carrier != null ? Data.Fm((s.TruePos - carrier.truePos).magnitude) + " m" : "—") + (threats > 0 ? "  /  " + threats + " HOSTILE" : "");
        carrierRange.color = threats > 0 ? Ui.RED : Dim;
        attitude.forward = s.Forward; attitude.up = s.transform.up; attitude.right = s.transform.right; attitude.velocity = s.vel; attitude.SetVerticesDirty();
        float range = Mathf.Min(12000, State.Stat("scanner").range);
        scope.contacts.Clear();
        // Reuse the simulation's nearby list. Unknown ores stay neutral until a radar pulse reveals them.
        int sampled = 0;
        foreach (int i in s.nearRocks)
        {
            if (i < 0 || i >= belt.count || !belt.alive[i]) continue;
            var d = belt.RockPos(i) - s.TruePos;
            if (d.sqrMagnitude > range * range) continue;
            bool known = belt.markFrom[i] <= State.time && belt.markUntil[i] > State.time;
            scope.Add(d, s, range, known && belt.ore[i] >= 0 ? Data.ORES[belt.ore[i]].color : Dim, false);
            if (++sampled >= 96) break;
        }
        if (carrier != null) scope.Add(carrier.truePos - s.TruePos, s, range, Amber, true);
        if (game.raiders != null) foreach (var r in game.raiders.raiders) if (!r.dead) scope.Add(r.pos - s.TruePos, s, range, Ui.RED, true);
        if (game.drones != null) foreach (var d in game.drones.drones) scope.Add(d.pos - s.TruePos, s, range, Ui.GREEN, false);
        scope.sweep = Time.time * .7f; scope.SetVerticesDirty();
        radarRange.text = "RANGE " + Data.Fm(range) + " m";
        radarState.text = s.radarCd > 0 ? "PULSE " + s.radarCd.ToString("0.0") + "s  [R]" : "PULSE READY  [R]";
        UpdateTarget(s, belt);
        float cap = State.CargoCapacity(), total = State.CargoTotal();
        cargoValue.text = Mathf.RoundToInt(total) + " / " + Mathf.RoundToInt(cap);
        load.Show(cap > 0 ? total / cap : 0, State.UsedSlots() + "/" + State.CargoSlots() + " slots", total >= cap ? Ui.RED : Amber);
        string rows = ""; int n = 0;
        foreach (var ore in Data.ORES) { float units; if (!State.cargo.TryGetValue(ore.key, out units) || units <= 0) continue; if (n++ < 2) rows += (rows == "" ? "" : "\n") + ore.name.ToUpperInvariant() + " " + Mathf.FloorToInt(units); }
        cargoRows.text = rows == "" ? "HOLD EMPTY\nREADY TO EXTRACT" : rows + (n > 2 ? "  +" + (n - 2) : "");
        int count = game.drones != null ? game.drones.drones.Count : 0, working = 0;
        if (game.drones != null) foreach (var d in game.drones.drones) if (d.phase != "idle") working++;
        droneValue.text = count.ToString("00");
        droneState.text = count == 0 ? "NOT FITTED" : working > 0 ? working + " WORKING\n" + (count - working) + " STANDBY" : "ONLINE\nSTANDBY";
        droneState.color = count == 0 ? Dim : Ui.GREEN;
        credits.text = Data.Fmt(State.credits) + " cr";
        chargeLabel.color = s.overcharge ? Ui.GREEN : Amber;
        foreach (var b in flightButtons) b.interactable = !s.docked && s.CanFly && !hud.MenuVisible;
    }

    void UpdateTarget(Ship s, Belt belt)
    {
        bool locked = s.lockKind != "";
        int rock = locked && s.lockKind == "rock" ? s.lockRock : s.target;
        var raider = locked && s.lockKind == "raider" ? s.lockRaider : s.raiderTarget;
        targetName.text = "NO TARGET"; targetInfo.text = "AIM AT\nA CONTACT"; targetState.text = "Q / MMB TO LOCK";
        targetDiagram.kind = -1; targetDiagram.tint = Dim;
        integrity.Show(0, "—", Dim);
        targetState.color = Dim;
        if (raider != null && !raider.dead)
        {
            targetName.text = "PIRATE RAIDER";
            targetInfo.text = Data.Fm(Mathf.Max(0, (raider.pos - s.LaserOrigin()).magnitude - Raiders.RADIUS)) + " m\nSH " + Mathf.CeilToInt(Mathf.Max(0, raider.shield));
            targetState.text = "HOSTILE / " + (locked ? "LOCKED" : "TRACKING"); targetState.color = Ui.RED;
            targetDiagram.kind = 1; targetDiagram.tint = Ui.RED;
            integrity.Show(raider.hp / Mathf.Max(1, raider.maxHp), Mathf.CeilToInt(raider.hp) + " HP", Ui.RED);
        }
        else if (locked && s.lockKind == "station")
        {
            targetName.text = "CARGO SHIP"; targetInfo.text = Data.Fm(s.lockDist) + " m\nCARRIER";
            targetState.text = "FRIENDLY / LOCKED"; targetDiagram.kind = 1; targetDiagram.tint = Amber;
        }
        else if (rock >= 0 && rock < belt.count && belt.alive[rock])
        {
            int ore = belt.ore[rock];
            float d = Mathf.Max(0, (belt.RockPos(rock) - s.LaserOrigin()).magnitude - belt.radius[rock]);
            targetName.text = ore >= 0 ? Data.ORES[ore].name.ToUpperInvariant() : "BARREN";
            targetInfo.text = Data.Fm(d) + " m\n" + Belt.CLS_NAME[belt.cls[rock]].ToUpperInvariant();
            targetState.text = ore >= 0 && Data.ORES[ore].unlock > State.up["laser"] + 1 ? "NEEDS LASER LV " + Data.ORES[ore].unlock : d > State.Stat("range").reach ? "BEYOND LASER REACH" : locked ? "LOCKED / IN RANGE" : "IN RANGE / Q TO LOCK";
            targetDiagram.kind = 0; targetDiagram.tint = ore >= 0 ? Data.ORES[ore].color : Dim;
            integrity.Show(belt.hp[rock] / Mathf.Max(1, belt.hpMax[rock]), Mathf.CeilToInt(belt.hp[rock]) + " HP", Amber);
        }
        targetName.color = targetDiagram.tint;
        targetDiagram.SetVerticesDirty();
        weapon.text = s.weapon == "gun" ? "AUTOCANNON" : "MINING LASER";
        weaponState.text = s.weapon == "gun" && State.Stat("gun").reach <= 0 ? "NOT FITTED" : s.gunFiring ? "FIRING" : s.laserOn ? "CUTTING" : s.overcharge ? "OVERCHARGE ARMED" : "READY";
        weaponState.color = s.laserOn || s.gunFiring ? Amber : Ui.GREEN;
    }

    public class Scope : MaskableGraphic
    {
        public struct Contact { public Vector2 p; public Color c; public bool diamond; }
        public readonly List<Contact> contacts = new List<Contact>(112);
        public float sweep;
        public void Add(Vector3 delta, Ship s, float range, Color c, bool diamond)
        {
            if (delta.sqrMagnitude > range * range || range <= 0) return;
            contacts.Add(new Contact { p = new Vector2(Vector3.Dot(delta, s.transform.right), Vector3.Dot(delta, s.Forward)) / range, c = c, diamond = diamond });
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect(); var c = r.center; float radius = Mathf.Min(r.width, r.height) * .45f;
            for (int ring = 1; ring <= 4; ring++) Circle(vh, c, radius * ring / 4, ring == 4 ? Ui.A(Cyan, .8f) : Ui.A(Cyan, .18f));
            for (int i = 0; i < 12; i++) { var d = new Vector2(Mathf.Sin(i * Mathf.PI / 6), Mathf.Cos(i * Mathf.PI / 6)); Ui.Line(vh, c + d * radius * .92f, c + d * radius * 1.03f, 1, Ui.A(Cyan, .6f)); }
            Ui.Line(vh, c + new Vector2(-1, 0) * radius, c + new Vector2(1, 0) * radius, 1, Ui.A(Cyan, .12f));
            Ui.Line(vh, c + Vector2.up * radius, c + new Vector2(0, -1) * radius, 1, Ui.A(Cyan, .12f));
            for (int i = 0; i < 12; i++) { float a = sweep - i * .018f; Ui.Line(vh, c, c + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius, 1, Ui.A(Cyan, .16f * (1 - i / 12f))); }
            foreach (var p in contacts) { var q = c + p.p * radius; Ui.Diamond(vh, q, p.diamond ? 3 : 1.6f, 1.5f, p.c); }
            Ui.Outline(vh, new[] { c + new Vector2(0, 7), c + new Vector2(-5, -5), c + new Vector2(0, -2), c + new Vector2(5, -5) }, 1, Cyan);
        }
    }
    public class Attitude : MaskableGraphic
    {
        public Vector3 forward, up, right, velocity;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect(); var c = r.center;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1, 1)) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(right.y, up.y);
            for (int i = -2; i <= 2; i++)
            {
                float y = Mathf.Repeat(i * 18 + pitch * 1.2f + 45, 90) - 45;
                float w = i == 0 ? 66 : 35;
                var a = c + Ui.Rot(new Vector2(-w, y), roll); var b = c + Ui.Rot(new Vector2(w, y), roll);
                if (a.y < r.yMin + 3 || a.y > r.yMax - 3 || b.y < r.yMin + 3 || b.y > r.yMax - 3) continue;
                Ui.Line(vh, a, b, 1, Ui.A(Cyan, i == 0 ? .7f : .22f));
            }
            Ui.Line(vh, c + new Vector2(-39, 0), c + new Vector2(-13, 0), 1.5f, Cyan);
            Ui.Line(vh, c + new Vector2(13, 0), c + new Vector2(39, 0), 1.5f, Cyan);
            Ui.Diamond(vh, c, 3, 1, Amber);
            if (velocity.sqrMagnitude > 1) { var v = velocity.normalized; var p = c + new Vector2(Vector3.Dot(v, right), Vector3.Dot(v, up)) * 33; Circle(vh, p, 4, Cyan); }
            for (int i = 0; i < 9; i++)
            {
                float y = r.yMin + i * r.height / 8;
                Ui.Line(vh, new Vector2(r.xMin, y), new Vector2(r.xMin + (i % 2 == 0 ? 12 : 5), y), 1, Ui.A(Cyan, .4f));
                Ui.Line(vh, new Vector2(r.xMax, y), new Vector2(r.xMax - (i % 2 == 0 ? 12 : 5), y), 1, Ui.A(Cyan, .4f));
            }
        }
    }
    public class Diagram : MaskableGraphic
    {
        public int kind; public Color tint = Cyan;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect(); var c = r.center; float k = Mathf.Min(r.width, r.height) * .44f;
            if (kind < 0) { Circle(vh, c, k * .7f, Ui.A(tint, .35f)); Ui.Line(vh, c + new Vector2(-1, 0) * k, c + new Vector2(1, 0) * k, 1, Ui.A(tint, .3f)); return; }
            Vector2[] p;
            if (kind == 1) p = new[] { new Vector2(0, 1), new Vector2(.22f, .5f), new Vector2(.22f, .05f), new Vector2(.88f, -.6f), new Vector2(.3f, -.4f), new Vector2(.27f, -.9f), new Vector2(-.27f, -.9f), new Vector2(-.3f, -.4f), new Vector2(-.88f, -.6f), new Vector2(-.22f, .05f), new Vector2(-.22f, .5f) };
            else p = new[] { new Vector2(-.25f, 1), new Vector2(.55f, .7f), new Vector2(.95f, .12f), new Vector2(.5f, -.8f), new Vector2(-.5f, -.9f), new Vector2(-.98f, -.2f), new Vector2(-.72f, .56f) };
            for (int i = 0; i < p.Length; i++) p[i] = c + p[i] * k;
            Ui.Outline(vh, p, 1.2f, tint);
            for (int i = 0; i < p.Length; i++) Ui.Line(vh, p[i], kind == 1 ? c : c + new Vector2(k * .17f, k * .2f), 1, Ui.A(tint, .32f));
            if (kind == 0) { for (int i = 0; i < p.Length - 1; i++) Ui.Poly(vh, new[] { p[i], p[i + 1], c }, Ui.A(tint, .08f + i % 3 * .07f)); }
        }
    }
    static void Circle(VertexHelper vh, Vector2 center, float radius, Color c)
    {
        for (int i = 0; i < 64; i++) { float a = i * Mathf.PI / 32, b = (i + 1) * Mathf.PI / 32; Ui.Line(vh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, 1, c); }
    }
}
