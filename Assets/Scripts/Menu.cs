using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// The start menu and the pause menu, one card (#intro .card.menu in belt-runner-3d.html): the sector eyebrow, the
/// BELT RUNNER title, a line about the game, then Continue / Resume, New game, Controls and Settings. Settings has the
/// sound switch and volume, the HUD size, a tutorial restart and the save wipe; Controls is the long list of keys.
/// Escape brings it up over the frozen game and takes it away again. Game owns the game state; this only asks.
public class Menu
{
    public Action onStart, onResume, onNewGame, onWipe, onTutorialRestart, onQuit;
    public Action<string, float> onSetting;   // "sound" 0/1, "volume" 0..1, "hud" 0.7..1.6

    public bool started, hasSave;
    public bool Visible { get { return _overlay != null && _overlay.gameObject.activeSelf; } }

    RectTransform _overlay, _card;
    Ui.Pane _pane;
    readonly Dictionary<string, RectTransform> _pages = new Dictionary<string, RectTransform>();
    readonly Dictionary<string, float> _pageH = new Dictionary<string, float>();
    Text _eyebrow;
    Ui.Btn _continueBtn, _newBtn, _soundBtn, _musicBtn, _tutBtn, _wipeBtn, _displayBtn;
    Text _continueInfo, _newInfo, _volT, _mvolT, _hudT;
    Slider _vol, _mvol, _hud;
    bool _newArmed, _wipeArmed, _syncing;
    float _newT, _wipeT, _tutT;
    float _headH;
    const float W = 520f, PAD = 34f;

    static readonly object[][] CONTROLS =
    {
        new object[] { "Mouse", "Aim the ship: cursor left or right of centre yaws, above or below pitches." },
        new object[] { new[] { "W", "S" }, "Throttle up and down. The engines hold whatever setting you leave them at. X cuts to zero; S at zero fires the retro thrusters." },
        new object[] { new[] { "A", "D", "↑", "↓" }, "Roll left and right · pitch up and down on keys." },
        new object[] { new[] { "G" }, "Laser overcharge on or off. With the upgrade fitted, the beam cuts ×1.5 to ×3 harder while it is armed, and the reactor feeds it from your fuel tank the whole time it is cutting. It switches itself off when the tank runs dry." },
        new object[] { new[] { "Shift" }, "Afterburner: hold it with the throttle open and thrust and top speed multiply, ×2 as fitted from the start and up to ×5 with the upgrades. It burns fuel far faster, so use it in bursts." },
        new object[] { new[] { "R" }, "Radar pulse · marks every ore rock in scanner range" },
        new object[] { new[] { "LMB" }, "Hold to fire the mining laser (L too). The dish under the nose cuts whatever the crosshair is on until it breaks. It never picks targets by itself: keep the nose on the rock." },
        new object[] { new[] { "Q", "MMB" }, "Hover the mouse over a rock or the cargo ship (the label names it), then press Q or the middle mouse button to lock the crosshair on it. The ship steers itself to keep it in the crosshair (you keep the throttle and roll) until it breaks up or goes beyond 50,000 m. Hover a different target and press Q to switch directly to it. Press Q over the current target or empty space to release the lock. A locked object always shows its range, and its details sit top centre." },
        new object[] { new[] { "F" }, "Flashlight: a spot beam from the nose, on or off. While docked F opens the hangar window on its Ship upgrades tab instead (again to close)." },
        new object[] { new[] { "T" }, "Out of fuel? T calls for recovery: the ship is brought straight back to a pad in the cargo ship's hangar for 15% of your credits. A hull breach calls it by itself." },
        new object[] { "Raiders", "Pirate raiders hold station off the rich pockets (the KP fields) and attack within 3,250 m. Roll the scroll wheel to select the autocannon (fitted from the start; the refits sharpen it) and hold LMB: bolts go straight down the nose, where the crosshair is (the mouse steers the ship, so you fly the nose onto the target). Q locks a raider: the ship follows it, keeping its nose on the raider while you work the gun with the mouse, and every raider within gun reach carries an amber LEAD pip marking where to put the crosshair so a bolt fired now meets it (no reticle on raiders). While raiders are attacking nothing else can be locked or picked, and a rock or cargo ship lock is dropped. The cargo ship's guns cover 4,500 m round it. Lose the hull to them and they strip a third of your hold before recovery." },
        new object[] { "Docking", "Fly slowly into either mouth of the cargo ship's through-hangar, or press E within 2,250 m of it and approach control flies you in. Once docked, E deposits all your ore into the cargo ship's storage. Fuel and repairs flow while you sit on the pad. The cargo ship warps with you; ore only sells at the Hub." },
        new object[] { new[] { "N" }, "Open the nav map. The cargo ship makes the jump between zones, so dock in its hangar first; you ride along." },
        new object[] { new[] { "Tab", "I" }, "The hangar window: a centred window with tabs. Inventory shows the hold's stacks and, while docked, the cargo ship's storage beside it with the gauges and the Hub market; Ship upgrades and Cargo ship upgrades list them. Docking opens it; Esc or Close shuts it." },
        new object[] { new[] { "C" }, "Hide or show the flight controls list in the bottom-left corner. Remembered between sessions." },
        new object[] { new[] { "F5" }, "Quick-save." },
        new object[] { new[] { "F9" }, "Testing: clear the zone's raiders and spawn three fresh ones 2,000 to 4,000 m out in random directions, mostly ahead, homed on you so they attack at once. Launching the game with -combat starts that way in a sandbox that never touches the save." },
        new object[] { new[] { "F10" }, "Testing: the raiders hold their fire (they still fly and chase); press again and they fire." },
        new object[] { new[] { "F8" }, "Testing: in the combat test F8 opens the upgrade tabs anywhere, credits are bottomless, and each upgrade row has a − button that takes a level off, so any loadout can be tried." },
        new object[] { new[] { "Space" }, "Hold to drift: the engine cuts (the throttle setting is kept for when you let go) and the ship coasts on along its momentum while the nose swings twice as fast as usual, to bring the gun round on a raider going past without losing your way. It does not brake: S at zero throttle does that. During a docking, departure, arrival or warp cutscene, Space skips to the end of it." },
        new object[] { new[] { "Esc" }, "Pause · opens this menu with Settings and Controls" },
    };

