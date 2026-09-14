using System.Collections.Generic;
using UnityEngine;

/// Recorded audio, ported from the browser game's SFX module (via the Godot port): every clip is an mp3 under
/// Resources/Sfx (the ElevenLabs recordings). Voice lines are radio transmissions: the key clicks open with a burst
/// of static (radio_on), the line follows a beat later, and the squelch closes it when it ends or is cut off by the
/// next one; one voice at a time. The hangar deck's announcements go through the intercom source instead: a tannoy
/// band-pass, a little overdrive and the reverb of a big steel room, with the PA chime before each. One-shots play
/// from a small pool. The continuous layers (engine idle, thrust, boost, retros, the laser beam and cut, the space
/// hum) each run on their own looping source and are faded toward a target every frame. Missing clips are silent.
public class Audio : MonoBehaviour
{
    public const float RADIO_LEAD = 0.32f;
    public const float PA_LEAD = 1.1f;
    static readonly Dictionary<string, float> GAIN_DB = new Dictionary<string, float>
    {
        { "radio_on", -6f }, { "radio_off", -7f }, { "pa_chime", -6f }, { "dock", -3f }, { "chime", -6f }, { "cash", -4f }, { "stow", -4f }, { "pickup", -7f },
        { "rock_break", -3f }, { "hit", -3f }, { "laser_on", -6f }, { "laser_off", -8f }, { "laser_bite", -7f }, { "radar_ping", -6f }, { "warp_charge", -4f }, { "warp_jump", -2f },
    };
    static readonly string[] LOOP_NAMES = { "engine_idle", "engine_thrust", "engine_boost", "retro", "laser_beam", "laser_cut", "space_hum" };

    public static Audio I;

    class Loop
    {
        public AudioSource src;
        public float gain, target, tau = 0.5f;
    }

    readonly Dictionary<string, Loop> _loops = new Dictionary<string, Loop>();
    readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    readonly List<AudioSource> _pool = new List<AudioSource>();
    AudioSource _voice, _intercom, _pa, _squelch;
    bool _voiceRadio, _voiceWasPlaying;
    bool _laserOn, _laserCutting;
    public readonly Dictionary<string, int> plays = new Dictionary<string, int>();

    public static Audio Create()
    {
        var go = new GameObject("Audio");
        var a = go.AddComponent<Audio>();
        I = a;
        a.Setup();
        return a;
    }

    void Setup()
    {
        _voice = Src("Voice");
        _squelch = Src("Squelch");
        _pa = Src("PA");
        // the intercom chain: a tannoy band-pass, a little overdrive, and the reverb of a big steel room
        var ig = new GameObject("Intercom");
        ig.transform.SetParent(transform, false);
        _intercom = ig.AddComponent<AudioSource>();
        _intercom.playOnAwake = false;
        var hp = ig.AddComponent<AudioHighPassFilter>();
        hp.cutoffFrequency = 380f;
        var lp = ig.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = 3800f;
        var dist = ig.AddComponent<AudioDistortionFilter>();
        dist.distortionLevel = 0.22f;
        var rev = ig.AddComponent<AudioReverbFilter>();
        rev.reverbPreset = AudioReverbPreset.Hangar;
        rev.dryLevel = -4f;
        rev.room = -600f;
        for (int i = 0; i < 8; i++) _pool.Add(Src("Pool" + i));
        foreach (var nm in LOOP_NAMES)
        {
            var c = Clip(nm);
            if (c == null) continue;
            var s = Src("Loop " + nm);
            s.clip = c;
            s.loop = true;
            s.volume = 0f;
            s.Play();
            _loops[nm] = new Loop { src = s, gain = 0f, target = nm == "space_hum" ? 0.07f : 0f, tau = 0.5f };
        }
        ApplySettings();
    }

