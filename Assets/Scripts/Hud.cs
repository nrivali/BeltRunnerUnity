using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// The flight HUD and the in-game panels, laid out and styled as belt-runner-3d.html's are (see Ui.cs for the
/// stylesheet): the ship status pane bottom-centre, the situation readouts top-right, the target pane top-centre, the
/// flight controls list bottom-left, the hint bar above the status pane, the boresight brackets on whatever the nose is
/// on, the cargo ship marker, radar blips, toasts, the cargo-full notice, the letterbox and caption for cutscenes, the
/// fade for a jump, the vignette and the damage flash, the version tag; the cargo ship services side panel (right), the
/// inventory side panel (left), the nav computer, the tutorial card top-left with its highlight rings, and the menu.
public class Hud : MonoBehaviour
{
    public Ship ship;
    public Tutorial tutorial;
    public Game game;
    public Menu menu;
    public Data.Zone zone = Data.ZONE_KESSLER;
    public bool started;

    Canvas _canvas;
    CanvasScaler _scaler;
    RectTransform _root;
    Vector2 _canvasSize = new Vector2(1280f, 720f);
    Camera _cam;

    // the flight HUD
    Ui.Vignette _vignette, _dmg, _shieldOut;
    Ui.SpeedStreaks _speed;
    float _speedK;
    float _shieldOutA;   // the shield-down warning: 0..1, eased
    Ui.Blips _blips;
    Ui.Reticle _reticle;
    RectTransform _reticleRt;
    Ui.Marker _marker, _fieldMarker;
    readonly List<Ui.Marker> _leadPips = new List<Ui.Marker>();   // one LEAD pip per raider within gun reach
    Ui.Crosshair _crosshair;
    RectTransform _crosshairRt;
    readonly List<Ui.Marker> _droneMarkers = new List<Ui.Marker>();
    readonly List<Ui.Marker> _raiderMarkers = new List<Ui.Marker>();
    RectTransform _status, _readouts, _target, _controls, _prompt, _notice, _toastBox, _version, _hoverLbl;
    Text _hoverTxt;
    Ui.Pane _statusPane;
    Ui.Gauge _gHull, _gShield, _gFuel, _gThr, _gCargo;
    Text _speedBig, _row1, _row2, _tEyebrow, _tName, _tRows, _tHpT, _tWarn, _promptText, _caption;
    Ui.SegBar _tHp;
    RectTransform _tHpRow;
    Image _barTop, _barBot, _fade;
    float _lastHull = -1f, _dmgT;
    bool _controlsShown = true;

    class ToastItem { public RectTransform rt; public Ui.Box box; public Text text; public float age; }
    readonly List<ToastItem> _toasts = new List<ToastItem>();

    // the cargo ship services panel
    RectTransform _services;
    Text _svcEyebrow, _svcSub, _svcCredits;
    Ui.Scroll _svcScroll;
    Ui.Btn _departBtn, _navBtn, _depositBtn;
    Text _resetLink;
    bool _resetArmed;
    float _resetT;
    bool _servicesVisible;
    string _svcSig = "";
    RectTransform _refitsBox;

    // the inventory panel
    RectTransform _inv;
    Text _invCap, _invCredits;
    Ui.SegBar _invBar;
    Ui.Scroll _invScroll;
    string _invSig = "";
    public readonly List<Slot> holdSlots = new List<Slot>();
    public readonly List<Slot> storeSlots = new List<Slot>();
    Slot _dragging;
    RectTransform _dragPreview;

    // the nav computer
    RectTransform _map, _mapCard, _chartRt;
    Ui.Chart _chart;
    Ui.Scroll _zinfo;
    Text _mapFuel;
    Data.Zone _mapSel;

    // the tutorial card and its rings
    RectTransform _tutBox;
    Text _tutStep, _tutTitle, _tutText, _tutWait;
    Ui.Btn _tutNext;
    RectTransform _tutNextChip;
    bool _tutHidden;
    Ui.Rings _rings;
    readonly Dictionary<string, RectTransform> _ringTargets = new Dictionary<string, RectTransform>();
    readonly List<string> _ringNames = new List<string>();

    public bool InvOpen { get { return _inv != null && _inv.gameObject.activeSelf; } }
    public bool MapOpen { get { return _map != null && _map.gameObject.activeSelf; } }
    public bool ServicesVisible { get { return _services != null && _services.gameObject.activeSelf; } }
    public bool MenuVisible { get { return menu != null && menu.Visible; } }