    public void Build(RectTransform root, Hud hud)
    {
        _overlay = Ui.Stretch("Menu", root);
        Ui.Fill(_overlay, Ui.OVERLAY, true);
        _card = Ui.Rect("Card", _overlay, Ui.MID, Ui.MID, Vector2.zero, new Vector2(W, 400f));
        _pane = _card.gameObject.AddComponent<Ui.Pane>();
        _pane.Card();
        _pane.raycastTarget = true;
        var f = new Ui.Flow(_card, PAD, 28f, W - 2f * PAD);
        _eyebrow = f.Para("KESSLER BELT · SECTOR 7", "display", 11, Ui.MUTED, 4f);
        f.Para("BELT RUNNER", "display_bold", 40, Ui.AMBER2, 8f);
        var tag = f.Para("One ship, one laser, an empty hold. Cut ore from the rocks, stow it aboard your cargo ship, sell it at the Hub, and refit the ship until it can afford a jump drive out.", "body", 14, Ui.MUTED, 22f);
        tag.lineSpacing = 1.2f;
        _headH = f.Used;
        BuildMain();
        BuildSettings();
        BuildControls();
        ShowPage("main");
        _overlay.gameObject.SetActive(false);
    }

    RectTransform Page(string name)
    {
        var rt = Ui.Rect("Page " + name, _card, Ui.TL, Ui.TL, new Vector2(PAD, -_headH), new Vector2(W - 2f * PAD, 10f));
        _pages[name] = rt;
        return rt;
    }

