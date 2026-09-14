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
        if (FindFirstObjectByType<EventSystem>() == null)
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
        BuildMenu();
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
        for (int i = 0; i < _toasts.Count; i++)
        {
            _toasts[i].Key.rectTransform.anchoredPosition = new Vector2(0f, -24f * (_toasts.Count - 1 - i));
        }
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
        _world.text = "ZONE " + zone.name + "    LASER " + laser + "\nRANGE " + Data.Fm(State.Stat("range").reach) + " m    RADAR " + radar + "    CR " + Data.Fmt(State.credits);
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
        _hint.text = ship.target >= 0 ? "LMB  Hold to mine" : (ship.firing ? "Aim the nose at a rock" : "");
    }
}
