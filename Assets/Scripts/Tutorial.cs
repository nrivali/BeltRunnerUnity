using UnityEngine;

/// Vega's questline (Vega is the ship's onboard assistant, voiced by an ElevenLabs voice; see tools/gen-voice.ps1):
/// thirteen short steps that teach the controls and the basic ideas, nothing more: the launch, the stick, the HUD,
/// the radar, a copper rock locked with Q, cutting it, the three weapons, what a fight is, docking, stowing, the Hub
/// and the departure. Every step is spoken by a recorded line (Sfx/tut_<id>) when it appears, and again from the
/// card's Replay button. Steps with `wait` wait for the pilot to actually do the thing; the rest wait for Next
/// (Enter). Progress is State.tut (-1 once finished or skipped).
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
        new Step("launch", "Welcome aboard", "Vega here. Press W on the pad. Approach control taxis you out; the ship is yours when it lets go.", "press W to launch"),
        new Step("steer", "Take the stick", "The mouse steers. W and S work the throttle, A and D roll, X cuts the throttle. Open up and give me a turn.", "open the throttle and turn", false, "controls"),
        new Step("hud", "The HUD", "The band along the bottom: SHIP is hull, shield, fuel and hold. FLIGHT is speed and the way home. TARGET is what you are looking at. WEAPON is the one in hand.", null, false, "status", "target"),
        new Step("radar", "Find ore", "Press R to pulse the radar. Coloured veins mean ore. Grey rock is barren, so skip it.", "press R", false, "readout"),
        new Step("lock", "Lock a rock", "Put the mouse on a copper rock, orange veins, and press Q to lock it.", "press Q on a copper rock", false, "target"),
        new Step("mine", "Cut it", "Get within laser reach and hold the left mouse button. When the rock breaks, fly through the glow to collect.", "collect copper", false, "target"),
        new Step("weapons", "Weapons", "2 is the autocannon, 3 the seeker rockets, 1 the mining laser. The wheel cycles them. Try one.", "press 2 or 3", false, "weapon"),
        new Step("combat", "Raiders", "Raiders hold the rich pockets. Lock one with Q and fire; a rocket chases it on its own. Your shield soaks hits and recharges once they stop.", null, false, "status"),
        new Step("return", "Head home", "Follow the CARGO SHIP readout. Within 2,250 press E and approach control brings you in.", "dock with the cargo ship", false, "marker"),
        new Step("stow", "Stow the haul", "Press E to move your ore into the cargo ship storage. The pad refuels you and mends the hull while you sit on it.", "move copper into storage", false, "deposit"),
        new Step("hub", "Selling", "Nothing sells out here. Press N and warp to the Hub to sell and upgrade.", null, false, "navmap"),
        new Step("depart", "Back out", "Press Depart, or W on the pad, to launch.", "press Depart", false, "depart"),
        new Step("done", "Tutorial complete", "That is the loop: fill the hold, stow it, sell at the Hub, upgrade. Vega out.", null, true),
    };

    public Game game;
    public Ship ship;
    public Hud hud;
    bool _flown;
    Vector3? _head0;
    int _shown = -1;

    public bool Active { get { return State.tut >= 0; } }
    public int StepIndex { get { return State.tut; } }

    bool AutoDone(Step s)
    {
        var belt = game.belt;
        switch (s.id)
        {
            case "launch": return !ship.docked && ship.cut == null;
            case "steer": return _flown;
            case "radar": return ship.radarPulsed;
            case "lock": return ship.lockKind == "rock" && ship.lockRock >= 0 && ship.lockRock < belt.count && belt.ore[ship.lockRock] >= 0 && Data.ORE_KEYS[belt.ore[ship.lockRock]] == "copper";
            case "mine": return State.cargo["copper"] >= 1f;
            case "weapons": return ship.weapon != "laser";
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
        if (s.id == "steer" && !_flown)
        {
            var f = ship.Forward;
            if (_head0 == null) _head0 = f;
            else if (ship.Speed > 150f && Vector3.Dot(f, _head0.Value) < 0.5f) _flown = true;
        }
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
        _flown = false;
        _head0 = null;
        ship.radarPulsed = false;
        State.Save();
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
        _flown = false;
        _head0 = null;
        State.Save();
    }
}