    /// .menu-btn: a wide left-aligned button with a small mono note at the right edge.
    Ui.Btn MenuButton(Ui.Flow f, string text, Action fn, bool primary, out Text info)
    {
        var b = Ui.Button(f.parent, text, fn, primary, false, f.w, 15, 16f, 13f);
        b.rt.anchoredPosition = new Vector2(f.x, f.y);
        b.label.alignment = TextAnchor.MiddleLeft;
        Ui.At(b.label.rectTransform, Ui.MID, Ui.MID, new Vector2(16f, 0f), new Vector2(b.Width - 32f, b.Height));
        info = Ui.Label(b.rt, "", "mono", 11, Ui.A(primary ? Ui.INK : Ui.TEXT, 0.8f), TextAnchor.MiddleRight);
        Ui.At(info.rectTransform, Ui.TR, Ui.TR, new Vector2(-16f, 0f), new Vector2(300f, b.Height));
        f.y -= b.Height + 10f;
        return b;
    }

    void BuildMain()
    {
        var p = Page("main");
        var f = new Ui.Flow(p, 0f, 0f, W - 2f * PAD);
        _continueBtn = MenuButton(f, "Continue", () => { if (started) { if (onResume != null) onResume(); } else if (onStart != null) onStart(); }, true, out _continueInfo);
        _newBtn = MenuButton(f, "New game", () => NewGame(), false, out _newInfo);
        Text dummy;
        MenuButton(f, "Controls", () => ShowPage("controls"), false, out dummy);
        MenuButton(f, "Settings", () => ShowPage("settings"), false, out dummy);
        f.Gap(2f);
        f.Para("Two charted zones for now: the Kessler Belt, where the ore is, and the Hub, where Meridian Colony buys all of it.", "body", 11, Ui.DIM, 6f, TextAnchor.UpperCenter);
        f.Para("Belt Runner 3D · v" + Data.VERSION, "mono", 11, Ui.DIM, 4f, TextAnchor.UpperCenter);
        var q = Ui.Link(p, "Quit to desktop", () => { if (onQuit != null) onQuit(); }, 11);
        Ui.At(q.rectTransform, Ui.TC, Ui.TC, new Vector2(0f, f.y), q.rectTransform.sizeDelta);
        f.y -= q.rectTransform.sizeDelta.y;
        _pageH["main"] = f.Used;
    }

    RectTransform SettingRow(Ui.Flow f, string label, float h)
    {
        var r = f.Box(h);
        Ui.MakeBox(r, Ui.CLEAR, Ui.LINE, 1f);
        var l = Ui.Label(r, label, "body", 13, Ui.MUTED, TextAnchor.MiddleLeft);
        Ui.At(l.rectTransform, Ui.TL, Ui.TL, new Vector2(12f, 0f), new Vector2(200f, h));
        f.Gap(10f);
        return r;
    }

    Ui.Btn Small(RectTransform row, string text, Action fn)
    {
        var b = Ui.Button(row, text, fn, false, false, 110f, 12, 14f, 7f);
        Ui.At(b.rt, Ui.TR, Ui.TR, new Vector2(-8f, -(row.sizeDelta.y - b.Height) * 0.5f), b.rt.sizeDelta);
        return b;
    }

    void VolumeRow(Ui.Flow f, string label, float min, float max, Action<float> onChange, out Slider s, out Text t)
    {
        var r = SettingRow(f, label, 44f);
        t = Ui.Label(r, "100%", "mono", 11, Ui.MUTED, TextAnchor.MiddleRight);
        Ui.At(t.rectTransform, Ui.TR, Ui.TR, new Vector2(-12f, 0f), new Vector2(40f, 44f));
        s = Ui.MakeSlider(r, min, max, true, new Vector2(0f, -14f), 150f, onChange);
        Ui.At(s.GetComponent<RectTransform>(), Ui.TR, Ui.TR, new Vector2(-12f - 40f - 10f, -14f), new Vector2(150f, 16f));
    }

