using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// The flight HUD and the start/pause menu, built from UGUI in code: the ship readouts along the bottom, the world
/// readouts top right, the target top centre, the toasts, the crosshair, the controls list, and a menu card.
public class Hud : MonoBehaviour
{
    public static readonly Color AMBER = Data.Hex("#F2A33A");
    public static readonly Color CYAN = Data.Hex("#5ED3F0");
    public static readonly Color INK = new Color(0.04f, 0.05f, 0.09f, 0.78f);
    public static readonly Color TEXT = new Color(0.9f, 0.92f, 0.96f);
    public static readonly Color MUTED = new Color(0.62f, 0.66f, 0.74f);
    public static readonly Color BAD = Data.Hex("#FF6B5B");

    public Action onStart, onNewGame, onQuit;
    public Ship ship;
    public Tutorial tutorial;

    // ---- the Flight Ops card (top left) and the inventory (Tab)
    GameObject _tutBox, _inv;
    Text _tutStep, _tutTitle, _tutText, _tutWait, _invText;
    Button _tutNext;
    bool _tutHidden;
    public bool InvOpen { get { return _inv != null && _inv.activeSelf; } }

    void BuildTutorial()
    {
        var card = Panel("Tutorial", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(430f, 176f));
        card.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.11f, 0.94f);
        _tutBox = card.gameObject;
        _tutStep = Label(card, "Step", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(260f, 16f), 11, TextAnchor.MiddleLeft, AMBER);
        var replay = SmallButton(card, "REPLAY", new Vector2(270f, -8f), 70f, () => { if (tutorial != null) tutorial.Speak(); });
        replay.GetComponent<RectTransform>().sizeDelta = new Vector2(70f, 22f);
        var skip = SmallButton(card, "SKIP", new Vector2(346f, -8f), 68f, () => { if (tutorial != null) tutorial.Skip(); });
        skip.GetComponent<RectTransform>().sizeDelta = new Vector2(68f, 22f);
        _tutTitle = Label(card, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -32f), new Vector2(400f, 24f), 17, TextAnchor.MiddleLeft, TEXT);
        _tutText = Label(card, "Text", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -58f), new Vector2(400f, 76f), 12, TextAnchor.UpperLeft, TEXT);
        _tutWait = Label(card, "Wait", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 10f), new Vector2(260f, 18f), 11, TextAnchor.MiddleLeft, AMBER);
        _tutNext = SmallButton(card, "NEXT  (Enter)", new Vector2(16f, 8f), 130f, () => { if (tutorial != null) tutorial.Advance(); }, true);
        _tutBox.SetActive(false);
        // the inventory: the hold's stacks, and the storage while docked
        var inv = Panel("Inventory", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(300f, 220f));
        inv.pivot = new Vector2(0f, 0.5f);
        inv.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.11f, 0.94f);
        _inv = inv.gameObject;
        Label(inv, "Cap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(270f, 16f), 11, TextAnchor.MiddleLeft, MUTED).text = "INVENTORY · Tab closes";
        _invText = Label(inv, "Text", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -32f), new Vector2(270f, 180f), 12, TextAnchor.UpperLeft, TEXT);
        _inv.SetActive(false);
    }

    /// Show a step (null hides the card).
    public void ShowTutorial(Tutorial.Step s, int number)
    {
        if (s == null)
        {
            _tutBox.SetActive(false);
            return;
        }
        _tutBox.SetActive(!_tutHidden);
        _tutStep.text = "FLIGHT OPS · " + number + " / " + Tutorial.STEPS.Length;
        _tutTitle.text = s.title;
        _tutText.text = s.text;
        _tutNext.gameObject.SetActive(!s.Auto);
        _tutNext.GetComponentInChildren<Text>().text = s.final ? "FINISH  (Enter)" : "NEXT  (Enter)";
        _tutWait.text = s.Auto ? "waiting · " + s.wait : "";
    }

    public void TutorialHidden(bool h)
    {
        _tutHidden = h;
        if (tutorial != null && tutorial.Active) _tutBox.SetActive(!h);
    }

    public void ToggleInventory()
    {
        _inv.SetActive(!_inv.activeSelf);
        if (_inv.activeSelf) RefreshInventory();
    }

    void RefreshInventory()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("HOLD  " + State.UsedSlots() + " / " + State.CargoSlots() + " slots\n");
        bool any = false;
        foreach (var k in Data.ORE_KEYS)
        {
            if (State.cargo[k] < 0.5f) continue;
            any = true;
            sb.Append("  " + Data.ORES[Data.OreIndex(k)].name.PadRight(12) + Mathf.RoundToInt(State.cargo[k]).ToString().PadLeft(4) + " u\n");
        }
        if (!any) sb.Append("  empty\n");
        sb.Append("\nCARGO SHIP STORAGE  " + State.StoreUsed() + " / " + Data.STORE_SLOTS + " slots" + (ship != null && ship.docked ? "" : " · dock to transfer") + "\n");
        any = false;
        foreach (var k in Data.ORE_KEYS)
        {
            if (State.store[k] < 0.5f) continue;
            any = true;
            sb.Append("  " + Data.ORES[Data.OreIndex(k)].name.PadRight(12) + Mathf.RoundToInt(State.store[k]).ToString().PadLeft(4) + " u\n");
        }
        if (!any) sb.Append("  empty\n");
        _invText.text = sb.ToString();
    }

    Font _font;
    Canvas _canvas;
    RectTransform _root;
    Text _hull, _fuel, _speed, _thrust, _cargo, _world, _target, _targetSub, _hint, _controls, _version;
    Text _menuTitle, _menuTag, _menuNote;
    GameObject _menu, _controlsPanel;
    Button _btnContinue, _btnNew, _btnQuit;
    RectTransform _toastBox;
    readonly List<KeyValuePair<Text, float>> _toasts = new List<KeyValuePair<Text, float>>();
    bool _controlsShown = true;
    int _newClicks;

    public void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _root = GetComponent<RectTransform>();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // ship readouts, bottom centre
        var shipPanel = Panel("ShipPanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(720f, 62f));
        _hull = Readout(shipPanel, "HULL", -300f);
        _fuel = Readout(shipPanel, "FUEL", -150f);
        _speed = Readout(shipPanel, "SPEED", 0f);
        _thrust = Readout(shipPanel, "THRUST", 150f);
        _cargo = Readout(shipPanel, "CARGO", 300f);
        // the world, top right
        var worldPanel = Panel("WorldPanel", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(420f, 56f));
        _world = Label(worldPanel, "World", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 50f), 13, TextAnchor.MiddleLeft, TEXT);
        // the target, top centre
        var targetPanel = Panel("TargetPanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(300f, 56f));
        _target = Label(targetPanel, "Target", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(290f, 24f), 16, TextAnchor.MiddleCenter, AMBER);
        _targetSub = Label(targetPanel, "TargetSub", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(290f, 20f), 12, TextAnchor.MiddleCenter, MUTED);
        // crosshair and the hint under it
        Label(_root, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f), 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f)).text = "+";
        _hint = Label(_root, "Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(700f, 22f), 13, TextAnchor.MiddleCenter, MUTED);
        // toasts, stacked under the target
        var tb = new GameObject("Toasts", typeof(RectTransform));
        _toastBox = tb.GetComponent<RectTransform>();
        _toastBox.SetParent(_root, false);
        _toastBox.anchorMin = _toastBox.anchorMax = new Vector2(0.5f, 1f);
        _toastBox.pivot = new Vector2(0.5f, 1f);
        _toastBox.anchoredPosition = new Vector2(0f, -78f);
        _toastBox.sizeDelta = new Vector2(700f, 200f);
        // controls, bottom left
        _controlsPanel = Panel("Controls", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 34f), new Vector2(330f, 300f)).gameObject;
        _controls = Label(_controlsPanel.GetComponent<RectTransform>(), "ControlsText", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -8f), new Vector2(310f, 284f), 11, TextAnchor.UpperLeft, MUTED);
        _controls.text = "FLIGHT CONTROLS\n" +
            "Mouse   Yaw · pitch\n" +
            "W / S   Throttle up · down\n" +
            "X       Cut throttle · S at zero fires retros\n" +
            "A / D   Roll left · right\n" +
            "Shift   Afterburner while throttled up (needs the refit)\n" +
            "G       Laser overcharge on · off (needs the refit)\n" +
            "Up/Down Pitch\n" +
            "LMB     Hold to fire the mining laser (Space or L too)\n" +
            "R       Radar pulse\n" +
            "F5      Quick-save\n" +
            "C       Hide · show this list\n" +
            "Esc     Pause · the menu";
        _version = Label(_root, "Version", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(200f, 18f), 11, TextAnchor.MiddleLeft, new Color(0.5f, 0.53f, 0.6f));
        _version.text = "v" + Data.VERSION;
        BuildServices();
        BuildMap();
        BuildTutorial();
        // the fade for a jump, over everything but the menu
        var fg = new GameObject("Fade", typeof(RectTransform));
        var fr = fg.GetComponent<RectTransform>();
        fr.SetParent(_root, false);
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        _fade = fg.AddComponent<Image>();
        _fade.color = new Color(0.008f, 0.012f, 0.039f, 0f);
        _fade.raycastTarget = false;
        BuildMenu();
    }

    Image _fade;

    // ---- the cargo ship services panel (#station): a side panel over the live view of the ship on the pad
    GameObject _services;
    Text _svcEyebrow, _svcCredits, _svcGauges, _svcHold, _svcRefitCap;
    RectTransform _svcRefits;
    Button _depositBtn, _departBtn, _warpBtn;
    bool _servicesVisible;

    void BuildServices()
    {
        var panel = Panel("Services", new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(400f, 0f));
        panel.pivot = new Vector2(1f, 0f);
        panel.offsetMin = new Vector2(-400f, 0f);
        panel.offsetMax = new Vector2(0f, 0f);
        panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.09f, 0.93f);
        _services = panel.gameObject;
        _svcEyebrow = Label(panel, "Eyebrow", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(360f, 16f), 11, TextAnchor.MiddleLeft, MUTED);
        Label(panel, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -32f), new Vector2(240f, 30f), 22, TextAnchor.MiddleLeft, TEXT).text = "CARGO SHIP";
        _svcCredits = Label(panel, "Credits", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(200f, -32f), new Vector2(180f, 30f), 20, TextAnchor.MiddleRight, AMBER);
        _svcGauges = Label(panel, "Gauges", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -72f), new Vector2(360f, 60f), 12, TextAnchor.UpperLeft, TEXT);
        Label(panel, "HoldCap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -136f), new Vector2(360f, 16f), 11, TextAnchor.MiddleLeft, MUTED).text = "YOUR HOLD";
        _svcHold = Label(panel, "Hold", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -152f), new Vector2(360f, 36f), 12, TextAnchor.UpperLeft, TEXT);
        _depositBtn = SmallButton(panel, "DEPOSIT ALL  (E)", new Vector2(20f, -192f), 170f, () => { if (ship != null) ship.DepositAll(); });
        SmallButton(panel, "TAKE ALL", new Vector2(200f, -192f), 120f, () => { if (ship != null) ship.TakeAll(); });
        _svcRefitCap = Label(panel, "RefitCap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -236f), new Vector2(360f, 16f), 11, TextAnchor.MiddleLeft, MUTED);
        _svcRefitCap.text = "PERSONAL SHIP · REFITS";
        var rg = new GameObject("Refits", typeof(RectTransform));
        _svcRefits = rg.GetComponent<RectTransform>();
        _svcRefits.SetParent(panel, false);
        _svcRefits.anchorMin = _svcRefits.anchorMax = new Vector2(0f, 1f);
        _svcRefits.pivot = new Vector2(0f, 1f);
        _svcRefits.anchoredPosition = new Vector2(20f, -254f);
        _svcRefits.sizeDelta = new Vector2(360f, 380f);
        _departBtn = SmallButton(panel, "DEPART  (W)", new Vector2(20f, 14f), 140f, () => { if (ship != null) ship.StartDeparture(); }, true);
        _warpBtn = SmallButton(panel, "WARP TO THE HUB", new Vector2(170f, 14f), 130f, () => { if (ship != null && ship.docked && !zone.hub) ship.StartWarp(Data.ZONE_HUB); }, true);
        SmallButton(panel, "HIDE  (F)", new Vector2(310f, 14f), 70f, () => ToggleServices(), true);
        _services.SetActive(false);
    }

    Button SmallButton(RectTransform parent, string label, Vector2 pos, float width, Action onClick, bool fromBottom = false)
    {
        var go = new GameObject("Btn " + label, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var anchor = fromBottom ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 30f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.15f, 0.24f, 1f);
        var b = go.AddComponent<Button>();
        var colors = b.colors;
        colors.highlightedColor = new Color(0.9f, 0.7f, 0.4f, 1f);
        colors.pressedColor = AMBER;
        b.colors = colors;
        b.onClick.AddListener(() => onClick());
        var t = Label(rt, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 12, TextAnchor.MiddleCenter, TEXT);
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.text = label;
        return b;
    }

    public bool ServicesVisible { get { return _services.activeSelf; } }

    public void ToggleServices()
    {
        _servicesVisible = !_servicesVisible;
        _services.SetActive(_servicesVisible && ship != null && ship.docked);
        if (_services.activeSelf) RefreshServices();
    }

    public void OnDocked(bool isDocked)
    {
        if (isDocked && !_servicesVisible) _servicesVisible = true;
        if (!isDocked) _servicesVisible = false;
        _services.SetActive(isDocked && _servicesVisible);
        if (isDocked) RefreshServices();
    }

    public void RefreshServices()
    {
        if (ship == null) return;
        bool atHub = ship.hold;
        _svcEyebrow.text = atHub ? "HOLDING STATION · OFF MERIDIAN COLONY" : "DOCKED · " + CargoShip.BayName(ship.dockSide).ToUpperInvariant();
        _departBtn.GetComponentInChildren<Text>().text = atHub ? "NAV MAP  (N)" : "DEPART  (W)";
        _warpBtn.gameObject.SetActive(!atHub);
        _svcRefitCap.text = atHub ? "CARGO SHIP · SUPPLIES AND MARKET" : "PERSONAL SHIP · REFITS";
        _svcCredits.text = Data.Fmt(State.credits) + " cr";
        int su = State.StoreUsed();
        _svcGauges.text = "Cargo ship storage   " + su + " / " + Data.STORE_SLOTS + " slots" + (su >= Data.STORE_SLOTS ? " · FULL" : "") + "\n" +
            "Fuel supply          " + Mathf.FloorToInt(State.shipFuel) + " / " + Mathf.RoundToInt(Data.CARGO_FUEL_CAP) + (State.shipFuel < Data.CARGO_FUEL_CAP * 0.2f ? " · LOW" : "") + "\n" +
            "Repair parts         " + Mathf.FloorToInt(State.parts) + " / " + Data.PARTS_CAP + (State.parts < Data.PARTS_CAP * 0.2f ? " · LOW" : "");
        var lines = new List<string>();
        foreach (var k in Data.ORE_KEYS) if (State.cargo[k] > 0.5f) lines.Add(Data.ORES[Data.OreIndex(k)].name + " " + Mathf.RoundToInt(State.cargo[k]) + " u");
        _svcHold.text = lines.Count > 0 ? string.Join(", ", lines.ToArray()) + "  ·  worth " + Data.Fmt(State.ValueOf(State.cargo)) + " cr at the Hub" : "The hold is empty.";
        var stored = new List<string>();
        foreach (var k in Data.ORE_KEYS) if (State.store[k] > 0.5f) stored.Add(Data.ORES[Data.OreIndex(k)].name + " " + Mathf.RoundToInt(State.store[k]));
        if (stored.Count > 0) _svcHold.text += "\nStored aboard: " + string.Join(", ", stored.ToArray());
        foreach (Transform c in _svcRefits) Destroy(c.gameObject);
        if (atHub)
        {
            RefreshMarket();
            return;
        }
        float y = 0f;
        foreach (var key in Data.UPGRADE_KEYS)
        {
            var u = Data.UPGRADES[key];
            int i = State.up[key];
            bool maxed = i >= u.costs.Length;
            var name = Label(_svcRefits, "Name", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(240f, 16f), 12, TextAnchor.MiddleLeft, TEXT);
            name.text = u.name + "  Lv" + (i + 1) + "/" + u.levels.Length;
            var desc = Label(_svcRefits, "Desc", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 16f), new Vector2(240f, 16f), 10, TextAnchor.MiddleLeft, MUTED);
            desc.text = maxed ? Data.Describe(key, i) + " · fully upgraded" : Data.Describe(key, i) + "  →  " + Data.Describe(key, i + 1);
            if (!maxed)
            {
                float cost = u.costs[i];
                var k2 = key;
                var b = SmallButton(_svcRefits, Data.Fmt(cost) + " cr", new Vector2(250f, y - 2f), 110f, () => Buy(k2));
                b.interactable = State.credits >= cost;
                var bt = b.GetComponentInChildren<Text>();
                bt.color = State.credits >= cost ? AMBER : MUTED;
            }
            y -= 40f;
        }
    }

    void Buy(string key)
    {
        string msg;
        bool ok = State.Buy(key, out msg);
        Toast(msg, !ok);
        RefreshServices();
    }

    /// The colony market (renderMarket): what is aboard and what it fetches, the sell buttons, today's prices; and
    /// the cargo ship's fuel and parts purchases. Shown in the refits' place while holding station at the Hub.
    void RefreshMarket()
    {
        float y = 0f;
        float fuelCost = Mathf.Ceil((Data.CARGO_FUEL_CAP - State.shipFuel) * Data.CARGO_FUEL_PRICE);
        float partsCost = Mathf.Ceil((Data.PARTS_CAP - State.parts) * Data.PARTS_PRICE);
        var fb = SmallButton(_svcRefits, fuelCost > 0f ? "REFUEL SUPPLY · " + Data.Fmt(fuelCost) + " cr" : "FUEL SUPPLY FULL", new Vector2(0f, y), 200f, () => { if (ship != null) ship.RefuelCargoShip(); });
        fb.interactable = fuelCost > 0f && State.credits >= 1f;
        var pb = SmallButton(_svcRefits, partsCost > 0f ? "RESTOCK PARTS · " + Data.Fmt(partsCost) + " cr" : "PARTS STORE FULL", new Vector2(0f, y - 34f), 200f, () => { if (ship != null) ship.BuyParts(); });
        pb.interactable = partsCost > 0f && State.credits >= Data.PARTS_PRICE;
        y -= 76f;
        var cap = Label(_svcRefits, "MarketCap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(360f, 16f), 11, TextAnchor.MiddleLeft, MUTED);
        cap.text = "MERIDIAN COLONY MARKET";
        y -= 18f;
        var lines = new List<string>();
        float total = 0f;
        foreach (var k in Data.ORE_KEYS)
        {
            float h = State.cargo[k], s = State.store[k];
            if (h + s < 0.5f) continue;
            float p = State.Price(k);
            float v = (h + s) * p;
            total += v;
            var o = Data.ORES[Data.OreIndex(k)];
            lines.Add(o.name.PadRight(11) + " hold " + Data.Fmt(h).PadLeft(4) + "  stored " + Data.Fmt(s).PadLeft(4) + "  @ " + p.ToString("0.0") + " = " + Data.Fmt(v) + " cr" + (o.zone != null ? "  (exotic +50%)" : ""));
        }
        var list = Label(_svcRefits, "Market", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(360f, 120f), 11, TextAnchor.UpperLeft, TEXT);
        list.text = lines.Count > 0 ? string.Join("\n", lines.ToArray()) + "\nEverything aboard  " + Data.Fmt(total) + " cr" : "Nothing aboard to sell. Cut ore in a belt zone and bring it back.";
        y -= 14f * Mathf.Max(2, lines.Count + 1) + 8f;
        if (lines.Count > 0)
        {
            var sb = SmallButton(_svcRefits, "SELL EVERYTHING", new Vector2(0f, y), 140f, () => { if (ship != null) ship.Sell(Data.ORE_KEYS, true, true); });
            SmallButton(_svcRefits, "SELL HOLD", new Vector2(150f, y), 100f, () => { if (ship != null) ship.Sell(Data.ORE_KEYS, true, false); });
            SmallButton(_svcRefits, "SELL STORAGE", new Vector2(260f, y), 100f, () => { if (ship != null) ship.Sell(Data.ORE_KEYS, false, true); });
            y -= 40f;
        }
        var pc = Label(_svcRefits, "PricesCap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(360f, 16f), 11, TextAnchor.MiddleLeft, MUTED);
        pc.text = "PRICES TODAY · cr each";
        y -= 18f;
        var plines = new List<string>();
        foreach (var k in Data.ORE_KEYS)
        {
            var o = Data.ORES[Data.OreIndex(k)];
            int d = Mathf.RoundToInt((State.market[k] - 1f) * 100f);
            string delta = d == 0 ? "" : (d > 0 ? "  ▲" + d + "%" : "  ▼" + (-d) + "%");
            string need = o.unlock > State.up["laser"] + 1 ? "  needs laser Lv" + o.unlock : "";
            plines.Add(o.name.PadRight(11) + State.Price(k).ToString("0.0").PadLeft(6) + delta + need + (o.zone != null ? "  exotic" : ""));
        }
        var pl = Label(_svcRefits, "Prices", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(360f, 110f), 11, TextAnchor.UpperLeft, MUTED);
        pl.text = string.Join("\n", plines.ToArray());
    }

    // ---- the nav map (N): the charted zones, the cargo ship's fuel supply, and the jump
    GameObject _map;
    Text _mapFuel, _mapInfo;
    Button _jumpBtn;
    Data.Zone _mapSel;
    public Data.Zone zone = Data.ZONE_KESSLER;
    public bool MapOpen { get { return _map != null && _map.activeSelf; } }

    void BuildMap()
    {
        _map = new GameObject("Map", typeof(RectTransform));
        var mr = _map.GetComponent<RectTransform>();
        mr.SetParent(_root, false);
        mr.anchorMin = Vector2.zero;
        mr.anchorMax = Vector2.one;
        mr.offsetMin = mr.offsetMax = Vector2.zero;
        _map.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        var card = Panel("MapCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 420f));
        card.SetParent(mr, false);
        card.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.11f, 0.97f);
        Label(card, "Eyebrow", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(400f, 16f), 11, TextAnchor.MiddleLeft, MUTED).text = "NAV COMPUTER";
        Label(card, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -36f), new Vector2(400f, 30f), 22, TextAnchor.MiddleLeft, TEXT).text = "CHARTED ZONES";
        Label(card, "FuelCap", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(300f, 16f), 11, TextAnchor.MiddleRight, MUTED).text = "CARGO SHIP FUEL SUPPLY";
        _mapFuel = Label(card, "Fuel", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -36f), new Vector2(300f, 30f), 20, TextAnchor.MiddleRight, AMBER);
        float y = -84f;
        foreach (var z in Data.ZONES)
        {
            var zz = z;
            SmallButton(card, z.name.ToUpperInvariant(), new Vector2(24f, y), 220f, () => { _mapSel = zz; RefreshMap(); });
            y -= 36f;
        }
        _mapInfo = Label(card, "Info", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -84f), new Vector2(466f, 260f), 12, TextAnchor.UpperLeft, TEXT);
        _jumpBtn = SmallButton(card, "JUMP", new Vector2(24f, 18f), 160f, () => { if (ship != null && _mapSel != null) { ship.StartWarp(_mapSel); CloseMap(); } }, true);
        SmallButton(card, "CLOSE  (N)", new Vector2(200f, 18f), 120f, () => CloseMap(), true);
        var foot = Label(card, "Foot", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(340f, 14f), new Vector2(400f, 40f), 10, TextAnchor.LowerLeft, MUTED);
        foot.text = "Nothing sells in the belt: haul it to the Hub, where exclusive ore fetches 50% more. Your cargo ship, storage and all, warps with you.";
        _map.SetActive(false);
    }

    public void ToggleMap()
    {
        if (MapOpen) CloseMap(); else OpenMap();
    }

    public void OpenMap()
    {
        if (ship != null && ship.warp != null) return;
        _map.SetActive(true);
        if (_mapSel == null || _mapSel.id == zone.id) _mapSel = null;
        RefreshMap();
    }

    public void CloseMap()
    {
        _map.SetActive(false);
    }

    void RefreshMap()
    {
        _mapFuel.text = Mathf.FloorToInt(State.shipFuel) + " / " + Mathf.RoundToInt(Data.CARGO_FUEL_CAP);
        var sel = _mapSel ?? zone;
        bool isCur = sel.id == zone.id;
        var sb = new System.Text.StringBuilder();
        sb.Append(isCur ? "CURRENT ZONE\n" : (sel.hub ? "COLONY ZONE\n" : "CHARTED BELT\n"));
        sb.Append(sel.name.ToUpperInvariant() + "\n\n");
        sb.Append(sel.tag + "\n\n");
        if (!isCur) sb.Append("Distance  " + Data.ZoneLy(zone, sel) + " ly\n");
        if (sel.hub)
        {
            sb.Append("Market: every ore; exclusive ores fetch +50%.\nFuel and repair parts for the cargo ship.\n");
        }
        else
        {
            var ex = new List<string>();
            foreach (var o in Data.ORES) if (o.zone == sel.id) ex.Add(o.name);
            sb.Append("Exclusive ores: " + string.Join(", ", ex.ToArray()) + "\nCommon ores in three belts and the ring belt.\n");
        }
        if (!isCur && ship != null && !ship.docked) sb.Append("\nDock with the cargo ship to jump: it makes the jump.");
        _mapInfo.text = sb.ToString();
        _jumpBtn.interactable = !isCur && ship != null && ship.docked && ship.warp == null;
        _jumpBtn.GetComponentInChildren<Text>().text = isCur ? "HERE" : "JUMP · " + sel.name.ToUpperInvariant();
    }

    void BuildMenu()
    {
        _menu = new GameObject("Menu", typeof(RectTransform));
        var mr = _menu.GetComponent<RectTransform>();
        mr.SetParent(_root, false);
        mr.anchorMin = Vector2.zero;
        mr.anchorMax = Vector2.one;
        mr.offsetMin = mr.offsetMax = Vector2.zero;
        var dim = _menu.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);
        var card = Panel("Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 400f));
        card.SetParent(mr, false);
        card.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.11f, 0.96f);
        _menuNote = Label(card, "Note", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(460f, 18f), 11, TextAnchor.MiddleLeft, MUTED);
        _menuTitle = Label(card, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -46f), new Vector2(460f, 44f), 34, TextAnchor.MiddleLeft, AMBER);
        _menuTitle.text = "BELT RUNNER";
        _menuTag = Label(card, "Tag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -96f), new Vector2(460f, 70f), 13, TextAnchor.UpperLeft, TEXT);
        _menuTag.text = "One ship, one laser, an empty hold. Cut ore from the rocks, stow it aboard your cargo ship, sell it at the Hub, and refit the ship until it can afford a jump drive out.";
        _btnContinue = MenuButton(card, "Continue", -180f, () => { if (onStart != null) onStart(); });
        _btnNew = MenuButton(card, "New game", -232f, () =>
        {
            _newClicks++;
            if (State.hasSave && _newClicks < 2)
            {
                Toast("Click New game again to wipe the saved pilot", true);
                return;
            }
            _newClicks = 0;
            if (onNewGame != null) onNewGame();
        });
        _btnQuit = MenuButton(card, "Quit", -284f, () => { if (onQuit != null) onQuit(); });
        Label(card, "Foot", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 14f), new Vector2(460f, 18f), 11, TextAnchor.MiddleLeft, MUTED).text = "Belt Runner 3D · Unity port · v" + Data.VERSION;
        _menu.SetActive(false);
    }

    RectTransform Panel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_root, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = INK;
        return rt;
    }

    Text Label(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    Text Readout(RectTransform panel, string title, float x)
    {
        var cap = Label(panel, title + "Cap", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -6f), new Vector2(140f, 16f), 10, TextAnchor.MiddleCenter, MUTED);
        cap.text = title;
        var v = Label(panel, title, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 6f), new Vector2(140f, 30f), 18, TextAnchor.MiddleCenter, TEXT);
        v.text = "-";
        return v;
    }

    Button MenuButton(RectTransform card, string label, float y, Action onClick)
    {
        var go = new GameObject("Btn " + label, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(card, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(30f, y);
        rt.sizeDelta = new Vector2(460f, 42f);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.15f, 0.24f, 1f);
        var b = go.AddComponent<Button>();
        var colors = b.colors;
        colors.highlightedColor = new Color(0.9f, 0.7f, 0.4f, 1f);
        colors.pressedColor = AMBER;
        b.colors = colors;
        b.onClick.AddListener(() => onClick());
        var t = Label(rt, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 15, TextAnchor.MiddleCenter, TEXT);
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.text = label.ToUpperInvariant();
        return b;
    }

    public void ShowMenu(bool visible, bool paused)
    {
        _menu.SetActive(visible);
        if (!visible) return;
        _newClicks = 0;
        _menuNote.text = paused ? "PAUSED · " + State.zoneId.ToUpperInvariant() : "KESSLER BELT · SECTOR 7";
        _btnContinue.gameObject.SetActive(paused || State.hasSave);
        _btnContinue.GetComponentInChildren<Text>().text = paused ? "RESUME" : "CONTINUE";
    }

    public bool MenuVisible { get { return _menu.activeSelf; } }

    public void ToggleControls()
    {
        _controlsShown = !_controlsShown;
        _controlsPanel.SetActive(_controlsShown);
    }

    public void Toast(string msg, bool bad)
    {
        var t = Label(_toastBox, "Toast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(700f, 22f), 13, TextAnchor.MiddleCenter, bad ? BAD : TEXT);
        t.text = msg;
        _toasts.Add(new KeyValuePair<Text, float>(t, 4.5f));
        if (_toasts.Count > 5)
        {
            Destroy(_toasts[0].Key.gameObject);
            _toasts.RemoveAt(0);
        }
        LayoutToasts();
    }

    void LayoutToasts()
    {
        for (int i = 0; i < _toasts.Count; i++)
        {
            _toasts[i].Key.rectTransform.anchoredPosition = new Vector2(0f, -24f * (_toasts.Count - 1 - i));
        }
    }

    public void UpdateHud(float dt, Ship ship, Belt belt, Data.Zone zone)
    {
        // toasts age, then go
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            float left = _toasts[i].Value - dt;
            if (left <= 0f)
            {
                Destroy(_toasts[i].Key.gameObject);
                _toasts.RemoveAt(i);
                continue;
            }
            _toasts[i] = new KeyValuePair<Text, float>(_toasts[i].Key, left);
            var c = _toasts[i].Key.color;
            c.a = Mathf.Clamp01(left / 0.6f);
            _toasts[i].Key.color = c;
        }
        LayoutToasts();
        _world.transform.parent.gameObject.SetActive(!ServicesVisible);
        if (InvOpen && Time.frameCount % 15 == 0) RefreshInventory();
        var fc = _fade.color;
        fc.a = ship.WarpFade();
        _fade.color = fc;
        var hullMax = State.Stat("hull").hp;
        var fuelMax = State.Stat("tank").cap;
        _hull.text = Mathf.RoundToInt(State.hull) + " / " + Mathf.RoundToInt(hullMax);
        _hull.color = State.hull < hullMax * 0.3f ? BAD : TEXT;
        _fuel.text = Mathf.RoundToInt(State.fuel) + " / " + Mathf.RoundToInt(fuelMax);
        _fuel.color = State.fuel < fuelMax * 0.2f ? BAD : TEXT;
        _speed.text = Mathf.RoundToInt(ship.Speed * Data.METRE).ToString();
        _thrust.text = Mathf.RoundToInt(ship.throttle * 100f) + "%" + (ship.afterburning ? " AB" : "");
        _cargo.text = State.UsedSlots() + " / " + State.CargoSlots() + " slots";
        string laser = ship.laserOn ? "CUTTING" : (ship.firing ? "no target" : "ready");
        string radar = ship.radarCd > 0f ? ship.radarCd.ToString("0.0") + "s" : "READY";
        string carrierTxt = ship.carrier != null ? "    CARGO SHIP " + Data.Fm((ship.carrier.truePos - ship.TruePos).magnitude) + " m" : "";
        _world.text = "ZONE " + zone.name + "    LASER " + laser + carrierTxt + "\nRANGE " + Data.Fm(State.Stat("range").reach) + " m    RADAR " + radar + "    CR " + Data.Fmt(State.credits);
        if (ship.target >= 0 && ship.target < belt.count && belt.alive[ship.target])
        {
            int t = ship.target;
            _target.text = belt.RockName(t);
            float d = (belt.RockPos(t) - ship.TruePos).magnitude;
            string ore = belt.ore[t] >= 0 ? Data.ORES[belt.ore[t]].name : "";
            bool locked = belt.ore[t] >= 0 && Data.ORES[belt.ore[t]].unlock > State.up["laser"] + 1;
            _targetSub.text = "RANGE " + Data.Fm(d) + " m · " + Mathf.RoundToInt(Mathf.Max(0f, belt.hp[t])) + " / " + Mathf.RoundToInt(belt.hpMax[t]) + (locked ? " · needs laser Lv" + Data.ORES[belt.ore[t]].unlock : "");
            _targetSub.color = locked ? BAD : MUTED;
            _target.transform.parent.gameObject.SetActive(true);
        }
        else
        {
            _target.transform.parent.gameObject.SetActive(false);
        }
        if (ship.docked) _hint.text = "W  Depart   ·   E  Deposit the hold   ·   F  Services panel";
        else if (ship.InCinematic) _hint.text = "Approach control has the ship · Space skips";
        else if (ship.target >= 0) _hint.text = "LMB  Hold to mine";
        else if (ship.carrier != null && (ship.carrier.truePos - ship.TruePos).magnitude < Data.DOCK_RANGE) _hint.text = "E  Auto-dock with the cargo ship · or fly in through either hangar mouth";
        else _hint.text = ship.firing ? "Aim the nose at a rock" : "";
    }
}
