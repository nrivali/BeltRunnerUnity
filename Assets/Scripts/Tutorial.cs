using UnityEngine;

/// Vega's questline (Vega is the ship's onboard assistant, voiced by an ElevenLabs voice; see tools/gen-voice.ps1),
/// revamped 2026-09-28 (the user's: "revamp the tutorial") for the ship as it is: fifteen short steps that teach the
/// controls and the ideas, nothing more: the launch, the stick with the burner and the drift, the cockpit, the HUD and
/// the scope, the radar, cutting a copper rock on its weak spot, the three weapons, raiders and the lock, flares against
/// seekers, docking, stowing, the services and the Hub, the Front's staged battle, and the departure. Every step is
/// spoken by a recorded line (Sfx/tut_<id>) when it appears, and again from the card's Replay button; a step whose
/// line is not recorded yet is silent. Steps with `wait` wait for the pilot to actually do the thing; the rest wait for
/// Next (Enter). Progress is State.tut (-1 once finished or skipped).
public class Tutorial
{
    public class Step
    {
        public string id, title, text, wait, ring, ring2;   // ring: the HUD piece the card points at (Hud._ringTargets)
        public bool final;
        public Step(string id, string title, string text, string wait = null, bool final = false, string ring = null, string ring2 = null) { this.id = id; this.title = title; this.text = text; this.wait = wait; this.final = final; this.ring = ring; this.ring2 = ring2; }
        public bool Auto { get { return wait != null && !final; } }
    }

    public static readonly Step[] STEPS =
    {
        new Step("launch", "Welcome aboard", "Vega here. Press Enter on the pad. Approach control taxis you out; the ship is yours when it lets go.", "press Enter to launch"),
        new Step("stick", "Take the stick", "The mouse aims. W and S work the throttle, A and D roll, Shift lights the afterburner, and Space cuts the engine so you drift and swing the nose. Open up and give me a turn.", "open the throttle and turn", false, "controls"),
        new Step("cockpit", "The cockpit", "Press P for the cockpit. The three screens carry the ship, the flight and the target; hold the right mouse button to look round. P again for the chase camera.", "press P"),
        new Step("band", "The HUD", "The band along the bottom: hull, shield, fuel and hold on the left; then speed and the way home; the target you are looking at; the weapon in hand. The scope at the right shows what is round you.", null, false, "status", "target"),
        new Step("pulse", "Find ore", "Press R to pulse the radar. Ore rocks light up with their range for a while; coloured veins mean ore, grey rock is barren, so skip it.", "press R", false, "readout"),
        new Step("cut", "Cut a rock", "Fly at a copper rock, orange veins, and hold the left mouse button with the crosshair on it. The beam cuts where you aim it; hold it on the glowing seam, the weak spot, and the rock cracks. Its ore comes to you.", "collect copper", false, "target"),
        new Step("guns", "Weapons", "2 is the autocannon, 3 the seeker rockets, 1 the mining laser; the wheel cycles them. The cannon overheats if you hold it, so let it cool. Try one.", "press 2 or 3", false, "weapon"),
        new Step("raiders", "Raiders", "Raiders come for you in the belt: red diamonds on the HUD and the scope. Put the crosshair on one and fire; Z locks it and steers your nose onto its lead. Your shield soaks hits and is back five seconds after the last.", null, false, "status"),
        new Step("flares", "Seekers", "Seeker rockets home on you. Press B to drop a flare and they chase it instead; a hard turn at the last moment throws one too. Drop one now.", "press B"),
        new Step("return", "Head home", "Follow the CARGO SHIP readout. Within " + Data.Fm(Data.DOCK_RANGE) + " press H and approach control brings you in.", "dock with the cargo ship", false, "marker"),
        new Step("stow", "Stow the haul", "Press E to move your ore into the cargo ship storage. The pad refuels you and mends the hull while you sit on it.", "move copper into storage", false, "deposit"),
        new Step("services", "The services", "On the pad, F opens the services: the workshop turns ore into parts, flares and rockets, and the refits sharpen the ship. Nothing sells out here; press N and warp to the Hub for that.", null, false, "navmap"),
        new Step("front", "The Front", "The Front is a battlefield: two battleships six kilometres apart. The fight starts in the middle; kills push the meter, and the side that fills it attacks the other ship until it is pushed back. Five pushes, or the core, take a ship.", null),
        new Step("depart", "Back out", "Press Depart, or Enter on the pad, to launch.", "press Depart", false, "depart"),
        new Step("done", "Tutorial complete", "That is the loop: fill the hold, stow it, sell at the Hub, upgrade. Vega out.", null, true),
    };