    void BuildSettings()
    {
        var p = Page("settings");
        var f = new Ui.Flow(p, 0f, 0f, W - 2f * PAD);
        var sr = SettingRow(f, "Sound", 48f);
        _soundBtn = Small(sr, "On", () => ToggleSound());
        VolumeRow(f, "Sound volume", 0f, 100f, v => { _volT.text = Mathf.RoundToInt(v) + "%"; if (!_syncing && onSetting != null) onSetting("volume", v / 100f); }, out _vol, out _volT);
        var mr = SettingRow(f, "Music", 48f);
        _musicBtn = Small(mr, "On", () => ToggleMusic());
        VolumeRow(f, "Music volume", 0f, 100f, v => { _mvolT.text = Mathf.RoundToInt(v) + "%"; if (!_syncing && onSetting != null) onSetting("music_volume", v / 100f); }, out _mvol, out _mvolT);
        var dr = SettingRow(f, "Display", 48f);
        _displayBtn = Small(dr, "Borderless", () => CycleDisplay());
        VolumeRow(f, "HUD size", 70f, 160f, v => { _hudT.text = Mathf.RoundToInt(v) + "%"; if (!_syncing && onSetting != null) onSetting("hud", v / 100f); }, out _hud, out _hudT);
        var tr = SettingRow(f, "Tutorial", 48f);
        _tutBtn = Small(tr, "Run again", () => RestartTutorial());
        var wr = SettingRow(f, "Saved game", 48f);
        _wipeBtn = Small(wr, "Wipe save", () => Wipe());
        f.Gap(6f);
        var back = Ui.Button(p, "Back", () => ShowPage("main"), false, false, f.w);
        back.rt.anchoredPosition = new Vector2(0f, f.y);
        f.y -= back.Height;
        _pageH["settings"] = f.Used;
    }

    void BuildControls()
    {
        var p = Page("controls");
        float w = W - 2f * PAD;
        var scroll = Ui.Scroll.Make(p, 0f, 0f, 0f, 0f);
        scroll.viewport.anchorMin = new Vector2(0f, 1f);
        scroll.viewport.anchorMax = new Vector2(1f, 1f);
        scroll.viewport.offsetMin = new Vector2(0f, -360f);
        scroll.viewport.offsetMax = new Vector2(0f, 0f);
        var f = new Ui.Flow(scroll.content, 0f, 0f, w);
        foreach (var row in CONTROLS)
        {
            if (!State.sandbox && row.Length > 1 && row[1] is string td && td.StartsWith("Testing")) continue;   // the test keys: the combat test only
            var keys = row[0] as string[];
            float top = f.y;
            if (keys != null) Ui.Keys(scroll.content, keys, false, 0f, f.y);
            else
            {
                var k = Ui.Label(scroll.content, (string)row[0], "body", 13, Ui.MUTED);
                Ui.At(k.rectTransform, Ui.TL, Ui.TL, new Vector2(0f, f.y), new Vector2(100f, 18f));
            }
            f.Para((string)row[1], "body", 13, Ui.MUTED, 8f, TextAnchor.UpperLeft, 0f, 340f);
            var d = scroll.content.GetChild(scroll.content.childCount - 1).GetComponent<RectTransform>();
            d.anchoredPosition = new Vector2(w - 340f, top);
        }
        scroll.SetHeight(f.Used);
        var back = Ui.Button(p, "Back", () => ShowPage("main"), false, false, w);
        back.rt.anchoredPosition = new Vector2(0f, -370f);
        _pageH["controls"] = 370f + back.Height;
    }