    AudioSource Src(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    /// The menu's sound switch and volume.
    public void ApplySettings()
    {
        AudioListener.volume = State.soundOn ? Mathf.Clamp01(State.volume) : 0f;
    }

    AudioClip Clip(string name)
    {
        AudioClip c;
        if (_clips.TryGetValue(name, out c)) return c;
        c = Resources.Load<AudioClip>("Sfx/" + name);
        _clips[name] = c;
        return c;
    }

    static float Lin(float db)
    {
        return Mathf.Pow(10f, db / 20f);
    }

    void Count(string name)
    {
        int n;
        plays.TryGetValue(name, out n);
        plays[name] = n + 1;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        foreach (var kv in _loops)
        {
            var l = kv.Value;
            l.gain += (l.target - l.gain) * (1f - Mathf.Exp(-dt / Mathf.Max(0.02f, l.tau)));
            l.src.volume = l.gain > 0.001f ? l.gain : 0f;
        }
        // the squelch closes when a radio line ends on its own
        bool playing = _voice.isPlaying;
        if (_voiceWasPlaying && !playing && _voiceRadio) Squelch("radio_off");
        _voiceWasPlaying = playing;
    }

    void LoopTarget(string name, float g, float tau)
    {
        Loop l;
        if (_loops.TryGetValue(name, out l)) { l.target = g; l.tau = tau; }
    }

    /// The engine mix for this frame (the HTML's SFX.engine): throttle 0..1, afterburner on, retros firing, or parked/idle.
    public void Engine(float throttle, bool boost, bool braking, bool idle)
    {
        float th = idle ? 0f : throttle;
        LoopTarget("engine_idle", idle ? 0.05f : 0.14f, 0.2f);
        LoopTarget("engine_thrust", th * 0.5f, 0.15f);
        LoopTarget("engine_boost", boost && !idle ? 0.55f : 0f, 0.2f);
        LoopTarget("retro", braking ? 0.3f : 0f, 0.06f);
        Loop t;
        if (_loops.TryGetValue("engine_thrust", out t)) t.src.pitch = Mathf.Lerp(t.src.pitch, 0.85f + th * 0.3f, 0.1f);
    }

    /// The laser mix (the HTML's SFX.laser): the beam loop while firing, the cut loop on a rock, and the on/off/bite transients.
    public void Laser(bool firing, bool cutting)
    {
        if (firing && !_laserOn) Sfx("laser_on");
        if (!firing && _laserOn) Sfx("laser_off");
        if (cutting && !_laserCutting) Sfx("laser_bite");
        _laserOn = firing;
        _laserCutting = cutting;
        LoopTarget("laser_beam", firing ? (cutting ? 0.16f : 0.26f) : 0f, firing ? 0.12f : 0.06f);
        LoopTarget("laser_cut", cutting ? 0.32f : 0f, 0.1f);
    }

    /// Current loop gains, for the smoke run.
    public string LoopState()
    {
        var parts = new List<string>();
        foreach (var kv in _loops) parts.Add(kv.Key + "=" + kv.Value.gain.ToString("0.000"));
        return string.Join(" ", parts.ToArray());
    }

    /// A one-shot from the pool.
    public void Sfx(string name, float extraDb = 0f)
    {
        var c = Clip(name);
        if (c == null) return;
        float db;
        if (!GAIN_DB.TryGetValue(name, out db)) db = -5f;
        foreach (var p in _pool)
        {
            if (p.isPlaying) continue;
            p.clip = c;
            p.volume = Lin(db + extraDb);
            p.pitch = 1f;
            p.Play();
            Count(name);
            return;
        }
    }

    /// A radio call: squelch open, the line a beat later, squelch closed when it ends. Cuts off any line already playing.
    public string lastVoice = "";

    public void Voice(string name)
    {
        lastVoice = name;
        Debug.Log("audio: voice " + name);
        var c = Clip(name);
        if (c == null) { Debug.LogWarning("audio: no clip for " + name); return; }
        StopVoice();
        _voiceRadio = true;
        Squelch("radio_on");
        _voice.clip = c;
        _voice.volume = Lin(-2f);
        _voice.PlayDelayed(RADIO_LEAD);
        _voiceWasPlaying = false;
        Count(name);
    }

    /// A deck announcement over the intercom: the PA chime, then the line through the tannoy chain, no squelch.
    public void Intercom(string name)
    {
        var c = Clip(name);
        if (c == null) return;
        StopVoice();
        _voiceRadio = false;
        var chime = Clip("pa_chime");
        if (chime != null)
        {
            _pa.clip = chime;
            _pa.volume = Lin(GAIN_DB["pa_chime"]);
            _pa.Play();
        }
        _intercom.clip = c;
        _intercom.volume = Lin(2f);
        _intercom.PlayDelayed(PA_LEAD);
        Count(name);
    }

    public void StopVoice()
    {
        if (_voice.isPlaying)
        {
            _voice.Stop();
            if (_voiceRadio) Squelch("radio_off");
        }
        _voice.clip = null;
        if (_intercom.isPlaying) _intercom.Stop();
        _voiceWasPlaying = false;
    }

    public bool VoicePlaying { get { return _voice.isPlaying || _intercom.isPlaying; } }

    void Squelch(string name)
    {
        var c = Clip(name);
        if (c == null) return;
        _squelch.clip = c;
        float db;
        if (!GAIN_DB.TryGetValue(name, out db)) db = -6f;
        _squelch.volume = Lin(db);
        _squelch.Play();
        Count(name);
    }

    // ---- static conveniences
    public static void Play(string name, float extraDb = 0f) { if (I != null) I.Sfx(name, extraDb); }
    public static void Say(string name) { if (I != null) I.Voice(name); }
    public static void Announce(string name) { if (I != null) I.Intercom(name); }
    public static void Hush() { if (I != null) I.StopVoice(); }
}