    public Game game;
    public Ship ship;
    public Hud hud;
    bool _flown;
    Vector3? _head0;
    bool? _cockpit0;   // the view when the cockpit step began: the deed is a toggle either way
    int _flares0 = -1; // the flares aboard when the flare step began
    int _shown = -1;

    public bool Active { get { return State.tut >= 0; } }
    public int StepIndex { get { return State.tut; } }
    /// Enter is this card's Next: the ship must not take it as Depart (the steps that wait on a deed leave Enter alone).
    public bool WantsEnter { get { return Active && !STEPS[Mathf.Clamp(State.tut, 0, STEPS.Length - 1)].Auto; } }

    bool AutoDone(Step s)
    {
        switch (s.id)
        {
            case "launch": return !ship.docked && ship.cut == null;
            case "stick": return _flown;
            case "cockpit": return _cockpit0 != null && State.cockpitView != _cockpit0.Value;
            case "pulse": return ship.radarPulsed;
            case "cut": return State.cargo["copper"] >= 1f;
            case "guns": return ship.weapon != "laser";
            case "flares": return _flares0 >= 0 && State.FlaresAboard < _flares0;
            case "return": return ship.docked;
            case "stow": return State.store["copper"] >= 1f;
            case "depart": return !ship.docked && ship.cut == null;
        }
        return false;
    }

    public void Update(float dt)
    {
        if (!Active)
        {
            if (_shown != -1)
            {
                _shown = -1;
                hud.ShowTutorial(null, 0);
            }
            return;
        }
        int i = Mathf.Clamp(State.tut, 0, STEPS.Length - 1);
        var s = STEPS[i];
        if (_shown != i)
        {
            _shown = i;
            hud.ShowTutorial(s, i + 1);
            Debug.Log("tutorial: show " + s.id);
            Speak();
        }
        // the steering step wants a real turn under power: the heading has to swing 60 degrees from where the step began while moving
        if (s.id == "stick" && !_flown)
        {
            var f = ship.Forward;
            if (_head0 == null) _head0 = f;
            else if (ship.Speed > 150f && Vector3.Dot(f, _head0.Value) < 0.5f) _flown = true;
        }
        // the cockpit and flare steps remember where things stood when they began, so the deed is a change from there
        if (s.id == "cockpit" && _cockpit0 == null) _cockpit0 = State.cockpitView;
        if (s.id == "flares" && _flares0 < 0) _flares0 = State.FlaresAboard;
        // the card stands aside for cutscenes and the map (the departure taxi is not a cutscene: the launch line plays over it)
        hud.TutorialHidden(ship.InCinematic || hud.MapOpen);
        if (s.Auto && AutoDone(s)) Advance();
        else if (Input.GetKeyDown(KeyCode.Return) && !s.Auto) Advance();
    }

    public void Speak()
    {
        if (Active) Audio.Say("tut_" + STEPS[Mathf.Clamp(State.tut, 0, STEPS.Length - 1)].id);
    }

    public void Advance()
    {
        if (!Active) return;
        var s = STEPS[Mathf.Clamp(State.tut, 0, STEPS.Length - 1)];
        if (s.final)
        {
            State.tut = -1;
            Audio.Hush();
            Audio.Play("chime");
            hud.Toast("Tutorial complete", false);
            State.Save();
            return;
        }
        State.tut += 1;
        Reset();
        State.Save();
    }

    void Reset()
    {
        _flown = false;
        _head0 = null;
        _cockpit0 = null;
        _flares0 = -1;
        ship.radarPulsed = false;
    }

    public void Skip()
    {
        State.tut = -1;
        Audio.Hush();
        hud.Toast("Tutorial skipped", false);
        State.Save();
    }

    public void Restart()
    {
        State.tut = 0;
        _shown = -1;
        Reset();
        State.Save();
    }
}