    public void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _scaler = gameObject.AddComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1280f, 720f);
        _scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _root = GetComponent<RectTransform>();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        _cam = ship != null ? ship.cam : Camera.main;
        // full-screen effects under everything
        _vignette = FullGraphic<Ui.Vignette>("Vignette");
        _dmg = FullGraphic<Ui.Vignette>("Damage");
        _dmg.tint = Ui.RED;
        _dmg.inner = 0.45f;
        _dmg.strength = 0f;
        // the shield-down warning: a faint red-orange edge that breathes while the shield is at zero
        _shieldOut = FullGraphic<Ui.Vignette>("ShieldOut");
        _shieldOut.tint = Data.Hex("#ff5a3c");
        _shieldOut.inner = 0.5f;
        _shieldOut.strength = 0f;
        _speed = FullGraphic<Ui.SpeedStreaks>("SpeedStreaks");
        _blips = FullGraphic<Ui.Blips>("Blips");
        _reticleRt = Ui.Rect("Reticle", _root, Ui.BL, Ui.MID, Vector2.zero, new Vector2(64f, 64f));
        _reticle = _reticleRt.gameObject.AddComponent<Ui.Reticle>();
        _reticle.raycastTarget = false;
        _reticleRt.gameObject.SetActive(false);
        _marker = Ui.Marker.Make(_root, Ui.AMBER);
        _fieldMarker = Ui.Marker.Make(_root, Ui.MUTED, false, true);
        // the gunnery: the crosshair on the nose ray at gun range, and the lead pip for a locked raider
        _crosshairRt = Ui.Rect("Crosshair", _root, Ui.BL, Ui.MID, Vector2.zero, new Vector2(48f, 48f));
        _crosshair = _crosshairRt.gameObject.AddComponent<Ui.Crosshair>();
        _crosshair.raycastTarget = false;
        _crosshairRt.gameObject.SetActive(false);
        BuildStatus();
        BuildReadouts();
        BuildTarget();
        BuildControls();
        BuildPrompt();
        BuildNotice();
        _toastBox = Ui.Rect("Toasts", _root, Ui.TC, Ui.TC, new Vector2(0f, -22f), new Vector2(900f, 300f));
        BuildCine();
        var vt = Ui.Fixed(_root, "v" + Data.VERSION, "mono", 11, Ui.A(Ui.DIM, 0.75f), Ui.BL, new Vector2(14f, 8f));
        _version = vt.rectTransform;
        BuildServices();
        BuildInventory();
        BuildMap();
        BuildTutorial();
        menu = new Menu();
        menu.Build(_root, this);
        SetScale(State.hudScale);
        _controlsShown = State.controlsShown;
        _controls.gameObject.SetActive(_controlsShown);
        if (Ui.fontsMissing > 0) Debug.LogWarning("hud: " + Ui.fontsMissing + " font roles fell back to the built-in font");
    }

    T FullGraphic<T>(string name) where T : MaskableGraphic
    {
        var rt = Ui.Stretch(name, _root);
        rt.pivot = Vector2.zero;
        var g = rt.gameObject.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    /// A glass pane hung from one point of the screen.
    RectTransform Pane(string name, Vector2 anchor, Vector2 pos, Vector2 size, out Ui.Pane pane, float cut = 14f, bool brackets = true)
    {
        var rt = Ui.Rect(name, _root, anchor, anchor, pos, size);
        pane = rt.gameObject.AddComponent<Ui.Pane>();
        pane.cut = cut;
        pane.brackets = brackets;
        pane.raycastTarget = false;
        return rt;
    }

    /// HUD size (Settings): the whole canvas scales about the corners its pieces hang from.
    public void SetScale(float s)
    {
        s = Mathf.Clamp(s, 0.7f, 1.6f);
        _scaler.referenceResolution = new Vector2(1280f / s, 720f / s);
    }

    // ---- bottom centre: the ship (.hud-tl)
    void BuildStatus()
    {
        _status = Pane("Status", Ui.BC, new Vector2(0f, 18f), new Vector2(972f, 67f), out _statusPane);
        float x = 18f, top = -12f;
        _gHull = Ui.Gauge.Make(_status, "Hull", Ui.GREEN, x, top - (43f - 29f), 150f); x += 168f;
        _gShield = Ui.Gauge.Make(_status, "Shield", Data.Hex("#8fe8ff"), x, top - (43f - 29f), 150f); x += 168f;
        _gFuel = Ui.Gauge.Make(_status, "Fuel", Ui.CYAN, x, top - (43f - 29f), 150f); x += 168f;
        var se = Ui.Eyebrow(_status, "Speed", Ui.HUD_DIM);
        se.alignment = TextAnchor.UpperCenter;
        Ui.At(se.rectTransform, Ui.TL, Ui.TL, new Vector2(x, top), new Vector2(96f, 14f));
        _speedBig = Ui.Glow(Ui.Label(_status, "0", "mono_semi", 24, Ui.GLOW_TEXT, TextAnchor.LowerCenter), Ui.HUD_GLOW, 1.5f);
        Ui.At(_speedBig.rectTransform, Ui.TL, Ui.TL, new Vector2(x, top - 14f), new Vector2(96f, 29f));
        x += 114f;
        _gThr = Ui.Gauge.Make(_status, "Thrust", Ui.AMBER, x, top - (43f - 31f), 150f, false, 10f); x += 168f;
        _gCargo = Ui.Gauge.Make(_status, "Cargo", Ui.CARGO, x, top - (43f - 29f), 150f);
    }

    // ---- top right: the situation (.hud-tr .readouts)
    void BuildReadouts()
    {
        Ui.Pane p;
        _readouts = Pane("Readouts", Ui.TR, new Vector2(-20f, -18f), new Vector2(300f, 58f), out p);
        _row1 = ReadoutRow(-10f);
        _row2 = ReadoutRow(-32f);
    }

    Text ReadoutRow(float y)
    {
        var t = Ui.Glow(Ui.Label(_readouts, "", "mono", 12, Ui.HUD_DIM, TextAnchor.UpperRight), Ui.A(Ui.HUD_GLOW, 0.35f));
        Ui.At(t.rectTransform, Ui.TR, Ui.TR, new Vector2(-14f, y), new Vector2(600f, 16f));
        return t;
    }

    static string Kv(string key, string val) { return key + " " + Ui.Col(val, Ui.GLOW_TEXT); }
    static string Kbd(string k) { return "<b>" + Ui.Col(k, Ui.GLOW_TEXT) + "</b>"; }

    // ---- top centre: the target (.hud-tc .target)
    void BuildTarget()
    {
        Ui.Pane p;
        _target = Pane("Target", Ui.TC, new Vector2(0f, -56f), new Vector2(230f, 92f), out p);
        _tEyebrow = Ui.Eyebrow(_target, "Target", Ui.HUD_DIM);
        _tEyebrow.alignment = TextAnchor.UpperCenter;
        Ui.At(_tEyebrow.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -10f), new Vector2(400f, 14f));
        _tName = Ui.Glow(Ui.Label(_target, "", "display", 15, Color.white, TextAnchor.UpperCenter), Ui.A(Ui.CYAN, 0.5f));
        Ui.At(_tName.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -27f), new Vector2(400f, 20f));
        _tRows = Ui.Glow(Ui.Label(_target, "", "mono", 12, Ui.HUD_DIM, TextAnchor.UpperCenter), Ui.A(Ui.HUD_GLOW, 0.35f));
        Ui.At(_tRows.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -50f), new Vector2(400f, 16f));
        _tHpRow = Ui.Rect("Hp", _target, Ui.TC, Ui.TC, new Vector2(0f, -70f), new Vector2(202f, 12f));
        var brt = Ui.Rect("Bar", _tHpRow, Ui.TL, Ui.TL, new Vector2(0f, -2f), new Vector2(140f, 8f));
        _tHp = brt.gameObject.AddComponent<Ui.SegBar>();
        _tHp.fill = Ui.AMBER2;
        _tHp.raycastTarget = false;
        _tHpT = Ui.Label(_tHpRow, "", "mono", 11, Ui.MUTED, TextAnchor.MiddleLeft);
        Ui.At(_tHpT.rectTransform, Ui.TL, Ui.TL, new Vector2(148f, 0f), new Vector2(60f, 12f));
        _tWarn = Ui.Label(_target, "", "body", 12, Ui.AMBER, TextAnchor.UpperCenter);
        Ui.At(_tWarn.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -88f), new Vector2(400f, 16f));
        _target.gameObject.SetActive(false);
    }

    // ---- bottom left: the flight controls list (.hud-bl .controls), C hides it
    static readonly object[][] CONTROL_ROWS =
    {
        new object[] { "Mouse", "Yaw · pitch" },
        new object[] { new[] { "W", "S" }, "Throttle up · down" },
        new object[] { new[] { "X" }, "Cut throttle · S at zero fires retros" },
        new object[] { new[] { "Space" }, "Hold · drift brake: engine cuts, retros slow you, nose swings free" },
        new object[] { new[] { "A", "D" }, "Roll left · right" },
        new object[] { new[] { "Shift" }, "Afterburner while throttled up (×2 speed from the start, ×5 with the refits · burns fuel fast)" },
        new object[] { new[] { "G" }, "Laser overcharge on · off (needs the refit · up to ×3 damage · the beam draws fuel while it cuts)" },
        new object[] { new[] { "↑", "↓" }, "Pitch" },
        new object[] { new[] { "LMB" }, "Hold to fire the selected weapon (L too). The laser cuts only what the crosshair is on: aim the nose at a rock" },
        new object[] { new[] { "Wheel" }, "Swap between the mining laser and the autocannon" },
        new object[] { new[] { "R" }, "Radar pulse" },
        new object[] { new[] { "Q" }, "Lock the crosshair on whatever the mouse is over · hover another target and press Q to switch · otherwise press Q to release" },
        new object[] { new[] { "F" }, "Flashlight on · off in flight · cargo ship services when docked" },
        new object[] { new[] { "T" }, "Out of fuel · recovery to the cargo ship (15% of credits)" },
        new object[] { new[] { "E" }, "Approach control within 2,250 m of the cargo ship · deposit ore on the pad" },
        new object[] { new[] { "Tab", "I" }, "Inventory · slots of 100 · jettison stacks" },
        new object[] { new[] { "N" }, "Nav map · warp (docked in the cargo ship)" },
        new object[] { new[] { "C" }, "Hide · show this list" },
        new object[] { new[] { "F5" }, "Quick-save" },
        new object[] { new[] { "F9" }, "Test · spawn 3 raiders 2,000 to 4,000 m out" },
        new object[] { new[] { "F10" }, "Test · raiders hold their fire · again to let them fire" },
        new object[] { new[] { "F8" }, "Test · the refit panel anywhere · bottomless credits · − takes a level off" },
        new object[] { new[] { "Esc" }, "Pause · the menu with settings and controls" },
    };

    void BuildControls()
    {
        Ui.Pane p;
        _controls = Pane("Controls", Ui.BL, new Vector2(20f, 18f), new Vector2(392f, 300f), out p, 14f, true);
        var title = Ui.Label(_controls, "FLIGHT CONTROLS", "display", 10, Ui.HUD_DIM);
        Ui.At(title.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -10f), new Vector2(300f, 14f));
        float y = -30f;
        const float keyW = 72f, descW = 280f;
        foreach (var row in CONTROL_ROWS)
        {
            if (!State.sandbox && row.Length > 1 && row[1] is string td && td.StartsWith("Test")) continue;   // the test keys: the combat test only
            var keys = row[0] as string[];
            if (keys != null) Ui.Keys(_controls, keys, true, 14f, y);
            else
            {
                var k = Ui.Label(_controls, (string)row[0], "body", 11, Ui.A(Ui.MUTED, 0.85f));
                Ui.At(k.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, y), new Vector2(keyW, 16f));
            }
            var d = Ui.Label(_controls, (string)row[1], "body", 11, Ui.A(Ui.MUTED, 0.85f), TextAnchor.UpperLeft, true);
            Ui.At(d.rectTransform, Ui.TL, Ui.TL, new Vector2(14f + keyW + 12f, y - 1f), new Vector2(descW, 0f));
            float h = Mathf.Max(16f, Mathf.Ceil(d.preferredHeight));
            d.rectTransform.sizeDelta = new Vector2(descW, h);
            y -= h + 3f;
        }
        _controls.sizeDelta = new Vector2(14f + keyW + 12f + descW + 14f, -y + 8f);
    }

    // ---- the hint bar above the status pane (.prompt) and the cargo-full notice (.notice)
    void BuildPrompt()
    {
        Ui.Pane p;
        _prompt = Pane("Prompt", Ui.BC, new Vector2(0f, 99f), new Vector2(300f, 36f), out p, 10f, false);
        p.top = new Color(0.031f, 0.047f, 0.102f, 0.8f);
        p.bot = new Color(0.031f, 0.047f, 0.102f, 0.6f);
        _promptText = Ui.Label(_prompt, "", "body", 14, Ui.TEXT, TextAnchor.MiddleCenter);
        Ui.At(_promptText.rectTransform, Ui.MID, Ui.MID, Vector2.zero, new Vector2(900f, 36f));
        _prompt.gameObject.SetActive(false);
        // the hover readout (.hoverLbl): what the mouse is over and how far it is, beside the cursor
        _hoverLbl = Ui.Rect("Hover", _root, Ui.BL, Ui.BL, Vector2.zero, new Vector2(100f, 20f));
        Ui.MakeBox(_hoverLbl, new Color(0.055f, 0.071f, 0.141f, 0.72f), Ui.LINE2, 1f);
        _hoverTxt = Ui.Label(_hoverLbl, "", "mono", 11, Ui.TEXT, TextAnchor.MiddleCenter);
        Ui.At(_hoverTxt.rectTransform, Ui.MID, Ui.MID, Vector2.zero, new Vector2(100f, 20f));
        _hoverLbl.gameObject.SetActive(false);
    }

    void BuildNotice()
    {
        _notice = Ui.Rect("Notice", _root, Ui.BC, Ui.BC, new Vector2(0f, 160f), new Vector2(430f, 54f));
        var box = Ui.MakeBox(_notice, new Color(0.157f, 0.086f, 0.024f, 0.92f), Ui.AMBER, 1f);
        box.leftEdge = Ui.AMBER;
        box.edgeW = 4f;
        var b = Ui.Label(_notice, "CARGO HOLD FULL", "display", 13, Ui.AMBER2, TextAnchor.UpperCenter);
        Ui.At(b.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -8f), new Vector2(400f, 16f));
        var t = Ui.Label(_notice, "Return to the cargo ship to stow it · press " + Kbd("E") + " within " + Data.Fm(Data.DOCK_RANGE) + " to auto-dock", "body", 12, Ui.TEXT, TextAnchor.UpperCenter);
        Ui.At(t.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, -28f), new Vector2(420f, 16f));
        _notice.gameObject.SetActive(false);
    }

    // ---- cutscenes: letterbox bars with a caption (.cine); the fade for a jump
    void BuildCine()
    {
        var fr = Ui.Stretch("Fade", _root);
        _fade = Ui.Fill(fr, new Color(0.008f, 0.012f, 0.039f, 0f));
        var top = Ui.Rect("BarTop", _root, Ui.TL, Ui.TL, Vector2.zero, new Vector2(0f, 80f));
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = new Vector2(1f, 1f);
        _barTop = Ui.Fill(top, Data.Hex("#02030a"));
        var bot = Ui.Rect("BarBot", _root, Ui.BL, Ui.BL, Vector2.zero, new Vector2(0f, 80f));
        bot.anchorMin = new Vector2(0f, 0f); bot.anchorMax = new Vector2(1f, 0f);
        _barBot = Ui.Fill(bot, Data.Hex("#02030a"));
        _caption = Ui.Label(_root, "", "display", 13, Ui.AMBER2, TextAnchor.MiddleCenter);
        Ui.At(_caption.rectTransform, Ui.BC, Ui.BC, new Vector2(0f, 26f), new Vector2(1000f, 20f));
        top.gameObject.SetActive(false);
        bot.gameObject.SetActive(false);
        _caption.gameObject.SetActive(false);
    }

    // ---- toasts (.toast): a dark strip with an amber (or red) left edge, fading in and out
    public void Toast(string msg, bool bad)
    {
        var it = new ToastItem { age = 0f };
        it.rt = Ui.Rect("Toast", _toastBox, Ui.TC, Ui.TC, Vector2.zero, new Vector2(100f, 30f));
        it.box = Ui.MakeBox(it.rt, new Color(0.024f, 0.039f, 0.094f, 0.9f), Ui.LINE2, 1f);
        it.box.leftEdge = bad ? Ui.RED : Ui.AMBER;
        it.box.edgeW = 3f;
        it.text = Ui.Label(it.rt, msg, "body", 13, Ui.TEXT, TextAnchor.MiddleCenter);
        float w = Mathf.Ceil(it.text.preferredWidth) + 31f;
        it.rt.sizeDelta = new Vector2(w, 30f);
        Ui.At(it.text.rectTransform, Ui.MID, Ui.MID, new Vector2(1.5f, 0f), new Vector2(w - 3f, 30f));
        _toasts.Add(it);
        while (_toasts.Count > 6) { Destroy(_toasts[0].rt.gameObject); _toasts.RemoveAt(0); }
        LayoutToasts();
    }

    void LayoutToasts()
    {
        float y = 0f;
        foreach (var t in _toasts)
        {
            t.rt.anchoredPosition = new Vector2(0f, y);
            y -= 36f;
        }
    }

    void TickToasts(float dt)
    {
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            t.age += dt;
            float a = t.age < 0.25f ? t.age / 0.25f : (t.age < 2.55f ? 1f : 1f - (t.age - 2.55f) / 0.64f);
            if (a <= 0f) { Destroy(t.rt.gameObject); _toasts.RemoveAt(i); continue; }
            t.box.alpha = a;
            t.box.SetVerticesDirty();
            t.text.color = Ui.A(Ui.TEXT, a);
        }
        LayoutToasts();
    }

    // ---- the cargo ship services panel (#station): a glass side panel over the live view of the ship on the pad
    void BuildServices()
    {
        _services = Ui.Rect("Services", _root, Ui.TR, Ui.TR, Vector2.zero, new Vector2(580f, 0f));
        _services.anchorMin = new Vector2(1f, 0f);
        _services.anchorMax = new Vector2(1f, 1f);
        _services.pivot = new Vector2(1f, 0.5f);
        var box = Ui.MakeBox(_services, Ui.SIDE_BG, Ui.LINE2, 0f, true);
        box.bl = 1f;
        _svcEyebrow = Ui.Eyebrow(_services, "Docked", Ui.MUTED);
        Ui.At(_svcEyebrow.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -20f), new Vector2(300f, 14f));
        var h2 = Ui.Label(_services, "CARGO SHIP", "display_bold", 24, Ui.TEXT);
        Ui.At(h2.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -36f), new Vector2(300f, 30f));
        _svcSub = Ui.Label(_services, "", "mono", 12, Ui.MUTED);
        Ui.At(_svcSub.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -68f), new Vector2(300f, 16f));
        var be = Ui.Eyebrow(_services, "Balance", Ui.MUTED);
        be.alignment = TextAnchor.UpperRight;
        Ui.At(be.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -20f), new Vector2(200f, 14f));
        _svcCredits = Ui.Label(_services, "0 cr", "mono_semi", 24, Ui.AMBER2, TextAnchor.UpperRight);
        Ui.At(_svcCredits.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -36f), new Vector2(240f, 30f));
        var rule = Ui.Rect("Rule", _services, Ui.TL, Ui.TL, new Vector2(0f, -100f), new Vector2(0f, 1f));
        rule.anchorMax = new Vector2(1f, 1f);
        Ui.Fill(rule, Ui.LINE);
        // footer
        var foot = Ui.Rect("Foot", _services, Ui.BL, Ui.BL, Vector2.zero, new Vector2(0f, 64f));
        foot.anchorMax = new Vector2(1f, 0f);
        var frule = Ui.Rect("Rule", foot, Ui.TL, Ui.TL, Vector2.zero, new Vector2(0f, 1f));
        frule.anchorMax = new Vector2(1f, 1f);
        Ui.Fill(frule, Ui.LINE);
        float x = 26f;
        _departBtn = Ui.Button(foot, "Depart", () => DepartPressed(), true);
        _departBtn.rt.anchoredPosition = new Vector2(x, -14f); x += _departBtn.Width + 10f;
        var wchip = Ui.Chip(foot, "W", false, new Vector2(x, -14f - (36f - 19f) * 0.5f)); x += wchip.sizeDelta.x + 10f;
        _navBtn = Ui.Button(foot, "Warp to the Hub", () => { if (ship != null && ship.docked && !zone.hub) ship.StartWarp(Data.ZONE_HUB); });
        _navBtn.rt.anchoredPosition = new Vector2(x, -14f); x += _navBtn.Width + 10f;
        var hide = Ui.Button(foot, "Hide", () => ToggleServices());
        hide.rt.anchoredPosition = new Vector2(x, -14f); x += hide.Width + 10f;
        Ui.Chip(foot, "F", false, new Vector2(x, -14f - (36f - 19f) * 0.5f));
        _resetLink = Ui.Link(foot, "Reset save", () => ResetPressed());
        Ui.At(_resetLink.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -20f), _resetLink.rectTransform.sizeDelta);
        _svcScroll = Ui.Scroll.Make(_services, 0f, 64f, 0f, 101f);
        _services.gameObject.SetActive(false);
    }

    void DepartPressed()
    {
        if (ship == null) return;
        if (zone.hub) ToggleMap();
        else ship.StartDeparture();
    }

    void ResetPressed()
    {
        if (_resetArmed)
        {
            _resetArmed = false;
            _resetLink.text = "Reset save";
            if (game != null) game.WipeSave();
            return;
        }
        _resetArmed = true;
        _resetT = 4f;
        _resetLink.text = "Click again to wipe save";
        _resetLink.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(_resetLink.preferredWidth) + 8f, _resetLink.rectTransform.sizeDelta.y);
    }

    public void OnDocked(bool isDocked)
    {
        if (isDocked && !_servicesVisible) _servicesVisible = true;
        if (!isDocked)
        {
            _servicesVisible = false;
            if (MapOpen) CloseMap();
        }
        _services.gameObject.SetActive(isDocked && _servicesVisible);
        if (isDocked) { _svcSig = ""; RefreshServices(); _invSig = ""; }
    }

    public void ToggleServices()
    {
        if (ship == null || (!ship.docked && !State.sandbox)) return;   // the combat test: the refits anywhere
        _servicesVisible = !_servicesVisible;
        _services.gameObject.SetActive(_servicesVisible);
        if (_servicesVisible) { _svcSig = ""; RefreshServices(); }
    }

    /// One refit row (.up): name and pips, the description with the next level in bold, the price button on the right.
    void RefitRow(Ui.Flow f, string name, int total, int have, string desc, float cost, bool maxed, Action fn, bool first, Action down = null)
    {
        if (!first) { f.Rule(); f.Gap(13f); }
        float top = f.y;
        var n = Ui.Label(f.parent, name, "display", 16, Ui.TEXT);
        Ui.At(n.rectTransform, Ui.TL, Ui.TL, new Vector2(f.x, f.y), new Vector2(300f, 20f));
        float px = f.x + Ui.Measure(n) + 12f;
        for (int j = 0; j < total; j++)
        {
            var pip = Ui.Rect("Pip", f.parent, Ui.TL, Ui.TL, new Vector2(px + j * 13f, f.y - 5f), new Vector2(8f, 8f));
            Ui.Fill(pip, j < have ? Ui.AMBER : Ui.LINE2);
        }
        f.y -= 23f;
        f.Para(desc, "body", 13, Ui.MUTED, 0f, TextAnchor.UpperLeft, 0f, f.w - 118f - 14f - (down != null ? 48f : 0f));
        float rowH = top - f.y;
        var b = Ui.Button(f.parent, maxed ? "Max" : Data.Fmt(cost) + " cr", fn, !maxed && State.credits >= cost, true, 118f, 14);
        b.rt.anchoredPosition = new Vector2(f.x + f.w - 118f, top - (rowH - b.Height) * 0.5f);
        if (maxed || State.credits < cost) b.interactable = false;
        if (down != null)
        {
            // the test's minus: a level off
            var m = Ui.Button(f.parent, "−", down, have > 1, true, 40f, 14);
            m.rt.anchoredPosition = new Vector2(f.x + f.w - 118f - 48f, top - (rowH - m.Height) * 0.5f);
            if (have <= 1) m.interactable = false;
        }
        f.Gap(13f);
    }

    Text H3(Ui.Flow f, string text, float gap = 12f)
    {
        return f.Para(text.ToUpperInvariant(), "display", 13, Ui.MUTED, gap);
    }

    public void RefreshServices()
    {
        if (ship == null || _services == null) return;
        bool atHub = ship.hold;
        string sig = atHub + "|" + Mathf.RoundToInt(State.credits) + "|" + State.StoreUsed() + "|" + Mathf.FloorToInt(State.shipFuel) + "|" + Mathf.FloorToInt(State.parts) + "|" + State.CargoTotal().ToString("0") + "|" + State.StoreTotal().ToString("0") + "|" + Mathf.RoundToInt(State.droneUnits);
        foreach (var k in Data.UPGRADE_KEYS) sig += State.up[k];
        foreach (var k in Data.DEPOT_KEYS) sig += State.depot[k];
        foreach (var k in Data.ORE_KEYS) sig += "," + State.cargo[k].ToString("0") + "/" + State.store[k].ToString("0") + "/" + State.market[k].ToString("0.00");
        if (sig == _svcSig) return;
        _svcSig = sig;
        _svcEyebrow.text = atHub ? "HOLDING STATION" : "DOCKED";
        _svcSub.text = atHub ? "Off " + (zone.colony ?? "the colony") : CargoShip.BayName(ship.dockSide);
        _svcCredits.text = Data.Fmt(State.credits) + " cr";
        _departBtn.SetText(atHub ? "Nav map" : "Depart");
        _navBtn.rt.gameObject.SetActive(!atHub);
        _svcScroll.Clear();
        float w = _svcScroll.Width;
        var f = new Ui.Flow(_svcScroll.content, 26f, 20f, w - 52f);
        H3(f, atHub ? "Cargo and market" : "Cargo");
        int su = State.StoreUsed();
        bool full = su >= Data.STORE_SLOTS;
        Gauge(f, "Cargo ship storage", full ? Ui.AMBER : Ui.CARGO, State.StoreTotal() / (Data.STORE_SLOTS * Data.STACK), su + " / " + Data.STORE_SLOTS + " slots" + (full ? " · FULL" : ""));
        bool lowFuel = State.shipFuel < Data.CARGO_FUEL_CAP * 0.2f;
        Gauge(f, "Cargo ship fuel supply", lowFuel ? Ui.AMBER : Ui.CYAN, State.shipFuel / Data.CARGO_FUEL_CAP, Mathf.FloorToInt(State.shipFuel) + " / " + Mathf.RoundToInt(Data.CARGO_FUEL_CAP));
        bool lowParts = State.parts < Data.PARTS_CAP * 0.2f;
        Gauge(f, "Repair parts", lowParts ? Ui.AMBER : Ui.GREEN, State.parts / Data.PARTS_CAP, Mathf.FloorToInt(State.parts) + " / " + Data.PARTS_CAP);
        if (atHub) Market(f);
        f.Gap(6f);
        H3(f, "Your hold");
        var lines = new List<string>();
        foreach (var k in Data.ORE_KEYS) if (State.cargo[k] > 0.5f) lines.Add(Data.ORES[Data.OreIndex(k)].name + " " + Mathf.RoundToInt(State.cargo[k]) + " u");
        f.Para(lines.Count > 0 ? string.Join(", ", lines.ToArray()) + "  ·  worth " + Data.Fmt(State.ValueOf(State.cargo)) + " cr at the Hub" : "The hold is empty.", "body", 13, Ui.MUTED, 12f);
        var row = f.Box(36f);
        _depositBtn = Ui.Button(row, "Deposit all", () => { if (ship != null) ship.DepositAll(); }, lines.Count > 0);
        _depositBtn.interactable = lines.Count > 0;
        float bx = _depositBtn.Width + 10f;
        var chip = Ui.Chip(row, "E", false, new Vector2(bx, -(36f - 19f) * 0.5f)); bx += chip.sizeDelta.x + 10f;
        Ui.Button(row, "Take all", () => { if (ship != null) ship.TakeAll(); }).rt.anchoredPosition = new Vector2(bx, 0f);
        f.Gap(20f);
        f.Rule();
        f.Gap(20f);
        H3(f, "Personal ship");
        float refitsTop = f.y;
        bool first = true;
        foreach (var key in Data.UPGRADE_KEYS)
        {
            var u = Data.UPGRADES[key];
            int i = State.up[key];
            bool maxed = i >= u.costs.Length;
            string desc = maxed ? Ui.Col("<b>" + Data.Describe(key, i) + "</b>", Ui.TEXT) + " · Fully upgraded" : Data.Describe(key, i) + " → " + Ui.Col("<b>" + Data.Describe(key, i + 1) + "</b>", Ui.TEXT);
            var k2 = key;
            RefitRow(f, u.name, u.levels.Length, i + 1, desc, maxed ? 0f : u.costs[i], maxed, () => Buy(k2), first, State.sandbox ? () => Downgrade(k2) : (Action)null);
            first = false;
        }
        _refitsBox = Ui.Rect("RefitsBox", _svcScroll.content, Ui.TL, Ui.TL, new Vector2(f.x, refitsTop), new Vector2(f.w, refitsTop - f.y));
        f.Gap(8f);
        H3(f, "Cargo ship", 10f);
        first = true;
        foreach (var key in Data.DEPOT_KEYS)
        {
            var u = Data.DEPOT_UPGRADES[key];
            int i = State.depot[key];
            bool maxed = i >= u.costs.Length;
            string desc = maxed ? Ui.Col("<b>" + Data.DescribeDepot(key, i) + "</b>", Ui.TEXT) + " · Fully upgraded" : Data.DescribeDepot(key, i) + " → " + Ui.Col("<b>" + Data.DescribeDepot(key, i + 1) + "</b>", Ui.TEXT);
            var k2 = key;
            RefitRow(f, u.name, u.costs.Length, i, desc, maxed ? 0f : u.costs[i], maxed, () => BuyDepot(k2), first);
            first = false;
        }
        f.Para("The dish leaves the ore it frees adrift for you to pick up. Collector drones gather it and stow it in the cargo ship storage" + (State.droneUnits > 0.5f ? " · <b>" + Data.Fmt(State.droneUnits) + "</b> stowed so far" : "") + ".", "body", 12, Ui.DIM, 20f);
        _svcScroll.SetHeight(f.Used);
    }

    void Gauge(Ui.Flow f, string title, Color c, float v, string text)
    {
        var g = Ui.Gauge.Make(f.parent, title, c, f.x, f.y, f.w, true, 10f);
        g.Show(v, text, c);
        f.y -= g.height + 12f;
    }

    void BuyDepot(string key)
    {
        string msg;
        bool ok = State.BuyDepot(key, out msg);
        Toast(msg, !ok);
        if (ok) Audio.Play("chime");
        _svcSig = "";
        RefreshServices();
    }

    void Buy(string key)
    {
        string msg;
        bool ok = State.Buy(key, out msg);
        Toast(msg, !ok);
        if (ok) Audio.Play("chime");
        if (ok && ship != null) ship.ConfigureModel();   // the fitting on the hull changes with its tier
        _svcSig = "";
        RefreshServices();
    }

    void Downgrade(string key)
    {
        string msg;
        bool ok = State.Downgrade(key, out msg);
        Toast(msg, !ok);
        if (ok && ship != null) ship.ConfigureModel();
        _svcSig = "";
        RefreshServices();
    }

    static RectTransform Dot(RectTransform parent, Color c, Vector2 pos)
    {
        var d = Ui.Rect("Dot", parent, Ui.TL, Ui.MID, pos, new Vector2(7f, 7f));
        d.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Ui.Fill(d, c);
        return d;
    }

    Text Cell(RectTransform row, string text, float x, float w, bool right, Color c, string kind = "mono", int size = 13)
    {
        var t = Ui.Label(row, text, kind, size, c, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
        Ui.At(t.rectTransform, Ui.TL, Ui.TL, new Vector2(x, 0f), new Vector2(w, row.sizeDelta.y));
        return t;
    }

    static string Delta(string k)
    {
        int d = Mathf.RoundToInt((State.market[k] - 1f) * 100f);
        if (d == 0) return "";
        return d > 0 ? " " + Ui.Col("▲" + d + "%", Ui.GREEN) : " " + Ui.Col("▼" + (-d) + "%", Ui.RED);
    }

    /// The colony market (renderMarket): what is aboard and what it fetches, the sell buttons, today's prices; and the
    /// cargo ship's fuel and parts purchases.
    void Market(Ui.Flow f)
    {
        float fuelCost = Mathf.Ceil((Data.CARGO_FUEL_CAP - State.shipFuel) * Data.CARGO_FUEL_PRICE);
        float partsCost = Mathf.Ceil((Data.PARTS_CAP - State.parts) * Data.PARTS_PRICE);
        var row = f.Box(36f);
        var fb = Ui.Button(row, fuelCost > 0f ? "Refuel supply · " + Data.Fmt(fuelCost) + " cr" : "Fuel supply full", () => { if (ship != null) ship.RefuelCargoShip(); }, fuelCost > 0f && State.credits >= 1f);
        fb.interactable = fuelCost > 0f;
        var pb = Ui.Button(row, partsCost > 0f ? "Restock parts · " + Data.Fmt(partsCost) + " cr" : "Parts store full", () => { if (ship != null) ship.BuyParts(); }, partsCost > 0f && State.credits >= Data.PARTS_PRICE);
        pb.interactable = partsCost > 0f;
        pb.rt.anchoredPosition = new Vector2(fb.Width + 10f, 0f);
        f.Gap(14f);
        H3(f, (zone.colony ?? "Colony") + " market", 8f);
        float[] cw = { 150f, 60f, 74f, 90f, 90f, 40f };
        string[] heads = { "Ore", "Hold", "Storage", "cr each", "Value", "" };
        var hr = f.Box(18f);
        float cx = 0f;
        for (int c = 0; c < heads.Length; c++) { Cell(hr, heads[c].ToUpperInvariant(), cx, cw[c], c > 0, Ui.DIM, "display_med", 11); cx += cw[c] + 8f; }
        float total = 0f;
        bool any = false;
        foreach (var k in Data.ORE_KEYS)
        {
            float h = State.cargo[k], s = State.store[k];
            if (h + s < 0.5f) continue;
            any = true;
            float p = State.Price(k);
            float v = (h + s) * p;
            total += v;
            var o = Data.ORES[Data.OreIndex(k)];
            f.Gap(4f);
            f.Rule();
            f.Gap(3f);
            var r = f.Box(22f);
            cx = 0f;
            Dot(r, o.color, new Vector2(4f, -11f));
            Cell(r, o.name + (o.zone != null ? " " + Ui.Col("exotic +" + Mathf.RoundToInt((Data.EXPORT_BONUS - 1f) * 100f) + "%", Ui.GREEN) : ""), 16f, cw[0] - 16f, false, Ui.TEXT, "body"); cx += cw[0] + 8f;
            Cell(r, Data.Fmt(h), cx, cw[1], true, Ui.TEXT); cx += cw[1] + 8f;
            Cell(r, Data.Fmt(s), cx, cw[2], true, Ui.TEXT); cx += cw[2] + 8f;
            Cell(r, p.ToString("0.0") + Delta(k), cx, cw[3], true, Ui.TEXT); cx += cw[3] + 8f;
            Cell(r, Data.Fmt(v), cx, cw[4], true, Ui.TEXT); cx += cw[4] + 8f;
            var kk = k;
            var sl = Ui.Link(r, "sell", () => { if (ship != null) ship.Sell(new[] { kk }, true, true); }, 12);
            Ui.At(sl.rectTransform, Ui.TR, Ui.TR, new Vector2(0f, 0f), new Vector2(40f, 22f));
        }
        if (any)
        {
            f.Gap(6f);
            var tr = f.Box(20f);
            Cell(tr, "Everything aboard", 0f, 300f, false, Ui.TEXT);
            Cell(tr, Data.Fmt(total) + " cr", f.w - 200f, 200f, true, Ui.AMBER2, "mono_semi");
            f.Gap(10f);
            var br = f.Box(36f);
            var b1 = Ui.Button(br, "Sell everything", () => { if (ship != null) ship.Sell(Data.ORE_KEYS, true, true); }, true);
            var b2 = Ui.Button(br, "Sell hold only", () => { if (ship != null) ship.Sell(Data.ORE_KEYS, true, false); });
            b2.rt.anchoredPosition = new Vector2(b1.Width + 10f, 0f);
            var b3 = Ui.Button(br, "Sell storage only", () => { if (ship != null) ship.Sell(Data.ORE_KEYS, false, true); });
            b3.rt.anchoredPosition = new Vector2(b1.Width + b2.Width + 20f, 0f);
        }
        else f.Para("Nothing aboard to sell. Cut ore in a belt zone and bring it back.", "body", 13, Ui.DIM);
        f.Gap(14f);
        var ph = f.Box(18f);
        Cell(ph, "PRICES TODAY", 0f, 200f, false, Ui.DIM, "display_med", 11);
        Cell(ph, "CR EACH", f.w - 120f, 120f, true, Ui.DIM, "display_med", 11);
        foreach (var k in Data.ORE_KEYS)
        {
            var o = Data.ORES[Data.OreIndex(k)];
            f.Gap(4f);
            f.Rule();
            f.Gap(3f);
            var r = f.Box(22f);
            Dot(r, o.color, new Vector2(4f, -11f));
            Cell(r, o.name + (o.zone != null ? " " + Ui.Col("exotic", Ui.DIM) : ""), 16f, 200f, false, Ui.TEXT, "body");
            int need = o.unlock;
            if (need > State.up["laser"] + 1) Cell(r, "needs laser Lv" + need, 200f, 170f, true, Ui.DIM);
            Cell(r, State.Price(k).ToString("0.0") + Delta(k), f.w - 120f, 120f, true, Ui.TEXT);
        }
        f.Gap(10f);
    }

    // ---- the inventory (#inv): a glass panel from the left edge with the hold's slots (and the storage's while docked)
    /// An inventory slot (.slot): a stack in the hold or in the cargo ship's storage, or an empty one. Stacks drag, as in
    /// the browser: a hold stack onto the storage grid stows it, a storage stack onto the hold grid takes it back, and a
    /// hold stack let go anywhere else is jettisoned. A double-click moves a stack across too; ✕ jettisons a hold stack.
    public class Slot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Hud hud;
        public string k = "";
        public float u;
        public bool store, docked;
        public Ui.Box box;
        bool _drag;
        public bool IsEmpty { get { return string.IsNullOrEmpty(k); } }

        public bool Accepts(Slot from)
        {
            if (from == null || from.IsEmpty) return false;
            return from.store ? !store : true;
        }

        public void SetOver(bool on)
        {
            if (on) box.Set(Ui.A(Ui.AMBER, 0.12f), Ui.AMBER);
            else if (IsEmpty) box.Set(Ui.A(Ui.PANEL2, 0.45f), Ui.A(Ui.LINE2, 0.45f));
            else box.Set(Ui.PANEL2, Ui.LINE2);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _drag = !IsEmpty && !(store && !docked) && hud.BeginDrag(this, e.position);
        }

        public void OnDrag(PointerEventData e) { if (_drag) hud.DragTo(e.position); }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_drag) return;
            _drag = false;
            Slot target = e.pointerEnter != null ? e.pointerEnter.GetComponentInParent<Slot>() : null;
            hud.EndDrag(this, target);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.clickCount == 2 && docked && !IsEmpty)
            {
                if (store) hud.TakeStack(k, u); else hud.StowStack(k, u);
            }
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (hud._dragging != null && hud._dragging != this && hud._dragging.store != store && Accepts(hud._dragging)) SetOver(true);
        }

        public void OnPointerExit(PointerEventData e) { SetOver(false); }
    }

    /// The stack's face: name, units of the stack, value at Hub prices.
    static void SlotVisual(RectTransform rt, string ore, float units)
    {
        var o = Data.ORES[Data.OreIndex(ore)];
        var n = Ui.Label(rt, o.name, "body_semi", 14, Ui.TEXT);
        Ui.At(n.rectTransform, Ui.TL, Ui.TL, new Vector2(12f, -12f), new Vector2(120f, 18f));
        var c = Ui.Label(rt, Mathf.FloorToInt(units) + " / " + Data.STACK, "mono", 14, Ui.TEXT);
        Ui.At(c.rectTransform, Ui.TL, Ui.TL, new Vector2(12f, -34f), new Vector2(120f, 18f));
        var v = Ui.Label(rt, Data.Fmt(units * State.Price(ore)) + " cr", "mono", 12, Ui.DIM);
        Ui.At(v.rectTransform, Ui.TL, Ui.TL, new Vector2(12f, -56f), new Vector2(120f, 16f));
    }

    Slot MakeSlot(RectTransform parent, Vector2 pos, float w, State.Stack st, bool store, bool docked)
    {
        var rt = Ui.Rect(store ? "StoreSlot" : "HoldSlot", parent, Ui.TL, Ui.TL, pos, new Vector2(w, 92f));
        var s = rt.gameObject.AddComponent<Slot>();
        s.hud = this;
        s.store = store;
        s.docked = docked;
        s.box = Ui.MakeBox(rt, Ui.PANEL2, Ui.LINE2, 1f, true);
        if (st == null)
        {
            s.box.Set(Ui.A(Ui.PANEL2, 0.45f), Ui.A(Ui.LINE2, 0.45f));
            var e = Ui.Label(rt, "EMPTY", "body", 12, Ui.A(Ui.DIM, 0.6f), TextAnchor.MiddleCenter);
            Ui.At(e.rectTransform, Ui.MID, Ui.MID, Vector2.zero, new Vector2(w, 92f));
            return s;
        }
        s.k = st.k;
        s.u = st.u;
        s.box.topEdge = Data.ORES[Data.OreIndex(st.k)].color;
        s.box.edgeW = 2f;
        SlotVisual(rt, st.k, st.u);
        if (!store)
        {
            var x = Ui.Rect("X", rt, Ui.TR, Ui.TR, new Vector2(-4f, -4f), new Vector2(20f, 20f));
            Ui.MakeBox(x, Ui.PANEL, Ui.LINE2, 1f, true);
            var xt = Ui.Label(x, "✕", "body", 11, Ui.MUTED, TextAnchor.MiddleCenter);
            Ui.At(xt.rectTransform, Ui.MID, Ui.MID, Vector2.zero, new Vector2(20f, 20f));
            var xb = x.gameObject.AddComponent<Button>();
            xb.transition = Selectable.Transition.None;
            var kk = st.k; var uu = st.u;
            xb.onClick.AddListener(() => Jettison(kk, uu));
            var hv = x.gameObject.AddComponent<Ui.Hover>();
            hv.text = xt; hv.normal = Ui.MUTED; hv.over = Ui.RED;
        }
        return s;
    }

    bool BeginDrag(Slot s, Vector2 screen)
    {
        _dragging = s;
        s.box.alpha = 0.35f;
        s.box.SetVerticesDirty();
        _dragPreview = Ui.Rect("DragPreview", _root, Ui.BL, Ui.MID, Vector2.zero, s.GetComponent<RectTransform>().sizeDelta);
        var b = Ui.MakeBox(_dragPreview, Ui.PANEL2, Ui.AMBER, 1f);
        b.alpha = 0.85f;
        SlotVisual(_dragPreview, s.k, s.u);
        foreach (var t in _dragPreview.GetComponentsInChildren<Text>(true)) t.raycastTarget = false;
        _dragPreview.SetAsLastSibling();
        DragTo(screen);
        return true;
    }

    void DragTo(Vector2 screen)
    {
        if (_dragPreview != null) _dragPreview.anchoredPosition = screen / _canvas.scaleFactor;
    }

    void EndDrag(Slot from, Slot target)
    {
        if (_dragPreview != null) Destroy(_dragPreview.gameObject);
        _dragPreview = null;
        _dragging = null;
        from.box.alpha = 1f;
        from.box.SetVerticesDirty();
        foreach (var s in holdSlots) s.SetOver(false);
        foreach (var s in storeSlots) s.SetOver(false);
        if (target != null && target != from && target.store != from.store && target.Accepts(from)) DropStack(from.k, from.u, from.store, target.store);
        else if (target == null && !from.store) Jettison(from.k, from.u);   // let go outside the grids: into space with it
    }

    /// A stack dropped on a slot: from the storage onto the hold grid it comes back aboard, from the hold onto the storage
    /// grid it is stowed; a hold stack on a hold slot pours or swaps in the browser, and the sorted grid already shows that.
    public void DropStack(string k, float u, bool fromStore, bool ontoStore)
    {
        if (fromStore && !ontoStore) TakeStack(k, u);
        else if (!fromStore && ontoStore) StowStack(k, u);
    }

    /// Jettison a stack (jettisonSlot): it leaves the hold and drifts off behind the ship as a lump that cannot be pulled
    /// back in for a minute, so a dropped stack is not scooped straight up again.
    public void Jettison(string k, float u)
    {
        float dropped = State.Jettison(k, u);
        if (dropped > 0.5f)
        {
            if (ship != null && game != null)
            {
                var back = -ship.Forward;
                var at = ship.transform.position + back * 40f * Data.SHIP_SCALE + UnityEngine.Random.insideUnitSphere * 10f;
                var drift = ship.vel + back * 45f + UnityEngine.Random.insideUnitSphere * 8f;
                var p = game.SpawnPickup(k, dropped, at, drift);
                if (p != null) p.noPick = 60f;
            }
            Audio.Play("stow", -6f);
            Toast("Jettisoned " + Mathf.RoundToInt(dropped) + " " + Data.ORES[Data.OreIndex(k)].name, false);
        }
        _invSig = "";
        RefreshInventory();
    }

    public void StowStack(string k, float u)
    {
        float moved = State.StowStack(k, u);
        if (moved < 0.5f) Toast("Cargo ship storage is full", true);
        else
        {
            Audio.Play("stow");
            Toast("Stowed " + Mathf.RoundToInt(moved) + " " + Data.ORES[Data.OreIndex(k)].name + " aboard the cargo ship", false);
            State.Save();
        }
        _invSig = "";
        RefreshInventory();
        _svcSig = "";
        RefreshServices();
    }

    public void TakeStack(string k, float u)
    {
        float moved = State.TakeStack(k, u);
        if (moved < 0.5f) Toast("No room in the hold", true);
        else
        {
            Audio.Play("stow");
            Toast("Took " + Mathf.RoundToInt(moved) + " " + Data.ORES[Data.OreIndex(k)].name + " back aboard", false);
            State.Save();
        }
        _invSig = "";
        RefreshInventory();
        _svcSig = "";
        RefreshServices();
    }

    void BuildInventory()
    {
        _inv = Ui.Rect("Inventory", _root, Ui.TL, Ui.TL, Vector2.zero, new Vector2(520f, 0f));
        _inv.anchorMin = new Vector2(0f, 0f);
        _inv.anchorMax = new Vector2(0f, 1f);
        _inv.pivot = new Vector2(0f, 0.5f);
        var box = Ui.MakeBox(_inv, Ui.SIDE_BG, Ui.LINE2, 0f, true);
        box.br = 1f;
        var e1 = Ui.Eyebrow(_inv, "Cargo hold", Ui.MUTED);
        Ui.At(e1.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -22f), new Vector2(300f, 14f));
        var h2 = Ui.Label(_inv, "INVENTORY", "display_bold", 24, Ui.TEXT);
        Ui.At(h2.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -38f), new Vector2(300f, 30f));
        var he = Ui.Eyebrow(_inv, "Hold", Ui.MUTED);
        he.alignment = TextAnchor.UpperRight;
        Ui.At(he.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -22f), new Vector2(200f, 14f));
        _invCap = Ui.Label(_inv, "0 / 4 slots", "mono_semi", 20, Ui.AMBER2, TextAnchor.UpperRight);
        Ui.At(_invCap.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -38f), new Vector2(240f, 26f));
        var ce = Ui.Eyebrow(_inv, "Credits", Ui.MUTED);
        Ui.At(ce.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -92f), new Vector2(300f, 14f));
        _invCredits = Ui.Glow(Ui.Label(_inv, "0", "mono_semi", 34, Ui.AMBER2), Ui.AMBER_GLOW, 1.5f);
        Ui.At(_invCredits.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -108f), new Vector2(300f, 40f));
        var brt = Ui.Rect("Bar", _inv, Ui.TL, Ui.TL, new Vector2(26f, -164f), new Vector2(468f, 8f));
        brt.anchorMax = new Vector2(1f, 1f);
        brt.offsetMin = new Vector2(26f, -172f);
        brt.offsetMax = new Vector2(-26f, -164f);
        _invBar = brt.gameObject.AddComponent<Ui.SegBar>();
        _invBar.segmented = false;
        _invBar.track = new Color(0.078f, 0.098f, 0.212f);
        _invBar.fill = Ui.CARGO;
        _invBar.raycastTarget = false;
        // footer
        var foot = Ui.Rect("Foot", _inv, Ui.BL, Ui.BL, Vector2.zero, new Vector2(0f, 96f));
        foot.anchorMax = new Vector2(1f, 0f);
        var cb = Ui.Button(foot, "Close", () => ToggleInventory());
        Ui.At(cb.rt, Ui.TR, Ui.TR, new Vector2(-26f - 34f, -26f), cb.rt.sizeDelta);
        Ui.Chip(foot, "Tab", false, new Vector2(-26f, -26f - (36f - 19f) * 0.5f), Ui.TR);
        var hint = Ui.Label(foot, "Each slot holds one stack of up to " + Data.STACK + " units of one ore, sorted most valuable first. Drag one stack onto another to pour them together; drag one out of the panel and let go (or press ✕) to drop it into space. Dock with the cargo ship to stow stacks in its storage; sell at the Hub.", "body", 12, Ui.DIM, TextAnchor.UpperLeft, true);
        hint.rectTransform.anchorMin = new Vector2(0f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.rectTransform.offsetMin = new Vector2(26f, 6f);
        hint.rectTransform.offsetMax = new Vector2(-26f - 34f - cb.Width - 14f, -12f);
        _invScroll = Ui.Scroll.Make(_inv, 0f, 96f, 0f, 186f);
        _inv.gameObject.SetActive(false);
    }

    public void ToggleInventory()
    {
        _inv.gameObject.SetActive(!_inv.gameObject.activeSelf);
        if (_inv.gameObject.activeSelf) { _invSig = ""; RefreshInventory(); }
    }

    void TotalRow(Ui.Flow f, string label, string value, bool topLine = true, Color? color = null)
    {
        if (topLine) { f.Rule(Ui.LINE2); f.Gap(10f); }
        var r = f.Box(20f);
        Cell(r, label, 0f, 400f, false, Ui.TEXT, "mono", 15);
        Cell(r, value, f.w - 300f, 300f, true, color ?? Ui.AMBER2, "mono_semi", 15);
        f.Gap(10f);
    }

    void SlotGrid(Ui.Flow f, List<State.Stack> stacks, int n, bool store, bool docked, List<Slot> into)
    {
        float colW = (f.w - 20f) / 3f;
        int rows = Mathf.CeilToInt(n / 3f);
        for (int i = 0; i < n; i++)
        {
            int r = i / 3, c = i % 3;
            var s = MakeSlot(f.parent, new Vector2(f.x + c * (colW + 10f), f.y - r * 102f), colW, i < stacks.Count ? stacks[i] : null, store, docked);
            into.Add(s);
        }
        f.y -= rows * 102f - 10f;
    }

    void RefreshInventory()
    {
        if (_inv == null || !_inv.gameObject.activeSelf || _dragging != null) return;
        bool docked = ship != null && ship.docked;
        var holdSt = State.Stacks(State.cargo);
        var storeSt = docked ? State.Stacks(State.store) : new List<State.Stack>();
        var sb = new System.Text.StringBuilder();
        sb.Append(State.CargoSlots()).Append('|').Append(zone.id).Append('|').Append(Mathf.RoundToInt(State.credits)).Append('|').Append(docked);
        foreach (var s in holdSt) sb.Append(s.k).Append(s.u.ToString("0"));
        sb.Append('|');
        foreach (var s in storeSt) sb.Append(s.k).Append(s.u.ToString("0"));
        string sig = sb.ToString();
        if (sig == _invSig) return;
        _invSig = sig;
        int ns = State.CargoSlots();
        int us = State.UsedSlots();
        _invCap.text = us + " / " + ns + " slots";
        _invCredits.text = Data.Fmt(State.credits);
        _invBar.Set((float)us / ns, us >= ns ? Ui.AMBER : Ui.CARGO);
        _invScroll.Clear();
        holdSlots.Clear();
        storeSlots.Clear();
        var f = new Ui.Flow(_invScroll.content, 26f, 0f, _invScroll.Width - 52f);
        SlotGrid(f, holdSt, ns, false, docked, holdSlots);
        f.Gap(10f);
        bool canDeposit = docked && State.CargoTotal() > 0.5f;
        var row = f.Box(36f);
        var dep = Ui.Button(row, "Deposit all", () => { if (ship != null) ship.DepositAll(); _invSig = ""; RefreshInventory(); }, canDeposit);
        dep.interactable = canDeposit;
        Ui.Chip(row, "E", false, new Vector2(dep.Width + 10f, -(36f - 19f) * 0.5f));
        f.Gap(12f);
        if (us > 0) TotalRow(f, "Hold value at Hub prices", Data.Fmt(State.ValueOf(State.cargo)) + " cr");
        else f.Para("The hold is empty. Break a rock and fly through the ore it drops.", "body", 14, Ui.DIM, 10f, TextAnchor.UpperCenter);
        int su = State.StoreUsed();
        if (docked)
        {
            f.Gap(8f);
            var xh = f.Box(20f);
            var xe = Ui.Eyebrow(xh, "Cargo ship storage", Ui.MUTED);
            Ui.At(xe.rectTransform, Ui.TL, Ui.TL, new Vector2(0f, -3f), new Vector2(200f, 14f));
            Cell(xh, su + " / " + Data.STORE_SLOTS + " slots", 150f, 150f, false, Ui.DIM, "mono", 13);
            var tl = Ui.Link(xh, "▲ Take all", () => { if (ship != null) ship.TakeAll(); _invSig = ""; RefreshInventory(); }, 13);
            Ui.At(tl.rectTransform, Ui.TR, Ui.TR, Vector2.zero, tl.rectTransform.sizeDelta);
            f.Gap(10f);
            SlotGrid(f, storeSt, Data.STORE_SLOTS, true, true, storeSlots);
            f.Gap(10f);
            if (su > 0) TotalRow(f, "Storage value at Hub prices", Data.Fmt(State.ValueOf(State.store)) + " cr");
            if (su > 0 && us > 0) TotalRow(f, "Everything aboard", Data.Fmt(State.ValueOf(State.store) + State.ValueOf(State.cargo)) + " cr", false);
            f.Para("Drag stacks between the two grids, or double-click one, to move it. The storage rides with the cargo ship and sells at the Hub.", "body", 12, Ui.DIM, 10f);
        }
        else TotalRow(f, "Cargo ship storage", su + " / " + Data.STORE_SLOTS + " slots · dock to transfer", false, Ui.TEXT);
        _invScroll.SetHeight(f.Used + 10f);
    }

    /// The smoke run's stand-in for a drag: the first stack of one grid dropped on the other grid.
    public bool SmokeDrop(bool fromStore, float maxUnits = 1e9f)
    {
        var src = fromStore ? storeSlots : holdSlots;
        var dst = fromStore ? holdSlots : storeSlots;
        Slot s = null;
        foreach (var c in src) if (!c.IsEmpty) { s = c; break; }
        if (s == null || dst.Count == 0) return false;
        DropStack(s.k, Mathf.Min(s.u, maxUnits), fromStore, !fromStore);
        return true;
    }

    public bool SmokeJettisonFirst()
    {
        foreach (var c in holdSlots) if (!c.IsEmpty) { Jettison(c.k, c.u); return true; }
        return false;
    }

    // ---- the nav computer (#map): the chart of charted zones and the picked zone's details, with the warp button
    void BuildMap()
    {
        _map = Ui.Stretch("Map", _root);
        Ui.Fill(_map, Ui.OVERLAY, true);
        _mapCard = Ui.Rect("MapCard", _map, Ui.MID, Ui.MID, Vector2.zero, new Vector2(1060f, 620f));
        var pane = _mapCard.gameObject.AddComponent<Ui.Pane>();
        pane.Card();
        pane.raycastTarget = true;
        var e = Ui.Eyebrow(_mapCard, "Nav computer", Ui.MUTED);
        Ui.At(e.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -20f), new Vector2(300f, 14f));
        var h2 = Ui.Label(_mapCard, "CHARTED ZONES", "display_bold", 24, Ui.TEXT);
        Ui.At(h2.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -36f), new Vector2(400f, 30f));
        var fe = Ui.Eyebrow(_mapCard, "Cargo ship fuel supply", Ui.MUTED);
        fe.alignment = TextAnchor.UpperRight;
        Ui.At(fe.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -20f), new Vector2(300f, 14f));
        _mapFuel = Ui.Label(_mapCard, "", "mono_semi", 24, Ui.AMBER2, TextAnchor.UpperRight);
        Ui.At(_mapFuel.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -36f), new Vector2(300f, 30f));
        var rule = Ui.Rect("Rule", _mapCard, Ui.TL, Ui.TL, new Vector2(0f, -84f), new Vector2(0f, 1f));
        rule.anchorMax = new Vector2(1f, 1f);
        Ui.Fill(rule, Ui.LINE);
        // body: the chart on the left (3/5), the zone details on the right (2/5)
        _chartRt = Ui.Rect("Chart", _mapCard, Ui.TL, Ui.TL, Vector2.zero, Vector2.zero);
        _chartRt.anchorMin = new Vector2(0f, 0f);
        _chartRt.anchorMax = new Vector2(0.6f, 1f);
        _chartRt.offsetMin = new Vector2(22f, 64f + 18f);
        _chartRt.offsetMax = new Vector2(-22f, -103f);
        _chart = _chartRt.gameObject.AddComponent<Ui.Chart>();
        _chart.raycastTarget = true;
        _chart.picked = z => { _mapSel = z; RefreshMap(); };
        var vr = Ui.Rect("VRule", _mapCard, Ui.TL, Ui.TL, Vector2.zero, new Vector2(1f, 0f));
        vr.anchorMin = new Vector2(0.6f, 0f);
        vr.anchorMax = new Vector2(0.6f, 1f);
        vr.offsetMin = new Vector2(0f, 65f);
        vr.offsetMax = new Vector2(1f, -85f);
        Ui.Fill(vr, Ui.LINE);
        var zr = Ui.Rect("ZInfo", _mapCard, Ui.TL, Ui.TL, Vector2.zero, Vector2.zero);
        zr.anchorMin = new Vector2(0.6f, 0f);
        zr.anchorMax = new Vector2(1f, 1f);
        zr.offsetMin = new Vector2(0f, 65f);
        zr.offsetMax = new Vector2(0f, -85f);
        _zinfo = Ui.Scroll.Make(zr, 0f, 0f, 0f, 0f);
        // footer
        var foot = Ui.Rect("Foot", _mapCard, Ui.BL, Ui.BL, Vector2.zero, new Vector2(0f, 64f));
        foot.anchorMax = new Vector2(1f, 0f);
        var frule = Ui.Rect("Rule", foot, Ui.TL, Ui.TL, Vector2.zero, new Vector2(0f, 1f));
        frule.anchorMax = new Vector2(1f, 1f);
        Ui.Fill(frule, Ui.LINE);
        var cb = Ui.Button(foot, "Close", () => CloseMap());
        Ui.At(cb.rt, Ui.TR, Ui.TR, new Vector2(-26f - 34f, -14f), cb.rt.sizeDelta);
        Ui.Chip(foot, "N", false, new Vector2(-26f, -14f - (36f - 19f) * 0.5f), Ui.TR);
        var ft = Ui.Label(foot, "Nothing sells in the belt: haul it to the Hub, where exclusive ore fetches 50% more. Your cargo ship, storage and all, warps with you.", "body", 12, Ui.DIM, TextAnchor.MiddleLeft, true);
        ft.rectTransform.anchorMin = new Vector2(0f, 0f);
        ft.rectTransform.anchorMax = new Vector2(1f, 1f);
        ft.rectTransform.offsetMin = new Vector2(26f, 8f);
        ft.rectTransform.offsetMax = new Vector2(-26f - 34f - cb.Width - 14f, -8f);
        _map.gameObject.SetActive(false);
    }

    public void ToggleMap() { if (MapOpen) CloseMap(); else OpenMap(); }

    public void OpenMap()
    {
        if (ship != null && ship.warp != null) return;
        _map.gameObject.SetActive(true);
        if (_mapSel == null || _mapSel.id == zone.id) _mapSel = null;
        RefreshMap();
    }

    public void CloseMap() { _map.gameObject.SetActive(false); }

    void KvRow(Ui.Flow f, string key, string val)
    {
        var r = f.Box(18f);
        Cell(r, key, 0f, 80f, false, Ui.MUTED, "body", 12);
        var v = Ui.Label(r, val, "mono_med", 12, Ui.TEXT, TextAnchor.UpperLeft, true);
        Ui.At(v.rectTransform, Ui.TL, Ui.TL, new Vector2(92f, -1f), new Vector2(f.w - 92f, 0f));
        float h = Mathf.Max(18f, Mathf.Ceil(v.preferredHeight));
        v.rectTransform.sizeDelta = new Vector2(f.w - 92f, h);
        f.y -= h - 18f + 4f;
    }

    void OreRow(Ui.Flow f, Color dot, string name, string rarity, string price)
    {
        f.Rule();
        f.Gap(6f);
        var r = f.Box(34f);
        Dot(r, dot, new Vector2(4f, -9f));
        Cell(r, name, 16f, 240f, false, Ui.TEXT, "body", 13).alignment = TextAnchor.UpperLeft;
        var ra = Ui.Label(r, rarity.ToUpperInvariant(), "display", 10, Ui.DIM);
        Ui.At(ra.rectTransform, Ui.TL, Ui.TL, new Vector2(16f, -19f), new Vector2(300f, 12f));
        Cell(r, price, f.w - 160f, 160f, true, Ui.MUTED, "mono", 12).alignment = TextAnchor.UpperRight;
        f.Gap(6f);
    }

    void RefreshMap()
    {
        float w = Mathf.Min(1060f, _canvasSize.x - 48f), h = Mathf.Min(620f, _canvasSize.y - 48f);
        _mapCard.sizeDelta = new Vector2(w, h);
        _mapFuel.text = Mathf.FloorToInt(State.shipFuel) + " / " + Mathf.RoundToInt(Data.CARGO_FUEL_CAP);
        var cur = zone;
        var sel = _mapSel ?? cur;
        _chart.cur = cur;
        _chart.sel = sel;
        _chart.Rebuild();
        _zinfo.Clear();
        bool isCur = sel.id == cur.id;
        var f = new Ui.Flow(_zinfo.content, 24f, 20f, _zinfo.Width - 48f);
        f.Para((isCur ? "Current zone" : (sel.hub ? "Colony zone" : "Charted belt")).ToUpperInvariant(), "display", 11, sel.accent, 2f);
        f.Para(sel.name.ToUpperInvariant(), "display", 20, Ui.TEXT, 14f);
        f.Para(sel.tag, "body", 13, Ui.MUTED, 14f);
        string dist = isCur ? "here" : Data.ZoneLy(cur, sel) + " ly";
        if (sel.hub)
        {
            KvRow(f, "Distance", dist);
            KvRow(f, "Colony", sel.colony);
            KvRow(f, "Market", "buys every ore · exotics +" + Mathf.RoundToInt((Data.EXPORT_BONUS - 1f) * 100f) + "%");
            KvRow(f, "Fuel", "cargo ship resupplies here · " + Data.CARGO_FUEL_PRICE + " cr/u");
            f.Gap(10f);
            int aboard = Mathf.FloorToInt(State.CargoTotal() + State.StoreTotal());
            H3(f, "Aboard to sell", 4f);
            f.Para(aboard > 0 ? aboard + " across the hold and the cargo ship's storage" : "nothing yet · the storage rides with the cargo ship", "body", 12, Ui.MUTED, 14f);
            H3(f, "Holding station", 4f);
            f.Para("On arrival the cargo ship flies in and takes up station off Meridian Colony, and the market and services open from there. Set a course here to leave.", "body", 12, Ui.MUTED, 14f);
        }
        else
        {
            int nfields = 0;
            foreach (int n in Belt.FIELDS_PER_BELT) nfields += n;
            KvRow(f, "Distance", dist);
            KvRow(f, "Market", "none · sell at the Hub");
            KvRow(f, "Belts", (sel.amountMult > 1.2f ? "rich seams" : (sel.density < 1f ? "sparse" : "typical")) + " · " + nfields + " fields");
            KvRow(f, "Planet", sel.planetName);
            KvRow(f, "Raiders", sel.danger > 0f ? "holds by the rich pockets · cargo ship guns cover " + Data.Fm(Raiders.SAFE_R) + " m" : "none");
            f.Gap(10f);
            H3(f, "Asteroid fields", 4f);
            f.Para(nfields + " ore fields and rich pockets across the base belts, and the ring belt above the planet.", "body", 12, Ui.MUTED, 14f);
            H3(f, "Exclusive ore", 4f);
            foreach (var o in Data.ORES)
            {
                if (o.zone != sel.id) continue;
                bool locked = o.unlock > State.up["laser"] + 1;
                OreRow(f, o.color, o.name, o.rarity + (locked ? " · needs laser Lv" + o.unlock : ""), Mathf.RoundToInt(o.price * Data.EXPORT_BONUS) + " cr at the Hub");
            }
            f.Gap(10f);
            H3(f, "Planet", 4f);
            OreRow(f, sel.tint, sel.planetName, "central world · not mineable", "");
        }
        bool can = !isCur && ship != null && ship.docked && ship.warp == null;
        var row = f.Box(36f);
        var wb = Ui.Button(row, isCur ? "You are here" : (can ? "Warp · " + Data.ZoneLy(cur, sel) + " ly" : "Dock with the cargo ship to warp"), () => { if (can) { CloseMap(); ship.StartWarp(sel); } }, can);
        wb.interactable = can;
        _ringTargets["navmap"] = wb.rt;
        f.Gap(20f);
        _zinfo.SetHeight(f.Used);
        _zinfo.Top();
    }

    // ---- the tutorial card (top-left) and its rings, fed by Tutorial
    void BuildTutorial()
    {
        var rr = Ui.Stretch("Rings", _root);
        rr.pivot = Vector2.zero;
        _rings = rr.gameObject.AddComponent<Ui.Rings>();
        _rings.raycastTarget = false;
        _tutBox = Ui.Rect("Tutorial", _root, Ui.TL, Ui.TL, new Vector2(18f, -18f), new Vector2(420f, 180f));
        var box = Ui.MakeBox(_tutBox, Ui.CARD_BG, Ui.AMBER, 1f, true);
        box.leftEdge = Ui.AMBER;
        box.edgeW = 4f;
        _tutStep = Ui.Eyebrow(_tutBox, "Vega", Ui.MUTED);   // the ship's onboard assistant
        Ui.At(_tutStep.rectTransform, Ui.TL, Ui.TL, new Vector2(16f, -12f), new Vector2(240f, 14f));
        var skip = Ui.Link(_tutBox, "Skip tutorial", () => { if (tutorial != null) tutorial.Skip(); }, 11);
        Ui.At(skip.rectTransform, Ui.TR, Ui.TR, new Vector2(-12f, -8f), skip.rectTransform.sizeDelta);
        var replay = Ui.Link(_tutBox, "Replay", () => { if (tutorial != null) tutorial.Speak(); }, 11);
        Ui.At(replay.rectTransform, Ui.TR, Ui.TR, new Vector2(-12f - skip.rectTransform.sizeDelta.x - 6f, -8f), replay.rectTransform.sizeDelta);
        _tutTitle = Ui.Label(_tutBox, "", "display", 17, Ui.TEXT);
        Ui.At(_tutTitle.rectTransform, Ui.TL, Ui.TL, new Vector2(16f, -30f), new Vector2(380f, 22f));
        _tutText = Ui.Label(_tutBox, "", "body", 13, Ui.TEXT, TextAnchor.UpperLeft, true);
        _tutText.lineSpacing = 1.25f;
        Ui.At(_tutText.rectTransform, Ui.TL, Ui.TL, new Vector2(16f, -56f), new Vector2(388f, 60f));
        _tutNext = Ui.Button(_tutBox, "Next", () => { if (tutorial != null) tutorial.Advance(); }, true, false, 0f, 13, 14f, 7f);
        _tutNextChip = Ui.Chip(_tutBox, "Enter", false, Vector2.zero);
        _tutWait = Ui.Label(_tutBox, "", "mono", 11, Ui.AMBER2, TextAnchor.MiddleLeft);
        _tutBox.gameObject.SetActive(false);
        _ringTargets["status"] = _status;
        _ringTargets["readout"] = _readouts;
        _ringTargets["target"] = _target;
        _ringTargets["marker"] = _marker.rt;
        _ringTargets["controls"] = _controls;
        _ringTargets["deposit"] = null;
        _ringTargets["depart"] = _departBtn.rt;
    }

    /// Show a step (null hides the card).
    public void ShowTutorial(Tutorial.Step s, int number)
    {
        _ringNames.Clear();
        if (s == null)
        {
            _tutBox.gameObject.SetActive(false);
            _rings.rects.Clear();
            _rings.SetVerticesDirty();
            return;
        }
        _tutBox.gameObject.SetActive(!_tutHidden);
        _tutStep.text = "FLIGHT OPS · " + number + " / " + Tutorial.STEPS.Length;
        _tutTitle.text = s.title;
        _tutText.text = s.text;
        float th = Mathf.Ceil(_tutText.preferredHeight);
        _tutText.rectTransform.sizeDelta = new Vector2(388f, th);
        float y = -56f - th - 10f;
        _tutNext.rt.gameObject.SetActive(!s.Auto);
        _tutNextChip.gameObject.SetActive(!s.Auto);
        _tutNext.SetText(s.final ? "Finish" : "Next");
        _tutNext.rt.anchoredPosition = new Vector2(16f, y);
        _tutNextChip.anchoredPosition = new Vector2(16f + _tutNext.Width + 8f, y - (_tutNext.Height - 19f) * 0.5f);
        _tutWait.text = s.Auto ? "waiting · " + s.wait : "";
        float wx = s.Auto ? 16f : 16f + _tutNext.Width + 8f + _tutNextChip.sizeDelta.x + 12f;
        Ui.At(_tutWait.rectTransform, Ui.TL, Ui.TL, new Vector2(wx, y), new Vector2(300f, s.Auto ? 20f : _tutNext.Height));
        float foot = s.Auto ? 20f : _tutNext.Height;
        _tutBox.sizeDelta = new Vector2(420f, -y + foot + 14f);
        if (!string.IsNullOrEmpty(s.ring)) _ringNames.Add(s.ring);
        if (!string.IsNullOrEmpty(s.ring2)) _ringNames.Add(s.ring2);
    }

    public void TutorialHidden(bool h)
    {
        _tutHidden = h;
        if (tutorial != null && tutorial.Active) _tutBox.gameObject.SetActive(!h);
    }

    public void ToggleControls()
    {
        _controlsShown = !_controlsShown;
        State.controlsShown = _controlsShown;
        State.Save();
    }

    // ---- the menu
    public void ShowMenu(bool visible, bool paused)
    {
        if (!visible) { menu.Close(); return; }
        menu.Open(paused, State.hasSave);
    }

    // ---- screen geometry: world points to canvas points, and the edge of the screen for what is off it
    readonly Vector3[] _corners = new Vector3[4];

    bool Project(Vector3 world, out Vector2 sp)
    {
        var s = _cam.WorldToScreenPoint(world);
        bool behind = s.z < 0f;
        sp = new Vector2(s.x, s.y) / _canvas.scaleFactor;
        if (behind) sp = _canvasSize - sp;
        return behind;
    }

    bool OnScreen(Vector2 sp) { return sp.x > 0f && sp.x < _canvasSize.x && sp.y > 0f && sp.y < _canvasSize.y; }

    Vector2 Edge(Vector2 sp, out float ang)
    {
        var c = _canvasSize * 0.5f;
        var d = sp - c;
        if (d.magnitude < 1f) d = new Vector2(1f, 0f);
        ang = Mathf.Atan2(d.y, d.x);
        const float mg = 46f;
        float sc = Mathf.Min(d.x != 0f ? Mathf.Abs((c.x - mg) / d.x) : float.PositiveInfinity, d.y != 0f ? Mathf.Abs((c.y - mg) / d.y) : float.PositiveInfinity);
        return c + d * sc;
    }

    /// placeMarker: a marker on a scene point, or pinned to the screen edge with an arrow pointing the way when it is off
    /// screen (mirrored when it is behind the camera).
    void PlaceMarker(Ui.Marker m, Vector3 worldPos, string text)
    {
        Vector2 sp;
        bool behind = Project(worldPos, out sp);
        bool on = !behind && OnScreen(sp);
        float ang = 0f;
        if (!on) sp = Edge(sp, out ang);
        m.Place(sp, !on, ang, text);
    }

    Rect CanvasRect(RectTransform rt)
    {
        rt.GetWorldCorners(_corners);
        float s = _canvas.scaleFactor;
        return Rect.MinMaxRect(_corners[0].x / s, _corners[0].y / s, _corners[2].x / s, _corners[2].y / s);
    }

    static readonly Dictionary<string, string> DRONE_WORDS = new Dictionary<string, string> { { "idle", "standing by" }, { "out", "collecting" }, { "return", "returning" }, { "enter", "entering hangar" }, { "unload", "unloading" }, { "exit", "leaving hangar" } };

    public void DamageFlash() { _dmgT = 0.6f; }
    public void WreckFlash() { _dmgT = 1.4f; }

    // ---- every frame
    public void UpdateHud(float dt, Ship ship, Belt belt, CargoShip carrier, Data.Zone zone, bool isStarted)
    {
        this.zone = zone;
        started = isStarted;
        _canvasSize = _root.rect.size;
        TickToasts(dt);
        if (_resetArmed) { _resetT -= dt; if (_resetT <= 0f) { _resetArmed = false; _resetLink.text = "Reset save"; } }
        bool docked = ship.docked;
        bool inCut = ship.InCinematic;
        bool hold = ship.hold;
        // the ship: hull, fuel, speed, thrust, cargo
        float hp = State.Stat("hull").hp;
        float hf = State.hull / hp;
        _gHull.Show(hf, Mathf.CeilToInt(State.hull) + " / " + Mathf.RoundToInt(hp), hf < 0.25f ? Ui.RED : (hf < 0.5f ? Ui.AMBER : Ui.GREEN), hf < 0.25f);
        float tank = State.Stat("tank").cap;
        float ff = State.fuel / tank;
        _gFuel.Show(ff, Mathf.FloorToInt(State.fuel) + " / " + Mathf.RoundToInt(tank), ff < 0.2f ? Ui.RED : Ui.CYAN);
        float sf = State.shield / Data.SHIELD_MAX;
        bool charging = State.sinceHit >= Data.SHIELD_WAIT && State.shield < Data.SHIELD_MAX;
        // the wait: a countdown to the recharge while the shield is down and the last hit is under ten seconds old
        string tail = charging ? " ↑" : State.shield < Data.SHIELD_MAX ? " · " + (Data.SHIELD_WAIT - State.sinceHit).ToString("0.0") + " s" : "";
        _gShield.Show(sf, Mathf.CeilToInt(State.shield) + " / " + Mathf.RoundToInt(Data.SHIELD_MAX) + tail, sf < 0.25f ? Ui.AMBER : Data.Hex("#8fe8ff"));
        float tv = ship.braking ? 1f : ship.throttle;
        _gThr.Show(tv, ship.drifting ? "DRIFT" : ship.braking ? "RETRO" : Mathf.RoundToInt(ship.throttle * 100f) + "%", ship.braking ? Ui.CYAN : (ship.afterburning ? Ui.AMBER2 : Ui.AMBER));
        int us = State.UsedSlots();
        int ns = State.CargoSlots();
        bool full = us >= ns && State.CargoTotal() >= State.CargoCapacity() - 0.5f;
        _gCargo.Show(State.CargoTotal() / State.CargoCapacity(), us + " / " + ns + " slots", full ? Ui.AMBER : Ui.CARGO);
        _gCargo.value.color = full ? Ui.AMBER2 : Ui.GLOW_TEXT;
        float spd = ship.Speed * Data.METRE;
        _speedBig.text = docked ? (hold ? "HOLD" : "DOCK") : Data.Fmt(spd);
        // the situation
        float toCarrier = carrier != null ? (ship.TruePos - carrier.truePos).magnitude : 0f;
        string spdT = docked ? (hold ? "HOLDING" : "DOCKED") : Mathf.RoundToInt(spd).ToString();
        var here = docked ? null : belt.FieldAt(ship.TruePos);
        _row1.text = Kv("ZONE", zone.name) + "   " + Kv("SPD", spdT) + "   " + Kv("CARGO SHIP", Data.Fm(toCarrier)) + "   " + Kv("FIELD", here != null ? here.name : "—");
        string laser = ship.laserOn ? "CUTTING" : (ship.firing ? "FIRING" : "ready");
        if (ship.overcharge) laser += " ⚡×" + State.Stat("overcharge").mult;
        string radar = ship.radarCd <= 0f ? "READY" : ship.radarCd.ToString("0.0") + "s";
        float reach = State.Stat("range").reach;
        bool locked = ship.lockKind != "" && !docked;
        string rangeTxt = locked ? Data.Fm(ship.lockDist) + " / " + Data.Fm(reach) + " m" : Data.Fm(reach) + " m";   // the lock's distance against the beam's reach
        int threat = game != null && game.raiders != null ? game.raiders.threat : 0;
        string threatTxt = threat > 0 ? Ui.Col(threat + " raider" + (threat > 1 ? "s" : ""), Ui.RED) : Ui.Col("none", Ui.GLOW_TEXT);
        string weaponTxt = ship.weapon == "gun" ? "Autocannon" : "Laser";
        _row2.text = Kv("WEAPON", weaponTxt) + "   " + Kv("LASER", laser) + "   " + Kv("RANGE", rangeTxt) + "   " + Kv("RADAR", radar) + "   THREAT " + threatTxt;
        float rw = Mathf.Max(Ui.Measure(_row1), Ui.Measure(_row2)) + 28f;
        if (Mathf.Abs(_readouts.sizeDelta.x - rw) > 0.5f) _readouts.sizeDelta = new Vector2(rw, 58f);
        // the target: the panel follows the lock when there is one, else the crosshair target
        bool hasTarget = ship.target >= 0 && ship.target < belt.count && belt.alive[ship.target] && !docked;
        int panelRock = locked && ship.lockKind == "rock" ? ship.lockRock : (hasTarget ? ship.target : -1);
        bool showTarget = false;
        var panelRaider = locked && ship.lockKind == "raider" ? ship.lockRaider : ship.raiderTarget;
        if (panelRaider != null && !panelRaider.dead && !docked)
        {
            showTarget = true;
            float rd = Mathf.Max(0f, (panelRaider.pos - ship.LaserOrigin()).magnitude - Raiders.RADIUS);
            _tEyebrow.text = locked && ship.lockKind == "raider" ? "LOCKED TARGET" : "TARGET";
            _tName.text = "Pirate raider";
            _tRows.text = Kv("SIZE", "Ship") + "   " + Kv("RANGE", Data.Fm(rd) + " m") + "   " + Kv("SHIELD", Mathf.CeilToInt(Mathf.Max(0f, panelRaider.shield)) + " / " + Mathf.RoundToInt(panelRaider.maxShield));
            _tHpRow.gameObject.SetActive(true);
            _tHp.Set(panelRaider.hp / Mathf.Max(1f, panelRaider.maxHp), Ui.RED);
            _tHpT.text = Mathf.CeilToInt(Mathf.Max(0f, panelRaider.hp)) + " / " + Mathf.RoundToInt(panelRaider.maxHp);
            bool hostile = panelRaider.state == "attack";
            _tWarn.text = hostile ? (State.Stat("gun").reach > 0f ? "Hostile · autocannon on it" : "Hostile · no autocannon fitted") : "";
            _tWarn.gameObject.SetActive(hostile);
            _target.sizeDelta = new Vector2(Mathf.Max(230f, Ui.Measure(_tRows) + 28f), hostile ? 108f : 92f);
        }
        else if (locked && ship.lockKind == "station")
        {
            showTarget = true;
            _tEyebrow.text = "LOCKED TARGET";
            _tName.text = "Cargo ship";
            _tRows.text = Kv("SIZE", "Carrier") + "   " + Kv("RANGE", Data.Fm(ship.lockDist) + " m");
            _tHpRow.gameObject.SetActive(false);
            _tWarn.gameObject.SetActive(false);
            _target.sizeDelta = new Vector2(Mathf.Max(230f, Ui.Measure(_tRows) + 28f), 74f);
        }
        else if (panelRock >= 0 && panelRock < belt.count && belt.alive[panelRock])
        {
            showTarget = true;
            int i = panelRock;
            float tdist = Mathf.Max(0f, (belt.RockPos(i) - ship.LaserOrigin()).magnitude - belt.radius[i]);
            _tEyebrow.text = locked ? "LOCKED TARGET" : "TARGET";
            _tName.text = (belt.ore[i] < 0 ? "Barren" : Data.ORES[belt.ore[i]].name) + " Rock";
            _tRows.text = Kv("SIZE", Belt.CLS_NAME[belt.cls[i]]) + "   " + Kv("RANGE", Data.Fm(tdist) + " m" + (tdist <= reach ? "" : " · beyond reach"));
            _tHpRow.gameObject.SetActive(true);
            _tHp.Set(belt.hp[i] / Mathf.Max(1f, belt.hpMax[i]), Ui.AMBER2);
            _tHpT.text = Mathf.CeilToInt(Mathf.Max(0f, belt.hp[i])) + " / " + Mathf.RoundToInt(belt.hpMax[i]);
            string reason = "";
            if (belt.ore[i] >= 0)
            {
                int need = Data.ORES[belt.ore[i]].unlock;
                if (need > State.up["laser"] + 1) reason = "Needs the Lv" + need + " mining laser";
            }
            _tWarn.text = reason;
            _tWarn.gameObject.SetActive(reason != "");
            float tw = Mathf.Max(230f, Ui.Measure(_tRows) + 28f);
            _target.sizeDelta = new Vector2(tw, reason != "" ? 108f : 92f);
        }
        // the hover label beside the cursor: what the mouse is over and how far it is
        var hv = ship.hover;
        if (hv != null && !docked && !inCut && started)
        {
            _hoverLbl.gameObject.SetActive(true);
            string ht = hv.name + " · " + Data.Fm(hv.dist) + " m";
            if (_hoverTxt.text != ht)
            {
                _hoverTxt.text = ht;
                float hw = Mathf.Ceil(_hoverTxt.preferredWidth) + 12f;
                _hoverLbl.sizeDelta = new Vector2(hw, 20f);
                _hoverTxt.rectTransform.sizeDelta = new Vector2(hw, 20f);
            }
            var mp = Input.mousePosition;
            _hoverLbl.anchoredPosition = new Vector2(mp.x, mp.y) / _canvas.scaleFactor + new Vector2(14f, -14f - 20f);
        }
        else _hoverLbl.gameObject.SetActive(false);
        // the hint bar
        var segs = new List<string>();
        if (!docked && (ship.recovery != null || ship.disabled)) segs.Add(ship.RecoveryStatus());
        else if (!docked)
        {
            if (ship.cut != null)
            {
                if (ship.cut.mode == "hold") segs.Add("Colony control has the cargo ship · " + (zone.colony ?? "the colony"));
                else if (ship.cut.mode == "dock") segs.Add("Approach control has the ship · " + CargoShip.BayName(ship.cut.side));
                if (ship.cut.mode != "depart") segs.Add(Kbd("Space") + " Skip");
            }
            else if (carrier != null && toCarrier < Data.DOCK_RANGE && !hold) segs.Add(Kbd("E") + " Auto-dock with the cargo ship · or fly in through either hangar mouth");
            else if (State.fuel <= 0.5f && ship.cut == null) segs.Add(Kbd("T") + " Out of fuel · recovery to the cargo ship (15% of credits)");
            if (ship.cut == null && ship.CanFly)
            {
                if (ship.weapon == "gun") segs.Add(ship.gunFiring ? "Autocannon firing · bolts go to the crosshair" : (ship.lockKind == "raider" ? Kbd("LMB") + " Fire · put the crosshair on the LEAD pip" : Kbd("LMB") + " Fire the autocannon at the crosshair"));
                else if (ship.raiderTarget != null && !hasTarget) segs.Add(Kbd("Wheel") + " Autocannon for the raider");
            }
            if (hasTarget && ship.cut == null && ship.weapon == "laser")
            {
                int i = ship.target;
                if (!ship.firing) segs.Add(Kbd("LMB") + " Hold to mine");
                else if (belt.ore[i] < 0) segs.Add("Breaking rock · scrap only");
                else segs.Add((ship.laserOn ? "Cutting " : "Aiming at ") + Data.ORES[belt.ore[i]].name);
            }
        }
        else if (started && State.CargoTotal() > 0.5f && !hold) segs.Add(Kbd("E") + " Deposit all ore into the cargo ship");
        if (started && !docked && ship.cut == null && ship.CanFly)
        {
            if (hv != null && !ship.HoverIsLock(hv)) segs.Add(Kbd("Q") + " " + (locked ? "Switch lock to " : "Lock on ") + hv.name);
            else if (locked) segs.Add(Kbd("Q") + " Release lock");
        }
        string ptext = string.Join(Ui.Col("  ·  ", Ui.DIM), segs.ToArray());
        if (ptext != _promptText.text)
        {
            _promptText.text = ptext;
            _prompt.sizeDelta = new Vector2(Mathf.Ceil(_promptText.preferredWidth) + 28f, 36f);
        }
        float baseY = 18f + _status.sizeDelta.y + 14f;
        _prompt.anchoredPosition = new Vector2(0f, baseY);
        _notice.anchoredPosition = new Vector2(0f, baseY + (segs.Count > 0 ? _prompt.sizeDelta.y + 10f : 0f));
        _notice.gameObject.SetActive(started && full && !docked && !inCut);
        // what shows when: the hangar hides the situation, the controls and the hint; a cutscene hides nearly everything
        bool showFlight = started && !inCut;
        _status.gameObject.SetActive(showFlight);
        float sa = docked ? 0.85f : 1f;
        if (Mathf.Abs(_statusPane.alpha - sa) > 0.01f) { _statusPane.alpha = sa; _statusPane.SetVerticesDirty(); }
        _readouts.gameObject.SetActive(showFlight && !docked);
        _controls.gameObject.SetActive(showFlight && !docked && _controlsShown);
        _prompt.gameObject.SetActive(showFlight && !docked && segs.Count > 0);
        _target.gameObject.SetActive(showTarget && showFlight);
        if (inCut && _tutBox.gameObject.activeSelf) _tutBox.gameObject.SetActive(false);
        _barTop.gameObject.SetActive(inCut);
        _barBot.gameObject.SetActive(inCut);
        _barTop.rectTransform.sizeDelta = new Vector2(0f, _canvasSize.y * 0.11f);
        _barBot.rectTransform.sizeDelta = new Vector2(0f, _canvasSize.y * 0.11f);
        _caption.gameObject.SetActive(inCut);
        _caption.rectTransform.anchoredPosition = new Vector2(0f, _canvasSize.y * 0.036f + 6f);
        if (inCut)
        {
            if (ship.warp != null) _caption.text = ("JUMP · " + ship.warp.z.name + " · " + Data.ZoneLy(zone, ship.warp.z) + " LY · SPACE SKIPS").ToUpperInvariant();
            else if (ship.cut.mode == "hold") _caption.text = ("ARRIVAL · " + (zone.colony ?? "the colony") + " · SPACE SKIPS").ToUpperInvariant();
            else _caption.text = ("APPROACH · " + CargoShip.BayName(ship.cut.side) + " · CARGO SHIP · SPACE SKIPS").ToUpperInvariant();
            if (_services.gameObject.activeSelf) _services.gameObject.SetActive(false);
        }
        else if (docked && _servicesVisible && !_services.gameObject.activeSelf) _services.gameObject.SetActive(true);
        var fc = _fade.color;
        fc.a = ship.WarpFade();
        _fade.color = fc;
        // the gunnery crosshair: where a bolt goes, at gun range; and the lead pip: where to put it for the locked raider
        if (showFlight && !docked && ship.cut == null && ship.CanFly)
        {
            // the crosshair rides the mouse: that is where the gun points
            var mp = Input.mousePosition;
            var cp = new Vector2(mp.x, mp.y) / _canvas.scaleFactor;
            _crosshairRt.gameObject.SetActive(OnScreen(cp) && ship.weapon == "gun");
            _crosshairRt.anchoredPosition = cp;
            _crosshair.Set(ship.gunFiring);
            // the crosshair stands in for the mouse: the pointer hides while it shows and no panel wants clicks
            Cursor.visible = !(_crosshairRt.gameObject.activeSelf && !InvOpen && !MapOpen && !MenuVisible && !_services.gameObject.activeSelf);
            _crosshair.SetHit(game.raiders != null ? game.raiders.hitFlash : 0f, game.raiders != null && game.raiders.hitKill);
            // a LEAD pip on every raider within gun reach: where to put the crosshair for a bolt fired now to meet it
            int li = 0;
            if (game.raiders != null)
            {
                float gunReach = ship.GunReach;
                foreach (var r in game.raiders.raiders)
                {
                    if (r.dead || (r.pos - ship.LaserOrigin()).magnitude > gunReach) continue;
                    Vector2 lp;
                    bool lBehind = Project(ship.AimPointFor(ship.LeadPoint(r)), out lp);   // where to put the crosshair, not where the lead point is
                    if (lBehind || !OnScreen(lp)) continue;
                    if (li >= _leadPips.Count) _leadPips.Add(Ui.Marker.Make(_root, Ui.AMBER2, true));
                    _leadPips[li++].Place(lp - new Vector2(0f, 38f), false, 0f, "LEAD");
                }
            }
            for (; li < _leadPips.Count; li++) _leadPips[li].Hide();
        }
        else { _crosshairRt.gameObject.SetActive(false); foreach (var p in _leadPips) p.Hide(); Cursor.visible = true; }
        // the reticle on the target
        if (hasTarget && showFlight && ship.cut == null)
        {
            Vector2 sp;
            bool behind = Project(belt.RockPos(ship.target) - game.worldOffset, out sp);
            _reticleRt.gameObject.SetActive(!behind);
            _reticleRt.anchoredPosition = sp;
            _reticle.Set(ship.laserOn, ship.lockKind == "rock" && ship.lockRock == ship.target);   // locked on: heavier, wider corners
        }
        else _reticleRt.gameObject.SetActive(false);   // raiders carry no reticle: the LEAD pip is the gunnery aid
        // raiders on the attack carry a red marker
        var rl = showFlight && !docked && !hold && game.raiders != null ? game.raiders.raiders : null;
        int nr = 0;
        if (rl != null) foreach (var r in rl) if (r.state == "attack") nr++;
        while (_raiderMarkers.Count < nr) _raiderMarkers.Add(Ui.Marker.Make(_root, Ui.RED, true));
        int ri = 0;
        if (rl != null) foreach (var r in rl)
        {
            if (r.state != "attack") continue;
            PlaceMarker(_raiderMarkers[ri++], r.pos - game.worldOffset, "RAIDER · " + Data.Fm((r.pos - ship.TruePos).magnitude));
        }
        for (; ri < _raiderMarkers.Count; ri++) _raiderMarkers[ri].Hide();
        // the cargo ship marker, the nearest charted field's (hidden while inside one), and one on every collector drone
        bool markersOn = carrier != null && showFlight && !docked && !hold;
        if (markersOn) PlaceMarker(_marker, carrier.transform.position, toCarrier >= 4500f ? "CARGO SHIP" : CargoShip.BayName(carrier.NearestSide(ship.TruePos)).ToUpperInvariant());
        else _marker.Hide();
        var inside = markersOn ? belt.FieldAt(ship.TruePos) : null;
        float edge = 0f;
        var nf = markersOn && inside == null ? belt.NearestField(ship.TruePos, out edge) : null;
        if (nf != null) PlaceMarker(_fieldMarker, belt.FieldCentre(nf) - game.worldOffset, (nf.pocket ? "RICH POCKET " : "FIELD ") + nf.name + " " + Data.Fm(edge));
        else _fieldMarker.Hide();
        var drones = markersOn && game.drones != null ? game.drones.drones : null;
        int nd = drones != null ? drones.Count : 0;
        while (_droneMarkers.Count < nd) _droneMarkers.Add(Ui.Marker.Make(_root, Ui.CYAN, true));
        for (int di = 0; di < _droneMarkers.Count; di++)
        {
            if (di >= nd) { _droneMarkers[di].Hide(); continue; }
            var c = drones[di];
            string word;
            if (!DRONE_WORDS.TryGetValue(c.phase, out word)) word = c.phase;
            PlaceMarker(_droneMarkers[di], c.pos - game.worldOffset, "DRONE " + (di + 1) + " · " + word + (c.load > 0.5f ? " · " + Mathf.RoundToInt(c.load) : "") + " · " + Data.Fm((c.pos - ship.TruePos).magnitude));
        }
        // radar blips
        _blips.items.Clear();
        if (showFlight && !docked && belt.count > 0)
        {
            var marked = belt.Marked(State.time, ship.TruePos, 36);
            int bi = 0;
            foreach (var m in marked)
            {
                int i = m.id;
                if (i == ship.target) continue;
                Vector2 sp;
                bool behind = Project(belt.RockPos(i) - game.worldOffset, out sp);
                bool on = !behind && OnScreen(sp);
                float ang = 0f;
                if (!on) sp = Edge(sp, out ang);
                var col = belt.ore[i] >= 0 ? Data.ORES[belt.ore[i]].color : Ui.CYAN;
                string lbl = bi < 4 ? (belt.ore[i] >= 0 ? Data.ORES[belt.ore[i]].name : "Rock") + " " + Data.Fm(m.dist) + " m" : "";
                _blips.items.Add(new Ui.Blip { pos = sp, off = !on, ang = ang, color = col, label = lbl, alpha = Mathf.Clamp01(m.left / 6f) });
                bi++;
            }
        }
        _blips.Apply();
        // the damage flash on a knock
        if (_lastHull >= 0f && State.hull < _lastHull - 0.5f && !docked) DamageFlash();
        _lastHull = State.hull;
        if (_dmgT > 0f)
        {
            _dmgT = Mathf.Max(0f, _dmgT - dt);
            _dmg.Set(0.55f * _dmgT / 0.6f);
        }
        // the shield-down warning: subtle, but it breathes, so the eye catches it
        bool shieldOut = State.shield <= 0f && !docked && ship.cut == null && ship.warp == null && ship.CanFly;
        _shieldOutA = Mathf.MoveTowards(_shieldOutA, shieldOut ? 1f : 0f, dt / (shieldOut ? 0.4f : 0.8f));
        float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / 1.4f);
        _shieldOut.Set(_shieldOutA * (0.09f + 0.09f * breathe));
        // the afterburner's speed streaks: in over a third of a second when it lights, out over half a second after
        bool burning = ship.afterburning && !docked && ship.cut == null && ship.warp == null;
        _speedK = Mathf.Lerp(_speedK, burning ? 1f : 0f, 1f - Mathf.Exp(-(burning ? 3f : 2f) * dt));
        // ... and the post pass: the radial blur and the fringing, centred where the streaks vanish
        if (game.lighting != null && game.lighting.post != null)
        {
            game.lighting.post.burn = _speedK * (0.75f + 0.08f * State.Stat("thrusters").mult);
            game.lighting.post.burnCenter = new Vector2(0.5f + ship.camYaw * 0.125f, 0.5f + ship.camPitch * 0.1f);
        }
        var engS = State.Stat("engine");
        // the streaks react to the turn: the vanishing point leads into it (a quarter of the half-width at full yaw, a
        // fifth of the half-height at full pitch) and the field tips with the bank
        _speed.Set(_speedK, dt, engS.max > 0f ? Mathf.Clamp01(ship.vel.magnitude / (engS.max * 3f)) : 0.5f,
            new Vector2(ship.camYaw * 0.25f, ship.camPitch * 0.2f), -ship.camYaw * 14f - ship.camRoll * 7f);
        // side panels follow the window
        _services.sizeDelta = new Vector2(Mathf.Min(580f, _canvasSize.x * 0.52f), 0f);
        _inv.sizeDelta = new Vector2(Mathf.Min(520f, _canvasSize.x * 0.48f), 0f);
        if (docked && Time.frameCount % 30 == 0 && _services.gameObject.activeSelf) RefreshServices();
        if (InvOpen && Time.frameCount % 15 == 0) RefreshInventory();
        // the tutorial's rings follow their targets
        _rings.rects.Clear();
        if (_tutBox.gameObject.activeSelf)
        {
            _ringTargets["deposit"] = _depositBtn != null ? _depositBtn.rt : null;
            _ringTargets["refits"] = _refitsBox;
            foreach (var n in _ringNames)
            {
                RectTransform c;
                if (!_ringTargets.TryGetValue(n, out c) || c == null || !c.gameObject.activeInHierarchy) continue;
                var r = CanvasRect(c);
                if (r.width > 2f && r.height > 2f) _rings.rects.Add(r);
            }
        }
        _rings.SetVerticesDirty();
    }
}