    string _page = "";
    public void ShowPage(string name)
    {
        if (_page != "" && name != _page) Audio.Play("ui_tab");   // a page change, not the first show
        _page = name;
        foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == name);
        _card.sizeDelta = new Vector2(W, _headH + _pageH[name] + 28f);
    }

    /// Bring the menu up: at launch (started = false) or as the pause menu.
    public void Open(bool isStarted, bool saveExists)
    {
        started = isStarted;
        hasSave = saveExists;
        Render();
        _overlay.gameObject.SetActive(true);
    }

    public void Close() { _overlay.gameObject.SetActive(false); }

    void Render()
    {
        _continueBtn.rt.gameObject.SetActive(hasSave || started);
        _continueBtn.SetText(started ? "Resume" : "Continue");
        _continueInfo.text = started ? "paused" : (hasSave ? Data.Fmt(State.credits) + " cr · " + Data.ZoneById(State.zoneId).name : "");
        _newBtn.SetPrimary(!hasSave && !started);
        _newInfo.color = Ui.A(_newBtn.primary ? Ui.INK : Ui.TEXT, 0.8f);
        _newInfo.text = hasSave || started ? "wipes the save" : "";
        _newArmed = false;
        _eyebrow.text = started ? ("PAUSED · " + Data.ZoneById(State.zoneId).name).ToUpperInvariant() : "KESSLER BELT · SECTOR 7";
        // the main page keeps its height whether or not Continue shows: the buttons stay where they were laid out
        SyncSettings();
        ShowPage("main");
    }

    /// The controls show the saved settings; nothing they emit while being set this way counts as a change.
    void SyncSettings()
    {
        _syncing = true;
        _soundBtn.SetText(State.soundOn ? "On" : "Off");
        _musicBtn.SetText(State.musicOn ? "On" : "Off");
        _displayBtn.SetText(DisplayName(State.display));
        _mvol.value = Mathf.RoundToInt(State.musicVolume * 100f);
        _mvolT.text = Mathf.RoundToInt(State.musicVolume * 100f) + "%";
        _vol.value = Mathf.RoundToInt(State.volume * 100f);
        _volT.text = Mathf.RoundToInt(State.volume * 100f) + "%";
        _hud.value = Mathf.RoundToInt(State.hudScale * 100f);
        _hudT.text = Mathf.RoundToInt(State.hudScale * 100f) + "%";
        _syncing = false;
        _tutBtn.SetText("Run again");
        _wipeBtn.SetText("Wipe save");
        _wipeArmed = false;
    }

    static string DisplayName(int d) { return d == 0 ? "Full screen" : d == 1 ? "Borderless" : "Windowed"; }

    /// Display: Full screen → Borderless → Windowed → Full screen.
    void CycleDisplay()
    {
        int d = (State.display + 1) % 3;
        if (onSetting != null) onSetting("display", d);
        _displayBtn.SetText(DisplayName(d));
    }

    void ToggleMusic()
    {
        bool on = !State.musicOn;
        if (onSetting != null) onSetting("music", on ? 1f : 0f);
        _musicBtn.SetText(on ? "On" : "Off");
    }

    void ToggleSound()
    {
        bool on = !State.soundOn;
        if (onSetting != null) onSetting("sound", on ? 1f : 0f);
        _soundBtn.SetText(on ? "On" : "Off");
    }

    void RestartTutorial()
    {
        if (onTutorialRestart != null) onTutorialRestart();
        _tutBtn.SetText("Restarted");
        _tutT = 2.5f;
    }

    void Wipe()
    {
        if (_wipeArmed)
        {
            _wipeArmed = false;
            if (onWipe != null) onWipe();
            return;
        }
        _wipeArmed = true;
        _wipeT = 4f;
        _wipeBtn.SetText("Click again");
    }

    void NewGame()
    {
        if (!hasSave && !started)
        {
            if (onStart != null) onStart();
            return;
        }
        if (!_newArmed)
        {
            _newArmed = true;
            _newT = 4f;
            _newInfo.text = "click again to wipe the save";
            return;
        }
        _newArmed = false;
        if (onNewGame != null) onNewGame();
    }

    /// The armed buttons disarm themselves after a few seconds.
    public void Tick(float dt)
    {
        if (!Visible) return;
        if (_newArmed) { _newT -= dt; if (_newT <= 0f) { _newArmed = false; _newInfo.text = "wipes the save"; } }
        if (_wipeArmed) { _wipeT -= dt; if (_wipeT <= 0f) { _wipeArmed = false; _wipeBtn.SetText("Wipe save"); } }
        if (_tutT > 0f) { _tutT -= dt; if (_tutT <= 0f) _tutBtn.SetText("Run again"); }
    }
}
