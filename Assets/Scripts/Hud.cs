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
    RectTransform _status, _readouts, _target, _controls, _prompt, _notice, _toastBox, _version, _hoverLbl, _pointerDot;
    Text _speedUnit;
    // the WEAPON pane: two rows (the laser, the cannon), the equipped one lit, and the cannon's heat under them
    RectTransform _weaponPane, _wHeatRt;
    Ui.WeaponIcon _wIcon;
    Ui.Box _wBox1, _wBox2;
    Text _wName1, _wName2, _wHeatT;
    Ui.SegBar _wHeat;
    const float BAND_H = 100f;   // the bottom band's panes are this tall
    Text _hoverTxt;
    Ui.Pane _statusPane;
    Ui.Gauge _gHull, _gShield, _gFuel, _gThr, _gCargo;
    Text _speedBig, _row1, _row2, _tName, _tRows, _tHpT, _tWarn, _promptText, _caption;
    Ui.SegBar _tHp;
    RectTransform _tHpRow;
    Image _barTop, _barBot, _fade;
    float _lastHull = -1f, _dmgT;
    bool _controlsShown = true;

    class ToastItem { public RectTransform rt; public Ui.Box box; public Text text; public float age; }
    readonly List<ToastItem> _toasts = new List<ToastItem>();

    // the hangar window: one centred window with tabs (Inventory · Ship refits · Cargo ship refits)
    RectTransform _win, _tabBar;
    static readonly string[] TAB_IDS = { "inv", "ship", "depot" };
    readonly Ui.Btn[] _tabBtns = new Ui.Btn[3];
    Text _tabHint;
    Text _winEyebrow, _winTitle, _winSub, _winCredits;
    Ui.Scroll _winScroll;
    Ui.Btn _departBtn, _navBtn, _depositBtn, _closeBtn;
    Text _resetLink;
    bool _resetArmed;
    float _resetT;
    string _winTab = "inv";   // "inv", "ship", "depot"
    string _svcSig = "";      // the signature of what the window shows; "" forces a rebuild
    string _invSig = "";      // kept for the callers that clear it: both feed the one window
    RectTransform _refitsBox;
    Ui.Gauge _gStoreW, _gFuelW, _gPartsW;   // the window's gauges, updated in place so the window is not rebuilt under the mouse
    // the window's motion: it eases open and shut, a tab's contents slide in; the balance counts to its new value
    CanvasGroup _winGroup, _bodyGroup;
    bool _winWant;
    public bool instantUi;   // the smoke run: the window shows at once, no ease, so its screenshots catch it
    float _winK, _bodyK, _credShown = -1f;
    Vector2 _bodyHome;   // the viewport's resting anchored position: the tab slide is relative to it (zero would shift a stretched rect)
    // the upgrade rows, kept and updated in place (a purchase lights its row instead of rebuilding the tab)
    class UpRow { public string key; public bool depot; public Image[] pips; public Text desc; public Ui.Btn buy, minus; public Ui.Box bg; public float flash; }
    readonly List<UpRow> _rows = new List<UpRow>();
    string _rowSig = "";
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

    public bool InvOpen { get { return _win != null && _win.gameObject.activeSelf && _winWant; } }
    public bool MapOpen { get { return _map != null && _map.gameObject.activeSelf; } }
    public bool ServicesVisible { get { return InvOpen; } }
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
        // the pointer dot: shown wherever the mouse is while the OS pointer is hidden in flight
        _pointerDot = Ui.Rect("PointerDot", _root, Ui.BL, Ui.MID, Vector2.zero, new Vector2(12f, 12f));
        _pointerDot.gameObject.AddComponent<Ui.Dot>().raycastTarget = false;
        _pointerDot.gameObject.SetActive(false);
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
        BuildWeaponPane();
        BuildControls();
        BuildPrompt();
        BuildNotice();
        _toastBox = Ui.Rect("Toasts", _root, Ui.TC, Ui.TC, new Vector2(0f, -22f), new Vector2(900f, 300f));
        BuildCine();
        var vt = Ui.Fixed(_root, "v" + Data.VERSION, "mono", 11, Ui.A(Ui.DIM, 0.75f), Ui.BL, new Vector2(14f, 8f));
        _version = vt.rectTransform;
        BuildWindow();
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

    // ---- the bottom band: four panes in a row, 1,130 wide, along the bottom edge
    //   SHIP   300   hull, shield, fuel and the hold, two by two
    //   FLIGHT 300   speed and thrust, the cargo ship's distance, the field, the radar, the threat
    //   TARGET 300   what the crosshair or the lock is on: name, size, range, health, the warning
    //   WEAPON 240   a picture of the equipped weapon, the two weapons with their keys, the equipped one lit, the heat
    const float BAND_LEFT = -585f;

    RectTransform BandPane(string name, float left, float w, out Ui.Pane pane)
    {
        return Pane(name, Ui.BC, new Vector2(left + w * 0.5f, 18f), new Vector2(w, BAND_H), out pane);
    }

    void BuildStatus()
    {
        _status = BandPane("Ship", BAND_LEFT, 300f, out _statusPane);
        // two by two, with room: hull and shield on the first row, fuel and the hold on the second
        const float gw = 130f, x1 = 14f, x2 = 156f, y1 = -16f, y2 = -56f;
        _gHull = Ui.Gauge.Make(_status, "Hull", Ui.GREEN, x1, y1, gw, false, 7f);
        _gShield = Ui.Gauge.Make(_status, "Shield", Data.Hex("#8fe8ff"), x2, y1, gw, false, 7f);
        _gFuel = Ui.Gauge.Make(_status, "Fuel", Ui.CYAN, x1, y2, gw, false, 7f);
        _gCargo = Ui.Gauge.Make(_status, "Hold", Ui.CARGO, x2, y2, gw, false, 7f);
    }

    // ---- FLIGHT: the big speed with its unit, the thrust gauge beside it, two small rows of the situation under them
    void BuildReadouts()
    {
        Ui.Pane p;
        _readouts = BandPane("Flight", BAND_LEFT + 310f, 300f, out p);
        _speedBig = Ui.Glow(Ui.Label(_readouts, "0", "mono_semi", 26, Ui.GLOW_TEXT, TextAnchor.LowerLeft), Ui.HUD_GLOW, 1.5f);
        Ui.At(_speedBig.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -12f), new Vector2(130f, 28f));
        _speedUnit = Ui.Label(_readouts, "SPEED · m/s", "mono", 10, Ui.HUD_DIM, TextAnchor.UpperLeft);   // under the number, so it sits still whatever the digits
        Ui.At(_speedUnit.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -41f), new Vector2(130f, 12f));
        _gThr = Ui.Gauge.Make(_readouts, "Thrust", Ui.AMBER, 160f, -12f, 126f, false, 8f);
        _row1 = FlightRow(-60f);
        _row2 = FlightRow(-78f);
    }

    Text FlightRow(float y)
    {
        var t = Ui.Glow(Ui.Label(_readouts, "", "mono", 11, Ui.HUD_DIM, TextAnchor.UpperLeft), Ui.A(Ui.HUD_GLOW, 0.35f));
        Ui.At(t.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, y), new Vector2(272f, 14f));
        return t;
    }

    static string Kv(string key, string val) { return key + " " + Ui.Col(val, Ui.GLOW_TEXT); }
    static string Kbd(string k) { return "<b>" + Ui.Col(k, Ui.GLOW_TEXT) + "</b>"; }

    // ---- TARGET: what the crosshair or the lock is on, and the weapon in hand
    void BuildTarget()
    {
        Ui.Pane p;
        _target = BandPane("Target", BAND_LEFT + 620f, 300f, out p);
        _tName = Ui.Glow(Ui.Label(_target, "", "display", 15, Color.white, TextAnchor.UpperLeft), Ui.A(Ui.CYAN, 0.5f));
        Ui.At(_tName.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -14f), new Vector2(272f, 20f));
        _tRows = Ui.Glow(Ui.Label(_target, "", "mono", 11, Ui.HUD_DIM, TextAnchor.UpperLeft), Ui.A(Ui.HUD_GLOW, 0.35f));
        Ui.At(_tRows.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -36f), new Vector2(272f, 14f));
        _tHpRow = Ui.Rect("Hp", _target, Ui.TL, Ui.TL, new Vector2(14f, -54f), new Vector2(272f, 12f));
        var brt = Ui.Rect("Bar", _tHpRow, Ui.TL, Ui.TL, new Vector2(0f, -2f), new Vector2(140f, 8f));
        _tHp = brt.gameObject.AddComponent<Ui.SegBar>();
        _tHp.fill = Ui.AMBER2;
        _tHp.raycastTarget = false;
        _tHpT = Ui.Label(_tHpRow, "", "mono", 11, Ui.MUTED, TextAnchor.MiddleLeft);
        Ui.At(_tHpT.rectTransform, Ui.TL, Ui.TL, new Vector2(148f, 0f), new Vector2(60f, 12f));
        _tWarn = Ui.Label(_target, "", "body", 11, Ui.AMBER, TextAnchor.UpperLeft);
        Ui.At(_tWarn.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -72f), new Vector2(272f, 14f));
        // the weapon line shares the last row with the warning: the warning takes it while there is one
        _target.gameObject.SetActive(false);
    }

    // ---- WEAPON: the two weapons as rows with their keys, the equipped one lit; the cannon's heat below
    void BuildWeaponPane()
    {
        Ui.Pane p;
        _weaponPane = BandPane("Weapon", BAND_LEFT + 930f, 240f, out p);
        // the picture on the left, the rows on the right
        var ic = Ui.Rect("Icon", _weaponPane, Ui.TL, Ui.TL, new Vector2(12f, -8f), new Vector2(64f, 60f));
        _wIcon = ic.gameObject.AddComponent<Ui.WeaponIcon>();
        _wIcon.raycastTarget = false;
        WeaponRow(-12f, "1", "Mining laser", out _wBox1, out _wName1);
        WeaponRow(-42f, "2", "Autocannon", out _wBox2, out _wName2);
        var he = Ui.Eyebrow(_weaponPane, "Heat", Ui.HUD_DIM);
        Ui.At(he.rectTransform, Ui.TL, Ui.TL, new Vector2(14f, -74f), new Vector2(60f, 14f));
        _wHeatT = Ui.Glow(Ui.Label(_weaponPane, "", "mono", 11, Ui.GLOW_TEXT, TextAnchor.UpperRight), Ui.A(Ui.HUD_GLOW, 0.35f));
        Ui.At(_wHeatT.rectTransform, Ui.TR, Ui.TR, new Vector2(-14f, -74f), new Vector2(120f, 14f));
        _wHeatRt = Ui.Rect("Heat", _weaponPane, Ui.TL, Ui.TL, new Vector2(14f, -90f), new Vector2(212f, 5f));
        _wHeat = _wHeatRt.gameObject.AddComponent<Ui.SegBar>();
        _wHeat.segmented = false;
        _wHeat.track = new Color(0.078f, 0.098f, 0.212f);
        _wHeat.fill = Ui.AMBER;
        _wHeat.raycastTarget = false;
    }

    void WeaponRow(float y, string key, string name, out Ui.Box box, out Text label)
    {
        var row = Ui.Rect("Weapon " + key, _weaponPane, Ui.TL, Ui.TL, new Vector2(84f, y), new Vector2(142f, 26f));
        box = Ui.MakeBox(row, Ui.A(Ui.CYAN, 0.04f), Ui.HUD_FAINT, 1f);
        Ui.Chip(row, key, true, new Vector2(6f, -5f));
        label = Ui.Label(row, name, "display", 13, Ui.HUD_DIM, TextAnchor.MiddleLeft);
        Ui.At(label.rectTransform, Ui.TL, Ui.TL, new Vector2(34f, 0f), new Vector2(106f, 26f));
    }

    // ---- bottom left: the flight controls list (.hud-bl .controls), C hides it
    static readonly object[][] CONTROL_ROWS =
    {
        new object[] { "Mouse", "Yaw · pitch" },
        new object[] { new[] { "W", "S" }, "Throttle up · down" },
        new object[] { new[] { "X" }, "Cut throttle · S at zero fires retros" },
        new object[] { new[] { "Space" }, "Hold · drift: engine cuts, you coast on, the nose swings twice as fast" },
        new object[] { new[] { "A", "D" }, "Roll left · right" },
        new object[] { new[] { "Shift" }, "Afterburner while throttled up (×2 speed from the start, ×5 with the upgrades · burns fuel fast)" },
        new object[] { new[] { "G" }, "Laser overcharge on · off (needs the upgrade · up to ×3 damage · the beam draws fuel while it cuts)" },
        new object[] { new[] { "↑", "↓" }, "Pitch" },
        new object[] { new[] { "LMB" }, "Hold to fire the selected weapon (L too). The laser cuts only what the crosshair is on: aim the nose at a rock" },
        new object[] { new[] { "1", "2" }, "Mining laser · autocannon (the wheel swaps too)" },
        new object[] { new[] { "R" }, "Radar pulse" },
        new object[] { new[] { "Q", "MMB" }, "Lock the crosshair on whatever the mouse is over · hover another target and press Q to switch · otherwise press Q to release" },
        new object[] { new[] { "F" }, "Flashlight on · off in flight · the upgrade tabs when docked" },
        new object[] { new[] { "T" }, "Out of fuel · recovery to the cargo ship (15% of credits)" },
        new object[] { new[] { "E" }, "Approach control within 2,250 m of the cargo ship · deposit ore on the pad" },
        new object[] { new[] { "Tab", "I" }, "The hangar window · inventory, and the upgrade tabs when docked" },
        new object[] { new[] { "N" }, "Nav map · warp (docked in the cargo ship)" },
        new object[] { new[] { "C" }, "Hide · show this list" },
        new object[] { new[] { "F5" }, "Quick-save" },
        new object[] { new[] { "F9" }, "Test · spawn 3 raiders 2,000 to 4,000 m out" },
        new object[] { new[] { "F10" }, "Test · raiders hold their fire · again to let them fire" },
        new object[] { new[] { "F8" }, "Test · the upgrade tabs anywhere · bottomless credits · − takes a level off" },
        new object[] { new[] { "Esc" }, "Pause · the menu with settings and controls" },
    };

    void BuildControls()
    {
        Ui.Pane p;
        _controls = Pane("Controls", Ui.BL, new Vector2(20f, 18f + BAND_H + 14f), new Vector2(392f, 300f), out p, 14f, true);   // above the band
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

    // ---- the hangar window: a centred glass window over the live view, with a tab bar. Docking opens it on Inventory;
    // Tab/I opens it on Inventory anywhere (in flight only that tab is offered), F opens it on Ship refits while docked
    // (and, in the combat test, anywhere); Esc or Close shuts it.
    void BuildWindow()
    {
        _win = Ui.Rect("Hangar", _root, Ui.MID, Ui.MID, Vector2.zero, new Vector2(1100f, 760f));
        var box = Ui.MakeBox(_win, Ui.SIDE_BG, Ui.LINE2, 1f, true);
        box.bl = 1f; box.br = 1f;
        _winEyebrow = Ui.Eyebrow(_win, "Docked", Ui.MUTED);
        Ui.At(_winEyebrow.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -20f), new Vector2(300f, 14f));
        _winTitle = Ui.Label(_win, "CARGO SHIP", "display_bold", 24, Ui.TEXT);
        Ui.At(_winTitle.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -36f), new Vector2(400f, 30f));
        _winSub = Ui.Label(_win, "", "mono", 12, Ui.MUTED);
        Ui.At(_winSub.rectTransform, Ui.TL, Ui.TL, new Vector2(26f, -68f), new Vector2(400f, 16f));
        var be = Ui.Eyebrow(_win, "Balance", Ui.MUTED);
        be.alignment = TextAnchor.UpperRight;
        Ui.At(be.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -20f), new Vector2(200f, 14f));
        _winCredits = Ui.Label(_win, "0 cr", "mono_semi", 24, Ui.AMBER2, TextAnchor.UpperRight);
        Ui.At(_winCredits.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -36f), new Vector2(240f, 30f));
        // the tab bar under the header, a rule beneath it
        _tabBar = Ui.Rect("Tabs", _win, Ui.TL, Ui.TL, new Vector2(26f, -92f), new Vector2(0f, 36f));
        _tabBar.anchorMax = new Vector2(1f, 1f);
        _tabBar.offsetMin = new Vector2(26f, -128f);
        _tabBar.offsetMax = new Vector2(-26f, -92f);
        // the tabs, built once: they only ever switch which one is lit (and the upgrade tabs hide in flight)
        string[] names = { "Inventory", "Ship upgrades", "Cargo ship upgrades" };
        float tx = 0f;
        for (int i = 0; i < 3; i++)
        {
            var id = TAB_IDS[i];
            var b = Ui.Button(_tabBar, names[i], () => PickTab(id), i == 0, false, 150f, 13);
            b.rt.anchoredPosition = new Vector2(tx, 0f);
            tx += b.Width + 8f;
            _tabBtns[i] = b;
        }
        _tabHint = Ui.Label(_tabBar, "", "body", 12, Ui.DIM, TextAnchor.MiddleRight);
        Ui.At(_tabHint.rectTransform, Ui.TR, Ui.TR, Vector2.zero, new Vector2(520f, 36f));
        var rule = Ui.Rect("Rule", _win, Ui.TL, Ui.TL, new Vector2(0f, -136f), new Vector2(0f, 1f));
        rule.anchorMax = new Vector2(1f, 1f);
        Ui.Fill(rule, Ui.LINE);
        // the footer
        var foot = Ui.Rect("Foot", _win, Ui.BL, Ui.BL, Vector2.zero, new Vector2(0f, 64f));
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
        _closeBtn = Ui.Button(foot, "Close", () => CloseWindow());
        _closeBtn.rt.anchoredPosition = new Vector2(x, -14f); x += _closeBtn.Width + 10f;
        Ui.Chip(foot, "Esc", false, new Vector2(x, -14f - (36f - 19f) * 0.5f));
        _resetLink = Ui.Link(foot, "Reset save", () => ResetPressed());
        Ui.At(_resetLink.rectTransform, Ui.TR, Ui.TR, new Vector2(-26f, -20f), _resetLink.rectTransform.sizeDelta);
        _winScroll = Ui.Scroll.Make(_win, 0f, 64f, 0f, 137f);
        _winGroup = _win.gameObject.AddComponent<CanvasGroup>();
        _bodyGroup = _winScroll.viewport.gameObject.AddComponent<CanvasGroup>();
        _bodyHome = _winScroll.viewport.anchoredPosition;
        _win.gameObject.SetActive(false);
    }

    /// The window's motion, every frame: the ease open and shut (alpha and a 4% scale), the tab body sliding up into
    /// place, the balance counting to its value, the upgrade rows' flashes fading, the wheel's glide, and 1 / 2 / 3
    /// picking the tabs.
    void TickWindow(float dt)
    {
        if (_win == null || !_win.gameObject.activeSelf) return;
        _winK = Mathf.Lerp(_winK, _winWant ? 1f : 0f, 1f - Mathf.Exp(-(_winWant ? 14f : 18f) * dt));
        if (_winWant && _winK > 0.995f) _winK = 1f;   // snapped: an ease that never quite lands keeps the whole window shimmering
        if (!_winWant && (_winK < 0.02f || instantUi)) { _win.gameObject.SetActive(false); return; }
        float e = 1f - (1f - _winK) * (1f - _winK);
        _winGroup.alpha = _winK;
        _winGroup.blocksRaycasts = _winWant;
        _winGroup.interactable = _winWant;
        _win.localScale = _winK >= 1f ? Vector3.one : Vector3.one * (0.96f + 0.04f * e);
        _bodyK = instantUi ? 1f : Mathf.Lerp(_bodyK, 1f, 1f - Mathf.Exp(-13f * dt));
        if (_bodyK > 0.995f) _bodyK = 1f;
        _bodyGroup.alpha = _bodyK;
        _winScroll.viewport.anchoredPosition = _bodyHome + new Vector2(0f, _bodyK >= 1f ? 0f : -Mathf.Round((1f - _bodyK) * 14f));
        if (_credShown < 0f) _credShown = State.credits;
        _credShown = Mathf.Lerp(_credShown, State.credits, 1f - Mathf.Exp(-9f * dt));
        if (Mathf.Abs(_credShown - State.credits) < 0.6f) _credShown = State.credits;
        _winCredits.text = Data.Fmt(Mathf.Round(_credShown)) + " cr";
        foreach (var r in _rows)
        {
            if (r.flash <= 0f) continue;
            r.flash = Mathf.Max(0f, r.flash - dt * 1.6f);
            r.bg.Set(Ui.A(Ui.AMBER, 0.16f * r.flash), Ui.A(Ui.AMBER, 0.6f * r.flash));
        }
        _winScroll.Tick(dt);
        if (_winWant && RefitsAvailable && !MenuVisible)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) PickTab("inv");
            if (Input.GetKeyDown(KeyCode.Alpha2)) PickTab("ship");
            if (Input.GetKeyDown(KeyCode.Alpha3)) PickTab("depot");
        }
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

    /// Docking opens the window on the Inventory tab; leaving closes it (and the map).
    public void OnDocked(bool isDocked)
    {
        if (isDocked) ShowWindow("inv");
        else
        {
            CloseWindow();
            if (MapOpen) CloseMap();
        }
    }

    /// The refit tabs are offered while docked (and anywhere in the combat test).
    bool RefitsAvailable { get { return ship != null && (ship.docked || State.sandbox); } }

    public void ShowWindow(string tab)
    {
        if (_win == null) return;
        if (tab != "inv" && !RefitsAvailable) tab = "inv";
        if (!InvOpen) Audio.Play("ui_open");
        else if (tab != _winTab) Audio.Play("ui_tab");
        if (!InvOpen) { _winK = instantUi ? 1f : 0f; _win.localScale = Vector3.one * (instantUi ? 1f : 0.96f); if (_winGroup != null) _winGroup.alpha = _winK; }
        _winWant = true;
        if (instantUi) _bodyK = 1f;
        _winTab = tab;
        _win.gameObject.SetActive(true);
        _svcSig = "";
        _invSig = "";
        RefreshWindow();
    }

    public void CloseWindow()
    {
        if (_win == null || !InvOpen) return;
        if (_dragPreview != null) { Destroy(_dragPreview.gameObject); _dragPreview = null; _dragging = null; }
        _winWant = false;   // the ease out runs in TickWindow, then the window deactivates
        Audio.Play("ui_close");
    }

    /// F: the window on Ship refits (docked, or anywhere in the combat test); pressed again, it closes.
    public void ToggleServices()
    {
        if (!RefitsAvailable) return;
        if (InvOpen && _winTab != "inv") CloseWindow();
        else ShowWindow("ship");
    }

    /// Tab / I: the window on Inventory; pressed again, it closes.
    public void ToggleInventory()
    {
        if (InvOpen) CloseWindow();
        else ShowWindow("inv");
    }

    void PickTab(string tab)
    {
        if (tab == _winTab) return;
        Audio.Play("ui_tab");
        _bodyK = 0f;
        _winTab = tab;
        _svcSig = "";
        RefreshWindow();
    }

    /// One upgrade row: name and pips, the description with the next level in bold, the price button on the right (and
    /// the test's minus). Built once per tab and kept in _rows; UpdateRows keeps it current, a purchase lights it.
    void RefitRow(Ui.Flow f, string key, bool depot, string name, int total, bool first)
    {
        if (!first) { f.Rule(); f.Gap(13f); }
        float top = f.y;
        var row = new UpRow { key = key, depot = depot, pips = new Image[total] };
        var bgRt = Ui.Rect("RowBg", f.parent, Ui.TL, Ui.TL, new Vector2(f.x - 12f, f.y + 8f), new Vector2(f.w + 24f, 10f));
        row.bg = Ui.MakeBox(bgRt, Ui.A(Ui.AMBER, 0f), Ui.A(Ui.AMBER, 0f), 1f);
        bgRt.SetAsFirstSibling();
        var n = Ui.Label(f.parent, name, "display", 16, Ui.TEXT);
        Ui.At(n.rectTransform, Ui.TL, Ui.TL, new Vector2(f.x, f.y), new Vector2(300f, 20f));
        float px = f.x + Ui.Measure(n) + 12f;
        for (int j = 0; j < total; j++)
        {
            var pip = Ui.Rect("Pip", f.parent, Ui.TL, Ui.TL, new Vector2(px + j * 13f, f.y - 5f), new Vector2(8f, 8f));
            row.pips[j] = Ui.Fill(pip, Ui.LINE2);
        }
        f.y -= 23f;
        bool test = !depot && State.sandbox;
        row.desc = f.Para(" ", "body", 13, Ui.MUTED, 0f, TextAnchor.UpperLeft, 0f, f.w - 118f - 14f - (test ? 48f : 0f));
        row.desc.rectTransform.sizeDelta = new Vector2(row.desc.rectTransform.sizeDelta.x, 36f);   // two lines, whatever the text does
        f.y = top - 23f - 36f;
        float rowH = top - f.y;
        var k2 = key;
        row.buy = Ui.Button(f.parent, "0 cr", () => { if (depot) BuyDepot(k2); else Buy(k2); }, true, true, 118f, 14);
        row.buy.rt.anchoredPosition = new Vector2(f.x + f.w - 118f, top - (rowH - row.buy.Height) * 0.5f);
        if (test)
        {
            row.minus = Ui.Button(f.parent, "−", () => Downgrade(k2), false, true, 40f, 14);
            row.minus.rt.anchoredPosition = new Vector2(f.x + f.w - 118f - 48f, top - (rowH - row.minus.Height) * 0.5f);
        }
        bgRt.sizeDelta = new Vector2(f.w + 24f, rowH + 16f);
        _rows.Add(row);
        f.Gap(13f);
    }

    void LightRow(string key)
    {
        foreach (var r in _rows) if (r.key == key) r.flash = 1f;
    }

    Text H3(Ui.Flow f, string text, float gap = 12f)
    {
        return f.Para(text.ToUpperInvariant(), "display", 13, Ui.MUTED, gap);
    }

    public void RefreshServices() { RefreshWindow(); }

    /// The window's contents for the current tab, rebuilt when what they show changes (the signature covers all of it).
    void RefreshWindow()
    {
        if (ship == null || _win == null || !_win.gameObject.activeSelf) return;
        if (_dragging != null) return;   // not under a drag: the slots would vanish under the pointer
        bool docked = ship.docked;
        bool atHub = ship.hold;
        var sb = new System.Text.StringBuilder();
        sb.Append(_winTab).Append('|').Append(docked).Append('|').Append(atHub).Append('|').Append(RefitsAvailable).Append('|').Append(zone.id).Append('|');
        if (_winTab == "inv")
        {
            // the inventory tab: its stacks and slots (not the fuel supply or the parts, which tick every frame on the pad
            // and only move their gauges; and credits only at the Hub, where the market shows them)
            sb.Append(State.StoreUsed()).Append('|').Append(State.CargoTotal().ToString("0")).Append('|').Append(State.StoreTotal().ToString("0")).Append('|').Append(State.CargoSlots()).Append('|');
            if (atHub) sb.Append(Mathf.RoundToInt(State.credits)).Append('|').Append(Mathf.RoundToInt(State.droneUnits));
            foreach (var k in Data.ORE_KEYS) sb.Append(',').Append(State.cargo[k].ToString("0")).Append('/').Append(State.store[k].ToString("0")).Append('/').Append(State.marketNext[k].ToString("0.00"));   // the targets, not the drifting prices: a rebuild every quarter second pulled the tabs out from under the mouse
        }
        else if (_winTab == "depot") sb.Append(Mathf.RoundToInt(State.droneUnits));   // the note under the rows
        string sig = sb.ToString();
        if (sig == _svcSig) { UpdateRows(); return; }
        _svcSig = sig;
        _invSig = sig;
        _bodyK = 0f;
        // the header
        _winEyebrow.text = atHub ? "HOLDING STATION" : docked ? "DOCKED" : "IN FLIGHT";
        _winTitle.text = docked ? "CARGO SHIP" : "SHIP";
        _winSub.text = atHub ? "Off " + (zone.colony ?? "the colony") : docked ? CargoShip.BayName(ship.dockSide) : zone.name;
        _departBtn.SetText(atHub ? "Nav map" : "Depart");
        _departBtn.rt.gameObject.SetActive(docked);
        _navBtn.rt.gameObject.SetActive(docked && !atHub);
        _resetLink.gameObject.SetActive(docked);
        // the tabs: the lit one follows the tab, the upgrade tabs show only where upgrades can be bought
        for (int i = 0; i < 3; i++)
        {
            bool show = i == 0 || RefitsAvailable;
            if (_tabBtns[i].rt.gameObject.activeSelf != show) _tabBtns[i].rt.gameObject.SetActive(show);
            if (_tabBtns[i].primary != (TAB_IDS[i] == _winTab)) _tabBtns[i].SetPrimary(TAB_IDS[i] == _winTab);
        }
        _tabHint.text = _winTab == "inv" ? (docked ? "Drag stacks between the grids or double-click one · E deposits all" : "Drag a stack out of the grid, or ✕, to jettison it") : "The price button buys the next level · 1 / 2 / 3 pick the tabs";
        // the body, rebuilt where it stood: the scroll position is kept (a purchase must not throw the list to the top)
        var keep = _winScroll.content.anchoredPosition;
        _winScroll.Clear();
        holdSlots.Clear();
        storeSlots.Clear();
        _depositBtn = null;
        _refitsBox = null;
        _rows.Clear();
        _rowSig = "";
        _gStoreW = _gFuelW = _gPartsW = null;
        float w = _winScroll.Width;
        var f = new Ui.Flow(_winScroll.content, 26f, 26f, w - 52f);
        if (_winTab == "inv") InventoryTab(f, docked, atHub);
        else if (_winTab == "ship") ShipTab(f);
        else DepotTab(f);
        _winScroll.SetHeight(f.Used + 10f);
        float maxY = Mathf.Max(0f, f.Used + 10f - _winScroll.viewport.rect.height);
        _winScroll.SetTarget(Mathf.Clamp(keep.y, 0f, maxY));
        UpdateRows();
    }

    /// The upgrade rows follow the levels and the balance in place: the pips, the description, the price button (its
    /// text and whether it can be pressed) and the test's minus.
    void UpdateRows()
    {
        if (_rows.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.Append(Mathf.RoundToInt(State.credits));
        foreach (var k in Data.UPGRADE_KEYS) sb.Append(State.up[k]);
        foreach (var k in Data.DEPOT_KEYS) sb.Append(State.depot[k]);
        string sig = sb.ToString();
        if (sig == _rowSig) return;
        _rowSig = sig;
        foreach (var r in _rows)
        {
            int i = r.depot ? State.depot[r.key] : State.up[r.key];
            int have = r.depot ? i : i + 1;
            float[] costs = r.depot ? Data.DEPOT_UPGRADES[r.key].costs : Data.UPGRADES[r.key].costs;
            bool maxed = i >= costs.Length;
            string now = r.depot ? Data.DescribeDepot(r.key, i) : Data.Describe(r.key, i);
            string next = maxed ? "" : (r.depot ? Data.DescribeDepot(r.key, i + 1) : Data.Describe(r.key, i + 1));
            r.desc.text = maxed ? Ui.Col("<b>" + now + "</b>", Ui.TEXT) + " · Fully upgraded" : now + " → " + Ui.Col("<b>" + next + "</b>", Ui.TEXT);
            for (int j = 0; j < r.pips.Length; j++) r.pips[j].color = j < have ? Ui.AMBER : Ui.LINE2;   // recoloured, never re-added
            float cost = maxed ? 0f : costs[i];
            r.buy.SetText(maxed ? "Max" : Data.Fmt(cost) + " cr");
            r.buy.SetPrimary(!maxed && State.credits >= cost);
            r.buy.interactable = !maxed && State.credits >= cost;
            if (r.minus != null) r.minus.interactable = have > 1;
        }
    }

    /// Inventory: the hold's grid on the left and the cargo ship storage's on the right (in flight, the hold alone),
    /// then the cargo ship's gauges and, at the Hub, the market.
    void InventoryTab(Ui.Flow f, bool docked, bool atHub)
    {
        var holdSt = State.Stacks(State.cargo);
        var storeSt = docked ? State.Stacks(State.store) : new List<State.Stack>();
        int ns = State.CargoSlots(), us = State.UsedSlots(), su = State.StoreUsed();
        float gutter = 30f;
        float colW = docked ? (f.w - gutter) * 0.42f : f.w;
        float rightW = f.w - gutter - colW;
        var L = new Ui.Flow(f.parent, f.x, -f.y, colW);
        // the hold
        var hh = L.Box(22f);
        var he = Ui.Eyebrow(hh, "Your hold", Ui.MUTED);
        Ui.At(he.rectTransform, Ui.TL, Ui.TL, new Vector2(0f, -4f), new Vector2(200f, 14f));
        Cell(hh, us + " / " + ns + " slots", colW - 160f, 160f, true, us >= ns ? Ui.AMBER : Ui.DIM, "mono", 13);
        L.Gap(6f);
        var bar = L.Box(6f, "Bar");
        var seg = bar.gameObject.AddComponent<Ui.SegBar>();
        seg.segmented = false; seg.track = new Color(0.078f, 0.098f, 0.212f); seg.fill = Ui.CARGO; seg.raycastTarget = false;
        seg.Set((float)us / ns, us >= ns ? Ui.AMBER : Ui.CARGO);
        L.Gap(12f);
        SlotGrid(L, holdSt, ns, false, docked, holdSlots, docked ? 2 : 3);
        L.Gap(10f);
        var row = L.Box(36f);
        bool canDeposit = docked && State.CargoTotal() > 0.5f;
        _depositBtn = Ui.Button(row, "Deposit all", () => { if (ship != null) ship.DepositAll(); _svcSig = ""; RefreshWindow(); }, canDeposit);
        _depositBtn.interactable = canDeposit;
        Ui.Chip(row, "E", false, new Vector2(_depositBtn.Width + 10f, -(36f - 19f) * 0.5f));
        L.Gap(12f);
        if (us > 0) TotalRow(L, "Hold value at Hub prices", Data.Fmt(State.ValueOf(State.cargo)) + " cr");
        else L.Para("The hold is empty. Break a rock and fly through the ore it drops.", "body", 13, Ui.DIM, 10f);
        float bottom = L.y;
        if (docked)
        {
            // the storage
            var R = new Ui.Flow(f.parent, f.x + colW + gutter, -f.y, rightW);
            var sh = R.Box(22f);
            var se = Ui.Eyebrow(sh, "Cargo ship storage", Ui.MUTED);
            Ui.At(se.rectTransform, Ui.TL, Ui.TL, new Vector2(0f, -4f), new Vector2(200f, 14f));
            Cell(sh, su + " / " + Data.STORE_SLOTS + " slots", rightW - 260f, 160f, true, su >= Data.STORE_SLOTS ? Ui.AMBER : Ui.DIM, "mono", 13);
            var tl = Ui.Link(sh, "▲ Take all", () => { if (ship != null) ship.TakeAll(); _svcSig = ""; RefreshWindow(); }, 13);
            Ui.At(tl.rectTransform, Ui.TR, Ui.TR, Vector2.zero, tl.rectTransform.sizeDelta);
            R.Gap(6f);
            var sbar = R.Box(6f, "Bar");
            var sseg = sbar.gameObject.AddComponent<Ui.SegBar>();
            sseg.segmented = false; sseg.track = new Color(0.078f, 0.098f, 0.212f); sseg.fill = Ui.CARGO; sseg.raycastTarget = false;
            sseg.Set((float)su / Data.STORE_SLOTS, su >= Data.STORE_SLOTS ? Ui.AMBER : Ui.CARGO);
            R.Gap(12f);
            SlotGrid(R, storeSt, Data.STORE_SLOTS, true, true, storeSlots, 3);
            R.Gap(10f);
            if (su > 0) TotalRow(R, "Storage value at Hub prices", Data.Fmt(State.ValueOf(State.store)) + " cr");
            if (su > 0 && us > 0) TotalRow(R, "Everything aboard", Data.Fmt(State.ValueOf(State.store) + State.ValueOf(State.cargo)) + " cr", false);
            R.Para("The storage rides with the cargo ship and sells at the Hub.", "body", 12, Ui.DIM, 6f);
            bottom = Mathf.Min(bottom, R.y);
        }
        f.y = bottom;
        f.Gap(16f);
        if (docked)
        {
            f.Rule();
            f.Gap(18f);
            H3(f, atHub ? "Cargo ship and market" : "Cargo ship");
            bool full = su >= Data.STORE_SLOTS;
            _gStoreW = Gauge(f, "Cargo ship storage", full ? Ui.AMBER : Ui.CARGO, State.StoreTotal() / (Data.STORE_SLOTS * Data.STACK), su + " / " + Data.STORE_SLOTS + " slots" + (full ? " · FULL" : ""));
            _gFuelW = Gauge(f, "Cargo ship fuel supply", Ui.CYAN, 0f, "");
            _gPartsW = Gauge(f, "Repair parts", Ui.GREEN, 0f, "");
            TickWindowGauges();
            if (atHub) Market(f);
        }
        else TotalRow(f, "Cargo ship storage", su + " / " + Data.STORE_SLOTS + " slots · dock to transfer", false, Ui.TEXT);
    }

    /// Ship refits: the personal ship's rows.
    void ShipTab(Ui.Flow f)
    {
        H3(f, "Ship upgrades");
        float refitsTop = f.y;
        bool first = true;
        foreach (var key in Data.UPGRADE_KEYS)
        {
            RefitRow(f, key, false, Data.UPGRADES[key].name, Data.UPGRADES[key].levels.Length, first);
            first = false;
        }
        _refitsBox = Ui.Rect("RefitsBox", f.parent, Ui.TL, Ui.TL, new Vector2(f.x, refitsTop), new Vector2(f.w, refitsTop - f.y));
    }

    /// Cargo ship refits: the mast dish and the collector drones.
    void DepotTab(Ui.Flow f)
    {
        H3(f, "Cargo ship upgrades");
        bool first = true;
        foreach (var key in Data.DEPOT_KEYS)
        {
            RefitRow(f, key, true, Data.DEPOT_UPGRADES[key].name, Data.DEPOT_UPGRADES[key].costs.Length, first);
            first = false;
        }
        f.Para("The dish leaves the ore it frees adrift for you to pick up. Collector drones gather it and stow it in the cargo ship storage" + (State.droneUnits > 0.5f ? " · <b>" + Data.Fmt(State.droneUnits) + "</b> stowed so far" : "") + ".", "body", 12, Ui.DIM, 20f);
    }

    Ui.Gauge Gauge(Ui.Flow f, string title, Color c, float v, string text)
    {
        var g = Ui.Gauge.Make(f.parent, title, c, f.x, f.y, f.w, true, 10f);
        g.Show(v, text, c);
        f.y -= g.height + 12f;
        return g;
    }

    /// The fuel supply and the parts gauges follow the live values without a rebuild.
    void TickWindowGauges()
    {
        if (_gFuelW != null)
        {
            bool lowFuel = State.shipFuel < Data.CARGO_FUEL_CAP * 0.2f;
            _gFuelW.Show(State.shipFuel / Data.CARGO_FUEL_CAP, Mathf.FloorToInt(State.shipFuel) + " / " + Mathf.RoundToInt(Data.CARGO_FUEL_CAP), lowFuel ? Ui.AMBER : Ui.CYAN);
        }
        if (_gPartsW != null)
        {
            bool lowParts = State.parts < Data.PARTS_CAP * 0.2f;
            _gPartsW.Show(State.parts / Data.PARTS_CAP, Mathf.FloorToInt(State.parts) + " / " + Data.PARTS_CAP, lowParts ? Ui.AMBER : Ui.GREEN);
        }
    }

    void BuyDepot(string key)
    {
        string msg;
        bool ok = State.BuyDepot(key, out msg);
        Toast(msg, !ok);
        if (ok) { Audio.Play("chime"); LightRow(key); }
        UpdateRows();
    }

    void Buy(string key)
    {
        string msg;
        bool ok = State.Buy(key, out msg);
        Toast(msg, !ok);
        if (ok) { Audio.Play("chime"); LightRow(key); }
        if (ok && ship != null) ship.ConfigureModel();   // the fitting on the hull changes with its tier
        UpdateRows();
    }

    void Downgrade(string key)
    {
        string msg;
        bool ok = State.Downgrade(key, out msg);
        Toast(msg, !ok);
        if (ok && ship != null) ship.ConfigureModel();
        UpdateRows();
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

    void TotalRow(Ui.Flow f, string label, string value, bool topLine = true, Color? color = null)
    {
        if (topLine) { f.Rule(Ui.LINE2); f.Gap(10f); }
        var r = f.Box(20f);
        Cell(r, label, 0f, 400f, false, Ui.TEXT, "mono", 15);
        Cell(r, value, f.w - 300f, 300f, true, color ?? Ui.AMBER2, "mono_semi", 15);
        f.Gap(10f);
    }

    void SlotGrid(Ui.Flow f, List<State.Stack> stacks, int n, bool store, bool docked, List<Slot> into, int cols = 3)
    {
        float colW = (f.w - 10f * (cols - 1)) / cols;
        int rows = Mathf.CeilToInt((float)n / cols);
        for (int i = 0; i < n; i++)
        {
            int r = i / cols, c = i % cols;
            var s = MakeSlot(f.parent, new Vector2(f.x + c * (colW + 10f), f.y - r * 102f), colW, i < stacks.Count ? stacks[i] : null, store, docked);
            into.Add(s);
        }
        f.y -= rows * 102f - 10f;
    }

    void RefreshInventory() { RefreshWindow(); }

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
        _gCargo.Show(State.CargoTotal() / State.CargoCapacity(), us + " / " + ns, full ? Ui.AMBER : Ui.CARGO);
        _gCargo.value.color = full ? Ui.AMBER2 : Ui.GLOW_TEXT;
        float spd = ship.Speed * Data.METRE;
        _speedBig.text = docked ? (hold ? "HOLD" : "DOCK") : Data.Fmt(spd);
        _speedUnit.gameObject.SetActive(!docked);
        // FLIGHT: the zone in the eyebrow, then the way to the cargo ship and the field, the radar and the threat
        float toCarrier = carrier != null ? (ship.TruePos - carrier.truePos).magnitude : 0f;
        var here = docked ? null : belt.FieldAt(ship.TruePos);
        _row1.text = docked ? Kv("CARGO SHIP", hold ? "holding station" : "docked") : Kv("CARGO SHIP", Data.Fm(toCarrier) + " m") + "   " + Kv("FIELD", here != null ? here.name : "—");
        string laser = ship.laserOn ? "CUTTING" : (ship.firing ? "FIRING" : "ready");
        if (ship.overcharge) laser += " ⚡×" + State.Stat("overcharge").mult;
        string radar = ship.radarCd <= 0f ? "READY" : ship.radarCd.ToString("0.0") + "s";
        float reach = State.Stat("range").reach;
        bool locked = ship.lockKind != "" && !docked;
        string rangeTxt = locked ? Data.Fm(ship.lockDist) + " / " + Data.Fm(reach) + " m" : Data.Fm(reach) + " m";   // the lock's distance against the beam's reach
        int threat = game != null && game.raiders != null ? game.raiders.threat : 0;
        string threatTxt = threat > 0 ? Ui.Col(threat + " raider" + (threat > 1 ? "s" : ""), Ui.RED) : Ui.Col("none", Ui.GLOW_TEXT);
        string weaponTxt = ship.weapon == "gun" ? "Autocannon" : "Laser";
        _row2.text = Kv("RADAR", radar) + "   THREAT " + threatTxt;
        // WEAPON: the equipped row lit amber, the other dim; the heat reads for the cannon
        bool gunUp = ship.weapon == "gun";
        _wIcon.Set(ship.weapon);
        _wBox1.Set(gunUp ? Ui.A(Ui.CYAN, 0.04f) : Ui.A(Ui.AMBER, 0.16f), gunUp ? Ui.HUD_FAINT : Ui.AMBER);
        _wBox2.Set(gunUp ? Ui.A(Ui.AMBER, 0.16f) : Ui.A(Ui.CYAN, 0.04f), gunUp ? Ui.AMBER : Ui.HUD_FAINT);
        _wName1.color = gunUp ? Ui.HUD_DIM : Ui.GLOW_TEXT;
        _wName2.color = gunUp ? Ui.GLOW_TEXT : Ui.HUD_DIM;
        _wHeatT.text = ship.gunOverheated ? Ui.Col("OVERHEATED", Ui.RED) : Mathf.RoundToInt(ship.gunHeat * 100f) + "%";
        _wHeat.Set(ship.gunHeat, ship.gunOverheated ? Ui.RED : ship.gunHeat > 0.75f ? Ui.AMBER2 : Ui.AMBER);
        // the target: the panel follows the lock when there is one, else the crosshair target
        bool hasTarget = ship.target >= 0 && ship.target < belt.count && belt.alive[ship.target] && !docked;
        int panelRock = locked && ship.lockKind == "rock" ? ship.lockRock : (hasTarget ? ship.target : -1);
        bool showTarget = false;
        var panelRaider = locked && ship.lockKind == "raider" ? ship.lockRaider : ship.raiderTarget;
        if (panelRaider != null && !panelRaider.dead && !docked)
        {
            showTarget = true;
            float rd = Mathf.Max(0f, (panelRaider.pos - ship.LaserOrigin()).magnitude - Raiders.RADIUS);
            _tName.text = locked && ship.lockKind == "raider" ? "Pirate raider · locked" : "Pirate raider";
            _tRows.text = Kv("SIZE", "Ship") + "   " + Kv("RANGE", Data.Fm(rd) + " m") + "   " + Kv("SHIELD", Mathf.CeilToInt(Mathf.Max(0f, panelRaider.shield)) + " / " + Mathf.RoundToInt(panelRaider.maxShield));
            _tHpRow.gameObject.SetActive(true);
            _tHp.Set(panelRaider.hp / Mathf.Max(1f, panelRaider.maxHp), Ui.RED);
            _tHpT.text = Mathf.CeilToInt(Mathf.Max(0f, panelRaider.hp)) + " / " + Mathf.RoundToInt(panelRaider.maxHp);
            bool hostile = panelRaider.state == "attack";
            _tWarn.text = hostile ? (State.Stat("gun").reach > 0f ? "Hostile · autocannon on it" : "Hostile · no autocannon fitted") : "";
            _tWarn.gameObject.SetActive(hostile);
        }
        else if (locked && ship.lockKind == "station")
        {
            showTarget = true;
            _tName.text = "Cargo ship · locked";
            _tRows.text = Kv("SIZE", "Carrier") + "   " + Kv("RANGE", Data.Fm(ship.lockDist) + " m");
            _tHpRow.gameObject.SetActive(false);
            _tWarn.gameObject.SetActive(false);
        }
        else if (panelRock >= 0 && panelRock < belt.count && belt.alive[panelRock])
        {
            showTarget = true;
            int i = panelRock;
            float tdist = Mathf.Max(0f, (belt.RockPos(i) - ship.LaserOrigin()).magnitude - belt.radius[i]);
            _tName.text = (belt.ore[i] < 0 ? "Barren" : Data.ORES[belt.ore[i]].name) + " Rock" + (locked ? " · locked" : "");
            _tRows.text = Kv("SIZE", Belt.CLS_NAME[belt.cls[i]]) + "   " + Kv("RANGE", Data.Fm(tdist) + " m");
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
        }
        if (!showTarget)
        {
            _tName.text = "No target";
            _tRows.text = Ui.Col("Q or MMB locks what the mouse is over", Ui.HUD_DIM);
            _tHpRow.gameObject.SetActive(false);
            _tWarn.gameObject.SetActive(false);
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
        float baseY = 18f + BAND_H + 14f;
        _prompt.anchoredPosition = new Vector2(0f, baseY);
        _notice.anchoredPosition = new Vector2(0f, baseY + (segs.Count > 0 ? _prompt.sizeDelta.y + 10f : 0f));
        _notice.gameObject.SetActive(started && full && !docked && !inCut);
        // what shows when: the hangar hides the situation, the controls and the hint; a cutscene hides nearly everything
        bool showFlight = started && !inCut;
        _status.gameObject.SetActive(showFlight);
        float sa = docked ? 0.85f : 1f;
        if (Mathf.Abs(_statusPane.alpha - sa) > 0.01f) { _statusPane.alpha = sa; _statusPane.SetVerticesDirty(); }
        _readouts.gameObject.SetActive(showFlight);
        _controls.gameObject.SetActive(showFlight && !docked && _controlsShown);
        _prompt.gameObject.SetActive(showFlight && !docked && segs.Count > 0);
        _target.gameObject.SetActive(showFlight);   // up in the hangar too, reading "No target"
        _weaponPane.gameObject.SetActive(showFlight);
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
            if (InvOpen) CloseWindow();
        }
        var fc = _fade.color;
        fc.a = ship.WarpFade();
        _fade.color = fc;
        // the gunnery crosshair: where a bolt goes, at gun range; and the lead pip: where to put it for the locked raider
        if (showFlight && !docked && ship.cut == null && ship.CanFly)
        {
            // the crosshair sits on the nose ray at the weapon's reach, with either weapon (the mouse steers the ship; the
            // nose is the aim); it never moves to a target: no assist of any kind
            float cDist = ship.weapon == "gun" ? ship.GunReach : State.Stat("range").reach;
            Vector2 cp;
            bool cBehind = Project(ship.LaserOrigin() + ship.Forward * cDist - game.worldOffset, out cp);
            _crosshairRt.gameObject.SetActive(!cBehind && OnScreen(cp));
            _crosshairRt.anchoredPosition = cp;
            _crosshair.Set(ship.gunFiring || ship.laserOn);
            // the crosshair stands in for the mouse: the pointer hides while it shows and no panel wants clicks
            Cursor.visible = !(_crosshairRt.gameObject.activeSelf && !InvOpen && !MapOpen && !MenuVisible);
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
                    // drawn at the crosshair's own distance along the line to the lead point, so crosshair-on-pip means the nose
                    // is on the lead point exactly (the dish sits off the camera: the lead point itself would draw a little off)
                    var toLead = (ship.LeadPoint(r) - ship.LaserOrigin()).normalized;
                    bool lBehind = Project(ship.LaserOrigin() + toLead * cDist - game.worldOffset, out lp);
                    if (lBehind || !OnScreen(lp)) continue;
                    if (li >= _leadPips.Count) _leadPips.Add(Ui.Marker.Make(_root, Ui.AMBER2, true));
                    _leadPips[li++].Place(lp - new Vector2(0f, 38f), false, 0f, "LEAD");
                }
            }
            for (; li < _leadPips.Count; li++) _leadPips[li].Hide();
        }
        else { _crosshairRt.gameObject.SetActive(false); foreach (var p in _leadPips) p.Hide(); Cursor.visible = true; }
        // the pointer dot stands in for the hidden pointer, on top of everything
        bool dotOn = !Cursor.visible;
        if (_pointerDot.gameObject.activeSelf != dotOn) _pointerDot.gameObject.SetActive(dotOn);
        if (dotOn)
        {
            var dm = Input.mousePosition;
            _pointerDot.anchoredPosition = new Vector2(dm.x, dm.y) / _canvas.scaleFactor;
            _pointerDot.SetAsLastSibling();
        }
        // the bracket: on the locked target, whatever it is, fitted to how big it looks; else a small one on a rock in the sights
        {
            Vector3 bracketAt = Vector3.zero;
            float bracketR = 0f;
            bool bracketLock = false;
            if (locked && ship.lockKind == "rock" && ship.lockRock >= 0 && ship.lockRock < belt.count && belt.alive[ship.lockRock]) { bracketAt = belt.RockPos(ship.lockRock); bracketR = belt.radius[ship.lockRock]; bracketLock = true; }
            else if (locked && ship.lockKind == "raider" && ship.lockRaider != null && !ship.lockRaider.dead) { bracketAt = ship.lockRaider.pos; bracketR = Raiders.RADIUS * 2f; bracketLock = true; }
            else if (locked && ship.lockKind == "station" && carrier != null) { bracketAt = carrier.truePos; bracketR = CargoShip.HALF.x; bracketLock = true; }
            else if (hasTarget) { bracketAt = belt.RockPos(ship.target); bracketR = belt.radius[ship.target]; }
            if (bracketR > 0f && showFlight && ship.cut == null && !docked)
            {
                Vector2 sp;
                bool behind = Project(bracketAt - game.worldOffset, out sp);
                _reticleRt.gameObject.SetActive(!behind);
                _reticleRt.anchoredPosition = sp;
                // the projected radius, in canvas pixels, with a margin; never smaller than a hand's width
                float camD = (bracketAt - game.worldOffset - _cam.transform.position).magnitude;
                float tanHalf = Mathf.Tan(_cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float pr = camD > 1f ? bracketR * (Screen.height * 0.5f) / (camD * tanHalf) / _canvas.scaleFactor : 0f;
                float side = Mathf.Clamp(pr * 2f * 1.25f + 16f, bracketLock ? 56f : 44f, 480f);
                _reticleRt.sizeDelta = new Vector2(side, side);
                _reticle.Set(ship.laserOn || ship.gunFiring, bracketLock);
            }
            else _reticleRt.gameObject.SetActive(false);
        }
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
        // the hangar window fills the screen between a top margin and the status pane; the tutorial card, which is only
        // there at the start of the game, draws over its corner rather than the window making room for it
        float topInset = 30f;
        float bottomInset = 18f + BAND_H + 16f;   // the hangar window stops above the HUD band
        float winH = Mathf.Min(760f, _canvasSize.y - topInset - bottomInset);
        var wantSize = new Vector2(Mathf.Min(1100f, _canvasSize.x - 60f), winH);
        var wantPos = new Vector2(0f, (bottomInset - topInset) * 0.5f);
        float ek = instantUi || !_win.gameObject.activeSelf ? 1f : 1f - Mathf.Exp(-14f * dt);   // eased in play, so the card coming or going never snaps it
        var winSz = Vector2.Lerp(_win.sizeDelta, wantSize, ek);
        var winPos = Vector2.Lerp(_win.anchoredPosition, wantPos, ek);
        if ((winSz - wantSize).sqrMagnitude < 1f) winSz = wantSize;   // snapped to whole pixels once it is there, or the text shimmers
        if ((winPos - wantPos).sqrMagnitude < 1f) winPos = wantPos;
        _win.sizeDelta = new Vector2(Mathf.Round(winSz.x), Mathf.Round(winSz.y));
        _win.anchoredPosition = new Vector2(Mathf.Round(winPos.x), Mathf.Round(winPos.y));
        TickWindow(dt);
        if (InvOpen && Time.frameCount % 15 == 0) { RefreshWindow(); TickWindowGauges(); }
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
