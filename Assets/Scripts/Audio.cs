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
        { "rock_break", -3f }, { "hit", -3f }, { "shield_down", -2f }, { "shield_up", -3f }, { "blaster", -4f }, { "laser_on", -6f }, { "laser_off", -8f }, { "laser_bite", -7f }, { "radar_ping", -6f }, { "warp_charge", -4f }, { "warp_jump", -2f },
    };
    static readonly string[] LOOP_NAMES = { "engine_idle", "engine_thrust", "engine_boost", "retro", "laser_beam", "laser_cut", "space_hum", "shield_out", "shield_charge" };

    public static Audio I;

    // loops whose clip gets a seamless seam once its audio data is decoded (the mp3s carry encoder padding, which clicks)
    static readonly string[] SEAMLESS = { "shield_out", "shield_charge" };
    readonly List<string> _pendingSeam = new List<string>();

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
            if (System.Array.IndexOf(SEAMLESS, nm) >= 0) { c.LoadAudioData(); _pendingSeam.Add(nm); }
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
        if (c == null && name == "shield_down") c = ShieldDownClip();   // the recorded clip is the one that plays; the synthesized one is the fallback
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
        for (int i = _pendingSeam.Count - 1; i >= 0; i--)
        {
            var nm = _pendingSeam[i];
            var l = _loops[nm];
            var st = l.src.clip.loadState;
            if (st == AudioDataLoadState.Loading || st == AudioDataLoadState.Unloaded) continue;
            _pendingSeam.RemoveAt(i);
            if (st != AudioDataLoadState.Loaded) continue;
            var seamed = Seamless(l.src.clip, 0.35f);
            if (seamed == null) continue;
            float vol = l.src.volume;
            l.src.Stop();
            l.src.clip = seamed;
            l.src.volume = vol;
            l.src.Play();
        }
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

    /// A copy of a clip that loops without a click: the silence the encoder padded on at either end is trimmed, and the
    /// last `fade` seconds are blended (equal power) under the first `fade` seconds and dropped, so the end runs straight
    /// into the start.
    static AudioClip Seamless(AudioClip c, float fade)
    {
        int ch = c.channels, n = c.samples, rate = c.frequency;
        if (n < rate / 2) return null;
        var d = new float[n * ch];
        if (!c.GetData(d, 0)) return null;
        // trim: the first and last frames that carry anything
        const float floor = 0.004f;
        int a = 0, b = n - 1;
        while (a < n) { bool any = false; for (int k = 0; k < ch; k++) if (Mathf.Abs(d[a * ch + k]) > floor) { any = true; break; } if (any) break; a++; }
        while (b > a) { bool any = false; for (int k = 0; k < ch; k++) if (Mathf.Abs(d[b * ch + k]) > floor) { any = true; break; } if (any) break; b--; }
        int len = b - a + 1;
        int L = Mathf.Min(Mathf.RoundToInt(rate * fade), len / 3);
        if (L < 16) return null;
        int outLen = len - L;
        var o = new float[outLen * ch];
        for (int i = 0; i < outLen; i++)
        {
            for (int k = 0; k < ch; k++)
            {
                float v = d[(a + i) * ch + k];
                if (i < L)
                {
                    float t = (i + 0.5f) / L;
                    float wIn = Mathf.Sin(t * Mathf.PI * 0.5f), wOut = Mathf.Cos(t * Mathf.PI * 0.5f);
                    v = v * wIn + d[(a + outLen + i) * ch + k] * wOut;   // the tail fades out under the head fading in
                }
                o[i * ch + k] = v;
            }
        }
        var s = AudioClip.Create(c.name + "_seamless", outLen, ch, rate, false);
        s.SetData(o, 0);
        return s;
    }

    /// The shield beds: the shield-down loop while it sits at zero, the recharge loop while it climbs (a hit restarts the
    /// ten-second wait, so the loop stops at once).
    public void ShieldLoop(bool down, bool charging)
    {
        LoopTarget("shield_out", down ? 0.22f : 0f, down ? 0.15f : 0.4f);
        LoopTarget("shield_charge", charging ? 0.25f : 0f, charging ? 0.12f : 0.04f);   // cut fast: a hit ends it
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

    /// The shield going down, synthesized: a tone that falls from 1,100 Hz to 180 Hz over 0.7 s with a crackle of
    /// noise over the first third, a soft knee at the start and a decay to nothing.
    static AudioClip ShieldDownClip()
    {
        const int rate = 22050;
        const float dur = 0.7f;
        int n = Mathf.RoundToInt(rate * dur);
        var d = new float[n];
        var rng = new System.Random(7);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(1100f, 180f, t * t);
            phase += f / rate * Mathf.PI * 2f;
            float tone = Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2.01f) * 0.2f;
            float crackle = t < 0.35f ? ((float)rng.NextDouble() * 2f - 1f) * (0.35f - t) * 1.4f : 0f;
            float env = Mathf.Min(1f, i / (rate * 0.01f)) * (1f - t) * (1f - t);
            d[i] = Mathf.Clamp((tone + crackle) * env * 0.8f, -1f, 1f);
        }
        var c = AudioClip.Create("shield_down", n, 1, rate, false);
        c.SetData(d, 0);
        return c;
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
        // Vega (the tutorial lines) is aboard the ship, so she speaks clean and at once; everyone else is on the radio
        // and gets the squelch before and after
        bool radio = !name.StartsWith("tut_");
        _voiceRadio = radio;
        if (radio) Squelch("radio_on");
        _voice.clip = c;
        _voice.volume = Lin(-2f);
        _voice.PlayDelayed(radio ? RADIO_LEAD : 0.05f);
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
    /// A gun shot: its own source, restarted on every shot so the sound lands on the shot however fast the gun fires.
    public static void Shot(string name, float extraDb = 0f) { if (I != null) I.GunShot(name, extraDb); }

    AudioSource _gun;
    AudioClip _gunRaw, _gunClip;
    void GunShot(string name, float extraDb)
    {
        var raw = Clip(name);
        if (raw == null) return;
        if (_gun == null) _gun = Src("Gun");
        if (raw != _gunRaw)
        {
            _gunRaw = raw;
            _gunClip = null;
            raw.LoadAudioData();
        }
        // the trimmed copy, once the data is in: an mp3 opens on a run of encoder silence, which is the lag
        if (_gunClip == null && raw.loadState == AudioDataLoadState.Loaded) _gunClip = TrimHead(raw, name == "blaster" ? 0.2f : 0f);
        float db;
        if (!GAIN_DB.TryGetValue(name, out db)) db = -5f;
        _gun.Stop();
        _gun.clip = _gunClip != null ? _gunClip : raw;
        _gun.volume = Lin(db + extraDb);
        _gun.pitch = 1f;
        _gun.Play();
        Count(name);
    }

    /// A copy of a clip without the silence at its head (and tail): `cutSec` off the head first, then the first and last frames that carry anything.
    static AudioClip TrimHead(AudioClip c, float cutSec = 0f)
    {
        int ch = c.channels, n = c.samples, rate = c.frequency;
        var d = new float[n * ch];
        if (!c.GetData(d, 0)) return null;
        const float floor = 0.01f;
        int a = Mathf.Min(n - 1, Mathf.RoundToInt(cutSec * rate)), b = n - 1;   // a fixed cut first (the blaster: 200 ms of build-up), then the silence
        while (a < n) { bool any = false; for (int k = 0; k < ch; k++) if (Mathf.Abs(d[a * ch + k]) > floor) { any = true; break; } if (any) break; a++; }
        while (b > a) { bool any = false; for (int k = 0; k < ch; k++) if (Mathf.Abs(d[b * ch + k]) > floor) { any = true; break; } if (any) break; b--; }
        int len = b - a + 1;
        if (len < 64) return null;
        var o = new float[len * ch];
        System.Array.Copy(d, a * ch, o, 0, len * ch);
        // a two-millisecond fade in so the cut does not click
        int f = Mathf.Min(len, rate / 500);
        for (int i = 0; i < f; i++) for (int k = 0; k < ch; k++) o[i * ch + k] *= (i + 1f) / f;
        var s = AudioClip.Create(c.name + "_trim", len, ch, rate, false);
        s.SetData(o, 0);
        Debug.Log("audio: " + c.name + " trimmed · " + (a * 1000f / rate).ToString("0") + " ms of silence off the head, " + (len * 1000f / rate).ToString("0") + " ms kept of " + (n * 1000f / rate).ToString("0"));
        return s;
    }
    public static void Say(string name) { if (I != null) I.Voice(name); }
    public static void Announce(string name) { if (I != null) I.Intercom(name); }
    public static void Hush() { if (I != null) I.StopVoice(); }
}
