using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// The soundtrack: the browser game's procedural synth engine (MUSIC_PROC in belt-runner-3d.html), ported note for
/// note by way of the Godot port's music.gd. Eight tracks, each a pad colour (five chord voices of detuned oscillators
/// through one slowly breathing lowpass), a sub, an echo whose time follows the tempo, ambient layers (sparkle, wind,
/// a wandering melody, a pulse, a choir swell) and a groove (kick, snare or clap, rim, hats, shaker, bass, arp, chord
/// stabs, a lead). It drifts in ambient mode for two or three minutes, brings the groove on for a minute or so, then
/// fades it and moves to the next track. Everything is rendered sample by sample at 22,050 Hz on its own thread into
/// a ring buffer, which OnAudioFilterRead drains at the mixer's rate, so the timing is sample-accurate as Web Audio's
/// scheduling was; the pads are resampled from loops rendered once per recipe, the noise hits rendered once per recipe.
public class Music : MonoBehaviour
{
    public static Music I;
    public Action<string> onGroove, onTrack, onCombat;
    /// Set by the main thread every frame: raiders are attacking. The render thread cuts to the combat track on the
    /// next step and drifts back six seconds after it clears.
    public volatile bool combat;

    public const int RATE = 22050;
    public const int CHUNK = 256;
    const int TABLE = 2048;
    public const float OUT_GAIN = 0.55f;
    const float PAD_REF = 110f;
    const float PAD_LOOP = 4f;

    class Osc { public string wave; public float cents, gain, mult; public Osc(string w, float c, float g, float m) { wave = w; cents = c; gain = g; mult = m; } }
    static readonly Dictionary<string, Osc[]> PADS = new Dictionary<string, Osc[]>
    {
        { "warm", new[] { new Osc("triangle", 0f, 0.6f, 1f), new Osc("sawtooth", 6f, 0.09f, 1f), new Osc("sawtooth", -6f, 0.09f, 1f) } },
        { "glass", new[] { new Osc("sine", 0f, 0.5f, 1f), new Osc("sine", 3f, 0.28f, 2f), new Osc("sine", -4f, 0.14f, 4f), new Osc("triangle", 0f, 0.08f, 1f) } },
        { "strings", new[] { new Osc("sawtooth", 14f, 0.16f, 1f), new Osc("sawtooth", -14f, 0.16f, 1f), new Osc("sawtooth", 0f, 0.12f, 1f), new Osc("triangle", 0f, 0.22f, 1f) } },
        { "organ", new[] { new Osc("square", 0f, 0.14f, 1f), new Osc("sine", 0f, 0.42f, 1f), new Osc("sine", 0f, 0.2f, 2f), new Osc("sine", 0f, 0.12f, 3f) } },
        { "hollow", new[] { new Osc("triangle", 0f, 0.5f, 1f), new Osc("triangle", 8f, 0.26f, 2f), new Osc("square", 0f, 0.05f, 0.5f) } },
    };
    static readonly int[] MAJ = { 0, 2, 4, 7, 9, 12, 14, 16 };
    static readonly int[] DOR = { 0, 2, 3, 5, 7, 9, 10, 12, 14 };
    static readonly int[] MINP = { 0, 3, 5, 7, 10, 12, 15, 17 };
    static readonly int[] LYD = { 0, 2, 4, 6, 7, 9, 11, 12, 14 };
    static readonly int[] AEO = { 0, 2, 3, 5, 7, 8, 10, 12, 14 };

    public static float Hz(float m) { return 440f * Mathf.Pow(2f, (m - 69f) / 12f); }

    class Chord { public float root; public int rootMidi; public float[] notes; }
    static Chord Ch(int root, params int[] n)
    {
        var c = new Chord { root = Hz(root), rootMidi = root, notes = new float[n.Length] };
        for (int i = 0; i < n.Length; i++) c.notes[i] = Hz(n[i]);
        return c;
    }

    class Ambient { public float sparkle = -1f, wind = -1f, melody = -1f; public string sparkleWave = "sine"; public bool pulse, choir; }
    class Groove
    {
        public int[] kick = new int[0], snare = new int[0], rim, openHat, stab, arp;
        public bool fill, clap, shaker, brush, pluck, lead;
        public string hat = "off", bassWave = "sine", arpWave = "sine", stabWave = "sine";
        public float arpVol;
        public float[][] bass = new float[0][];
    }
    class Track
    {
        public string name, pad, subWave, melodyWave;
        public int bpm, echo;
        public float padCut, lfo;
        public Ambient ambient;
        public int[] scale;
        public Chord[] chords;
        public Groove groove;
    }

    static float[][] B(params float[][] rows) { return rows; }
    static float[] Bp(float step, float mult) { return new[] { step, mult }; }

    // the combat track: fast, minor, a kick on every beat, a driving sawtooth bass, square arps, stabs and a lead (the
    // sub, the stabs and the lead are sine and triangle: a sawtooth held under everything was a buzz)
    static readonly Track COMBAT = new Track { name = "Vanguard", bpm = 156, pad = "strings", padCut = 520f, lfo = 0.14f, subWave = "sine", echo = 2, ambient = new Ambient(), scale = AEO, melodyWave = "triangle",
        chords = new[] { Ch(33, 45, 48, 52, 55, 60), Ch(31, 43, 46, 50, 53, 58), Ch(36, 48, 51, 55, 58, 63), Ch(32, 44, 48, 51, 55, 60) },
        groove = new Groove { kick = new[] { 0, 2, 4, 6 }, fill = true, snare = new[] { 2, 6 }, hat = "16", openHat = new[] { 1, 5 }, rim = new[] { 3, 7 }, bassWave = "sawtooth",
            bass = B(Bp(0, 1f), Bp(1, 1f), Bp(2, 1f), Bp(3, 1f), Bp(4, 1.5f), Bp(5, 1f), Bp(6, 2f), Bp(7, 1.5f)), arpWave = "square", arpVol = 0.035f, arp = new[] { 0, 4, 2, 4, 0, 4, 3, 5 },
            stab = new[] { 0, 3, 6 }, stabWave = "triangle", lead = true } };

    static readonly Track[] TRACKS =
    {
        new Track { name = "Drift", bpm = 112, pad = "warm", padCut = 520f, lfo = 0.06f, subWave = "sine", echo = 3, ambient = new Ambient { sparkle = 0.07f, sparkleWave = "sine", wind = 0.012f }, scale = DOR, melodyWave = "sine",
            chords = new[] { Ch(38, 50, 53, 57, 60, 64), Ch(34, 46, 50, 53, 57, 60), Ch(41, 53, 57, 60, 64, 67), Ch(36, 48, 52, 55, 59, 62) },
            groove = new Groove { kick = new[] { 0, 4 }, fill = true, snare = new[] { 2, 6 }, hat = "8", openHat = new[] { 3 }, bassWave = "sawtooth", bass = B(Bp(0, 1f), Bp(3, 1f), Bp(4, 1.5f), Bp(6, 2f), Bp(7, 0.75f)), arpWave = "square", arpVol = 0.045f, arp = new[] { 0, 2, 4, 2, 1, 3, 4, 3 } } },
        new Track { name = "Halcyon", bpm = 96, pad = "glass", padCut = 900f, lfo = 0.045f, subWave = "", echo = 4, ambient = new Ambient { melody = 0.16f, sparkle = 0.03f, sparkleWave = "triangle" }, scale = MAJ, melodyWave = "sine",
            chords = new[] { Ch(40, 52, 55, 59, 62, 66), Ch(36, 48, 52, 55, 59, 64), Ch(43, 55, 59, 62, 66, 69), Ch(38, 50, 54, 57, 60, 64) },
            groove = new Groove { hat = "off", shaker = true, bassWave = "triangle", bass = B(Bp(0, 1f), Bp(4, 1f), Bp(6, 1.5f)), arpWave = "triangle", arpVol = 0.09f, arp = new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, pluck = true, lead = true } },
        new Track { name = "Aurum", bpm = 118, pad = "organ", padCut = 700f, lfo = 0.08f, subWave = "triangle", echo = 2, ambient = new Ambient { wind = 0.03f, pulse = true }, scale = MINP, melodyWave = "triangle",
            chords = new[] { Ch(33, 45, 48, 52, 55, 59), Ch(41, 53, 57, 60, 64, 67), Ch(36, 48, 52, 55, 59, 62), Ch(43, 55, 59, 62, 65, 69) },
            groove = new Groove { kick = new[] { 0, 3, 6 }, fill = true, snare = new[] { 2, 5, 7 }, clap = true, hat = "16", openHat = new[] { 7 }, bassWave = "square", bass = B(Bp(0, 1f), Bp(1, 1f), Bp(3, 2f), Bp(6, 1.5f)), stab = new[] { 0, 3, 6 }, stabWave = "sawtooth" } },
        new Track { name = "Frost", bpm = 80, pad = "strings", padCut = 380f, lfo = 0.035f, subWave = "sine", echo = 4, ambient = new Ambient { sparkle = 0.11f, sparkleWave = "sine", melody = 0.06f, choir = true }, scale = AEO, melodyWave = "triangle",
            chords = new[] { Ch(35, 47, 50, 54, 57, 61), Ch(43, 55, 59, 62, 66, 69), Ch(38, 50, 54, 57, 61, 64), Ch(45, 57, 61, 64, 68, 71) },
            groove = new Groove { kick = new[] { 0, 5 }, snare = new[] { 4 }, hat = "8", bassWave = "sine", bass = B(Bp(0, 1f), Bp(5, 1f), Bp(6, 1.5f)), stab = new[] { 0, 4 }, stabWave = "sine", lead = true } },
        new Track { name = "Sable", bpm = 150, pad = "hollow", padCut = 300f, lfo = 0.1f, subWave = "sine", echo = 3, ambient = new Ambient { wind = 0.03f, sparkle = 0.03f, sparkleWave = "square" }, scale = MINP, melodyWave = "square",
            chords = new[] { Ch(37, 49, 52, 56, 59, 63), Ch(33, 45, 49, 52, 56, 59), Ch(40, 52, 56, 59, 63, 66), Ch(35, 47, 51, 54, 58, 61) },
            groove = new Groove { kick = new[] { 0, 2, 5 }, fill = true, snare = new[] { 4 }, hat = "16", openHat = new[] { 1, 3, 5, 7 }, bassWave = "sawtooth", bass = B(Bp(0, 1f), Bp(1, 1f), Bp(2, 1f), Bp(3, 1.5f), Bp(5, 1f), Bp(6, 2f), Bp(7, 1.5f)), arpWave = "sawtooth", arpVol = 0.03f, arp = new[] { 0, 0, 2, 2, 4, 4, 3, 1 } } },
        new Track { name = "Cinder", bpm = 100, pad = "warm", padCut = 480f, lfo = 0.05f, subWave = "triangle", echo = 3, ambient = new Ambient { pulse = true, sparkle = 0.04f, sparkleWave = "sine" }, scale = DOR, melodyWave = "sine",
            chords = new[] { Ch(31, 43, 46, 50, 53, 57), Ch(39, 51, 55, 58, 62, 65), Ch(34, 46, 50, 53, 57, 60), Ch(41, 53, 57, 60, 64, 67) },
            groove = new Groove { kick = new[] { 0, 4, 7 }, rim = new[] { 2, 6 }, hat = "8", brush = true, bassWave = "sine", bass = B(Bp(0, 1f), Bp(2, 0.75f), Bp(4, 1.5f), Bp(7, 2f)), stab = new[] { 1, 4, 6 }, stabWave = "triangle" } },
        new Track { name = "Meridian", bpm = 124, pad = "organ", padCut = 820f, lfo = 0.07f, subWave = "sine", echo = 2, ambient = new Ambient { choir = true, melody = 0.1f, sparkle = 0.05f, sparkleWave = "sine" }, scale = LYD, melodyWave = "triangle",
            chords = new[] { Ch(36, 48, 52, 55, 59, 62), Ch(43, 55, 59, 62, 66, 69), Ch(45, 57, 60, 64, 67, 71), Ch(41, 53, 57, 60, 64, 67) },
            groove = new Groove { kick = new[] { 0, 2, 4, 6 }, fill = true, snare = new[] { 2, 6 }, clap = true, hat = "8", openHat = new[] { 1, 3, 5, 7 }, bassWave = "square", bass = B(Bp(0, 1f), Bp(2, 1f), Bp(3, 2f), Bp(4, 1f), Bp(6, 1.5f), Bp(7, 1f)), arpWave = "triangle", arpVol = 0.05f, arp = new[] { 0, 2, 4, 7, 4, 2, 0, 3 }, lead = true } },
        new Track { name = "Umbra", bpm = 72, pad = "strings", padCut = 260f, lfo = 0.03f, subWave = "sine", echo = 4, ambient = new Ambient { wind = 0.04f, melody = 0.05f, choir = true }, scale = AEO, melodyWave = "sine",
            chords = new[] { Ch(29, 41, 44, 48, 51, 55), Ch(37, 49, 52, 56, 59, 63), Ch(34, 46, 49, 53, 56, 60), Ch(32, 44, 47, 51, 54, 58) },
            groove = new Groove { kick = new[] { 0, 6 }, snare = new[] { 4 }, hat = "off", shaker = true, bassWave = "square", bass = B(Bp(0, 1f), Bp(6, 0.75f)), stab = new[] { 0 }, stabWave = "triangle", lead = true } },
    };

    // ---- state (owned by the render thread once it starts; the main thread only reads the readouts and sets the switches)
    class Voice
    {
        public string kind;   // "note", "sample", "bass"
        public float[] tbl, buf;
        public float f, ph, a, d, vol, age, x1, x2, y1, y2;
        public int i;
        public bool echo, groove, sine;
    }
    class Pad { public float[] loop; public double ph; public float f, fTarget, tau, g, gTarget, gTau; public bool dying; public long dieAt; public int k; }

    readonly Dictionary<string, float[]> _tables = new Dictionary<string, float[]>();
    readonly Dictionary<string, float[]> _padLoops = new Dictionary<string, float[]>();
    readonly Dictionary<string, float[]> _hits = new Dictionary<string, float[]>();
    float[] _noise;
    long _pos;
    int _step;
    double _next;
    float _stepLen;
    string _mode = "ambient";
    float _modeUntil;
    int _chord, _track;
    // the fight: what to go back to, and when the all-clear lets the combat track go
    int _trackBefore;
    float _combatEnd = -1f;
    List<Voice> _voices = new List<Voice>();
    List<Pad> _pads = new List<Pad>();
    float _padGain, _padGainTarget = 0.16f, _padGainTau = 4f, _padGainWait = 0.5f;
    float _cut = 520f, _cutTarget = 520f, _cutTau = 0.01f;
    float _lfoF = 0.06f, _lfoTarget = 0.06f, _lfoTau = 0.01f, _lfoPh;
    float _subPh, _subF = 55f, _subTarget = 55f, _subTau = 0.01f, _subG, _subTargetG;
    string _subWave = "sine";
    float _grooveG, _grooveTarget, _grooveTau = 1.2f;
    int _melDeg = 3;
    float _melUntil;
    float[] _delay;
    int _delayPos;
    float _delayLen = 1f, _delayLenTarget = 1f, _delayLp;
    float _fx1, _fx2, _fy1, _fy2;
    float _outGain = OUT_GAIN;
    volatile float _outTarget = OUT_GAIN;
    readonly float[] _mix = new float[CHUNK];
    readonly float[] _echoIn = new float[CHUNK];
    readonly float[] _padMix = new float[CHUNK];
    System.Random _rng = new System.Random();
    readonly object _lock = new object();
    readonly List<string[]> _pending = new List<string[]>();
    // the ring buffer between the render thread and the mixer
    const int RING = RATE * 2;
    readonly float[] _ring = new float[RING];
    long _written, _read;   // sample counts, producer and consumer
    double _readFrac;
    Thread _thread;
    volatile bool _running;
    AudioSource _src;
    // readouts for the smoke run
    public volatile bool readyForSmoke;
    public int voicesPeak;
    public float outPeak;
    public long renderTicks;
    public int underruns;

    public static void Create()
    {
        if (I != null) return;
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        I = go.AddComponent<Music>();
    }

    void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        // a silent looping clip keeps the source playing, so the filter below is asked for samples
        int outRate = AudioSettings.outputSampleRate;
        var clip = AudioClip.Create("music-carrier", outRate, 1, outRate, false);
        clip.SetData(new float[outRate], 0);
        _src.clip = clip;
        _src.loop = true;
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
        _src.ignoreListenerVolume = true;   // the music has its own switch and slider, apart from the sound effects
        _src.volume = 1f;
        _delay = new float[RATE * 3];
        ApplySettings();
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "BeltRunner music" };
        _thread.Start();
        _src.Play();
    }

    void OnDestroy()
    {
        _running = false;
        if (_thread != null) _thread.Join(500);
        if (I == this) I = null;
    }

    void Update()
    {
        List<string[]> ev = null;
        lock (_lock)
        {
            if (_pending.Count > 0) { ev = new List<string[]>(_pending); _pending.Clear(); }
        }
        if (ev == null) return;
        foreach (var e in ev)
        {
            if (e[0] == "groove") { if (onGroove != null) onGroove(e[1]); }
            else if (e[0] == "combat") { if (onCombat != null) onCombat(e[1]); }
            else if (onTrack != null) onTrack(e[1]);
        }
    }

    /// The menu's music switch and volume (its own path to the speakers: the browser's music slider is independent).
    public void ApplySettings()
    {
        _outTarget = State.musicOn ? OUT_GAIN * Mathf.Clamp01(State.musicVolume) : 0f;
    }

    public string TrackName { get { return TRACKS[_track].name; } }
    public string Mode { get { return _mode; } }
    public int Step { get { return _step; } }
    public float Seconds { get { return (float)_pos / RATE; } }

    // ---- the mixer pulls from the ring buffer, upsampling to its own rate
    void OnAudioFilterRead(float[] data, int channels)
    {
        int frames = data.Length / channels;
        double step = (double)RATE / AudioSettings.outputSampleRate;
        long avail = _written - _read;
        for (int fr = 0; fr < frames; fr++)
        {
            float v = 0f;
            if (avail >= 2)
            {
                int i0 = (int)(_read % RING), i1 = (int)((_read + 1) % RING);
                v = _ring[i0] + (_ring[i1] - _ring[i0]) * (float)_readFrac;
            }
            else underruns++;
            for (int c = 0; c < channels; c++) data[fr * channels + c] += v;
            _readFrac += step;
            while (_readFrac >= 1.0 && avail >= 2) { _readFrac -= 1.0; _read++; avail--; }
        }
    }

    // ---- the render thread
    void Run()
    {
        try
        {
            BuildTables();
            BuildPadLoops();
            BuildNoise();
            InitSong();
            readyForSmoke = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_running)
            {
                long avail = RING - (_written - _read);
                if (avail >= CHUNK * 2)
                {
                    long t0 = sw.ElapsedTicks;
                    RenderChunk();
                    renderTicks += sw.ElapsedTicks - t0;
                }
                else Thread.Sleep(4);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("music: render thread stopped · " + e.Message);
        }
    }

    void BuildTables()
    {
        foreach (var w in new[] { "sine", "triangle", "square", "sawtooth" })
        {
            var t = new float[TABLE];
            for (int i = 0; i < TABLE; i++)
            {
                float ph = (float)i / TABLE;
                float v;
                switch (w)
                {
                    case "sine": v = Mathf.Sin(ph * Mathf.PI * 2f); break;
                    case "triangle": v = 1f - 4f * Mathf.Abs(ph - 0.5f); break;
                    case "square": v = ph < 0.5f ? 1f : -1f; break;
                    default: v = 2f * ph - 1f; break;
                }
                t[i] = v;
            }
            _tables[w] = t;
        }
    }

    /// A pad recipe rendered once at PAD_REF Hz as a loop: every chord voice plays it back at its own pitch.
    void BuildPadLoops()
    {
        int n = (int)(PAD_LOOP * RATE);
        int fade = (int)(0.25f * RATE);
        foreach (var kv in PADS)
        {
            var buf = new float[n];
            foreach (var osc in kv.Value)
            {
                var tbl = _tables[osc.wave];
                float f = PAD_REF * Mathf.Pow(2f, osc.cents / 1200f) * osc.mult;
                float g = osc.gain;
                double ph = 0.0;
                double inc = f / (double)RATE;
                for (int i = 0; i < n; i++)
                {
                    buf[i] += tbl[(int)(ph * TABLE) % TABLE] * g;
                    ph += inc;
                    if (ph >= 1.0) ph -= 1.0;
                }
            }
            // a crossfade at the end so the loop joins cleanly
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                buf[n - fade + i] = buf[n - fade + i] * (1f - a) + buf[i] * a;
            }
            _padLoops[kv.Key] = buf;
        }
    }

    void BuildNoise()
    {
        _noise = new float[65536];
        for (int i = 0; i < 65536; i++) _noise[i] = (float)(_rng.NextDouble() * 2.0 - 1.0);
    }

    float Rand() { return (float)_rng.NextDouble(); }
    float Range(float a, float b) { return a + (b - a) * Rand(); }

    void InitSong()
    {
        _track = _rng.Next(TRACKS.Length);
        ApplyTrack(0.01f);
        _padGainWait = 0.5f;
        _next = 0.2 * RATE;
        _modeUntil = Range(120f, 200f);
    }

    Track K { get { return _mode == "combat" ? COMBAT : TRACKS[_track]; } }
    float T { get { return (float)_pos / RATE; } }

    // ---- the track: pad recipe, filter, sub, echo time, first chord
    void ApplyTrack(float glide)
    {
        var k = K;
        _stepLen = 60f / k.bpm / 2f;
        _chord = 0;
        _delayLenTarget = _stepLen * k.echo;
        _cutTarget = k.padCut;
        _cutTau = glide;
        _lfoTarget = k.lfo;
        _lfoTau = glide;
        BuildPad(k.pad);
        _subWave = string.IsNullOrEmpty(k.subWave) ? "sine" : k.subWave;
        _subTargetG = string.IsNullOrEmpty(k.subWave) ? 0f : (k.subWave == "sine" ? 0.14f : 0.09f);
        SetChord(0, glide);
    }

    void BuildPad(string recipe)
    {
        foreach (var v in _pads)
        {
            v.dying = true;
            v.gTarget = 0f;
            v.gTau = 0.8f;
            v.dieAt = _pos + 4 * RATE;
        }
        var loop = _padLoops[recipe];
        for (int k = 0; k < 5; k++)
            _pads.Add(new Pad { loop = loop, ph = Rand() * loop.Length, f = 220f, fTarget = 220f, tau = 0.01f, g = 0f, gTarget = 0.2f, gTau = 1.5f, k = k });
    }

    void SetChord(int i, float glide)
    {
        _chord = i;
        var c = K.chords[i];
        foreach (var v in _pads)
        {
            if (v.dying) continue;
            v.fTarget = c.notes[v.k];
            v.tau = glide;
        }
        _subTarget = c.root;
        _subTau = glide;
    }

    // ---- one-shot voices
    void Note(string wave, float f, float a, float d, float vol, bool toEcho, bool groove)
    {
        _voices.Add(new Voice { kind = "note", tbl = _tables[wave], f = f, a = a, d = d, vol = vol, echo = toEcho, groove = groove });
    }

    /// A noise hit through a filter (bandpass / highpass / lowpass) with an exponential fade, rendered once per recipe.
    void Hit(string filter, float freq, float q, float d, float vol, bool toEcho, bool groove, float delay = 0f)
    {
        string key = filter + ":" + Mathf.RoundToInt(freq / 25f) * 25 + ":" + q.ToString("0.0") + ":" + d.ToString("0.000");
        float[] buf;
        if (!_hits.TryGetValue(key, out buf)) { buf = RenderHit(filter, freq, q, d); _hits[key] = buf; }
        _voices.Add(new Voice { kind = "sample", buf = buf, i = -(int)(delay * RATE), vol = vol, echo = toEcho, groove = groove });
    }

    float[] RenderHit(string filter, float freq, float q, float d)
    {
        int n = (int)((d + 0.05f) * RATE);
        var buf = new float[n];
        var co = Biquad(filter, freq, q);
        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        int start = _rng.Next(60000);
        float decay = Mathf.Pow(0.0001f, 1f / Mathf.Max(1f, d * RATE));
        float env = 1f;
        for (int i = 0; i < n; i++)
        {
            float x = _noise[(start + i) % 65536];
            float y = co[0] * x + co[1] * x1 + co[2] * x2 - co[3] * y1 - co[4] * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            buf[i] = y * env;
            env *= decay;
        }
        return buf;
    }

    /// RBJ biquad coefficients [b0, b1, b2, a1, a2] normalised by a0.
    static float[] Biquad(string kind, float freq, float q)
    {
        float w0 = Mathf.PI * 2f * Mathf.Clamp(freq, 10f, RATE * 0.45f) / RATE;
        float cw = Mathf.Cos(w0), sw = Mathf.Sin(w0);
        float alpha = sw / (2f * Mathf.Max(0.05f, q));
        float b0, b1, b2;
        switch (kind)
        {
            case "lowpass": b0 = (1f - cw) * 0.5f; b1 = 1f - cw; b2 = (1f - cw) * 0.5f; break;
            case "highpass": b0 = (1f + cw) * 0.5f; b1 = -(1f + cw); b2 = (1f + cw) * 0.5f; break;
            default: b0 = alpha; b1 = 0f; b2 = -alpha; break;
        }
        float a0 = 1f + alpha;
        return new[] { b0 / a0, b1 / a0, b2 / a0, (-2f * cw) / a0, (1f - alpha) / a0 };
    }

    void Kick()
    {
        float[] buf;
        if (!_hits.TryGetValue("kick", out buf))
        {
            int n = (int)(0.32f * RATE);
            buf = new float[n];
            double ph = 0.0;
            var tbl = _tables["sine"];
            float decay = Mathf.Pow(0.0001f / 0.7f, 1f / (0.3f * RATE));
            float g = 0.7f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float f = 160f * Mathf.Pow(42f / 160f, Mathf.Min(1f, t / 0.11f));
                buf[i] = tbl[(int)(ph * TABLE) % TABLE] * g;
                ph += f / RATE;
                if (ph >= 1.0) ph -= 1.0;
                g *= decay;
            }
            _hits["kick"] = buf;
        }
        _voices.Add(new Voice { kind = "sample", buf = buf, i = 0, vol = 1f, echo = false, groove = true });
    }

    /// The bass: an oscillator through a lowpass (a sweep from 900 to 160 Hz for the buzzy waves) with its own envelope.
    void Bass(float f, string wave)
    {
        bool sine = wave == "sine";
        _voices.Add(new Voice
        {
            kind = "bass", tbl = _tables[wave], f = f, sine = sine, a = sine ? 0.01f : 0.005f,
            d = sine ? Mathf.Min(0.6f, _stepLen * 1.8f) : Mathf.Min(0.24f, _stepLen * 0.9f),
            vol = sine ? 0.28f : (wave == "square" ? 0.14f : 0.2f), echo = false, groove = true,
        });
    }

    void Stab(Chord c, string wave)
    {
        for (int k = 1; k < 5; k++) Note(wave, c.notes[k], 0.01f, wave == "sawtooth" ? 0.12f : 0.3f, wave == "sawtooth" ? 0.03f : 0.05f, false, true);
    }

    /// A wandering lead: a random walk over the track's scale above the chord root, phrases with rests.
    void Melody(Track k, Chord c, float vol, string wave, bool groove)
    {
        float t = T;
        if (t < _melUntil) return;
        var sc = k.scale;
        _melDeg = Mathf.Clamp(_melDeg + (Rand() < 0.5f ? -1 : 1) * (Rand() < 0.25f ? 2 : 1), 0, sc.Length - 1);
        float f = Hz(c.rootMidi + 24f + sc[_melDeg]);
        float r = Rand();
        float len = _stepLen * (r < 0.3f ? 4f : (r < 0.65f ? 2f : 1f));
        Note(wave, f, 0.03f, len * 1.4f, vol, false, groove);
        Note(wave, f, 0.03f, len * 1.4f, vol * 0.5f, true, groove);
        _melUntil = t + len + (Rand() < 0.2f ? _stepLen * Range(4f, 10f) : 0f);
    }

    static bool Has(int[] arr, int v)
    {
        if (arr == null) return false;
        foreach (var x in arr) if (x == v) return true;
        return false;
    }

    // ---- the sequencer: one step of the pattern (schedule(s, t) in the browser)
    void Schedule(int s)
    {
        var k = K;
        var g = k.groove;
        var a = k.ambient;
        int bar = s % 8;
        float t = T;
        if (s % 32 == 0 && s > 0) SetChord((_chord + 1) % k.chords.Length, _mode != "ambient" ? 0.05f : 1.5f);
        // the fight: cut to the combat track the step raiders attack; six seconds after the all-clear, on a bar, drift back
        if (combat && _mode != "combat")
        {
            _trackBefore = _track;
            _mode = "combat";
            _combatEnd = -1f;
            _grooveTarget = 1f;
            _grooveTau = 0.35f;
            ApplyTrack(0.25f);
            k = K; g = k.groove; a = k.ambient;
            QueueEvent("combat", k.name);
        }
        else if (!combat && _mode == "combat")
        {
            if (_combatEnd < 0f) _combatEnd = t + 6f;
            else if (t >= _combatEnd && bar == 0)
            {
                _mode = "ambient";
                _modeUntil = t + Range(90f, 160f);
                _grooveTarget = 0f;
                _grooveTau = 3f;
                _track = _trackBefore;
                _combatEnd = -1f;
                ApplyTrack(3f);
                k = K; g = k.groove; a = k.ambient;
                QueueEvent("track", k.name);
            }
        }
        else if (combat) _combatEnd = -1f;
        if (_mode != "combat" && bar == 0 && t > _modeUntil)
        {
            if (_mode == "ambient")
            {
                _mode = "groove";
                _modeUntil = t + Range(60f, 95f);
                _grooveTarget = 1f;
                _grooveTau = 1.2f;
                QueueEvent("groove", k.name);
            }
            else
            {
                // groove over: fade it out and drift into the next track
                _mode = "ambient";
                _modeUntil = t + Range(120f, 200f);
                _grooveTarget = 0f;
                _grooveTau = 2.5f;
                _track = (_track + 1) % TRACKS.Length;
                ApplyTrack(2.5f);
                k = K;
                g = k.groove;
                a = k.ambient;
                QueueEvent("track", k.name);
            }
        }
        var c = k.chords[_chord];
        if (_mode == "ambient")
        {
            if (a.sparkle >= 0f && Rand() < a.sparkle)
            {
                float f = c.notes[_rng.Next(5)] * (Rand() < 0.5f ? 2f : 4f);
                Note(a.sparkleWave, f, 0.04f, 1.8f, 0.05f, true, false);
                Note(a.sparkleWave, f, 0.04f, 1.8f, 0.035f, false, false);
            }
            if (a.wind >= 0f && Rand() < a.wind) Hit("bandpass", Range(200f, 500f), 1.5f, 3f, 0.06f, false, false);
            if (a.melody >= 0f && Rand() < a.melody) Melody(k, c, 0.045f, k.melodyWave, false);
            if (a.pulse && bar % 4 == 0)
            {
                Note("triangle", c.root * 2f, 0.005f, _stepLen * 1.5f, 0.06f, false, false);
                Note("triangle", c.root * 2f, 0.005f, _stepLen * 1.5f, 0.03f, true, false);
            }
            if (a.choir && s % 32 == 8)
            {
                Note("sine", c.root * 3f, 3f, 5f, 0.05f, false, false);
                Note("sine", c.root * 3f * 1.005f, 3f, 5f, 0.04f, false, false);
            }
        }
        else
        {
            if (Has(g.kick, bar) || (g.fill && bar == 7 && (s >> 3) % 4 == 3)) Kick();
            if (Has(g.snare, bar))
            {
                if (g.clap) { foreach (var dl in new[] { 0f, 0.012f, 0.026f }) Hit("bandpass", 1500f, 1.2f, 0.12f, 0.16f, false, true, dl); }
                else
                {
                    Hit("bandpass", 1900f, 0.9f, 0.16f, 0.22f, false, true);
                    Note("sine", 190f, 0.002f, 0.09f, 0.14f, false, true);
                }
            }
            if (Has(g.rim, bar)) Hit("bandpass", 2600f, 4f, 0.05f, 0.18f, false, true);
            if (g.hat == "16" || (g.hat == "8" && bar % 2 == 0))
            {
                bool brush = g.brush;
                Hit("highpass", brush ? 4500f : 7500f, 0.7f, brush ? 0.09f : (bar % 2 == 1 ? 0.05f : 0.035f), brush ? 0.05f : (bar % 2 == 1 ? 0.05f : 0.08f), false, true);
            }
            if (Has(g.openHat, bar)) Hit("highpass", 6000f, 0.7f, 0.2f, 0.05f, false, true);
            if (g.shaker) Hit("highpass", 9000f, 0.5f, 0.06f, bar % 2 == 1 ? 0.05f : 0.03f, false, true);
            foreach (var bp in g.bass) if (bar == (int)bp[0]) Bass(c.root * bp[1], g.bassWave);
            if (g.arp != null)
            {
                int deg = g.arp[bar];
                float f = c.notes[deg % 5] * 2f * (deg >= 5 ? 2f : 1f);
                float d = g.pluck ? 0.35f : 0.16f;
                Note(g.arpWave, f, 0.005f, d, g.arpVol, false, true);
                Note(g.arpWave, f, 0.005f, d, g.arpVol * 0.65f, true, true);
            }
            if (Has(g.stab, bar)) Stab(c, g.stabWave);
            if (g.lead && Rand() < 0.35f) Melody(k, c, 0.06f, k.melodyWave, true);
        }
    }

    void QueueEvent(string kind, string name)
    {
        lock (_lock) _pending.Add(new[] { kind, name });
    }

    // ---- rendering: CHUNK samples at a time, split at step boundaries so every note starts on its sample
    void RenderChunk()
    {
        int done = 0;
        while (done < CHUNK)
        {
            long untilStep = (long)Math.Ceiling(_next) - _pos;
            if (untilStep <= 0)
            {
                Schedule(_step);
                _step++;
                _next += _stepLen * RATE;
                continue;
            }
            int n = (int)Math.Min(CHUNK - done, untilStep);
            RenderSpan(done, n);
            done += n;
            _pos += n;
        }
        for (int i = 0; i < CHUNK; i++)
        {
            float v = _mix[i];
            if (Mathf.Abs(v) > outPeak) outPeak = Mathf.Abs(v);
            _ring[(int)((_written + i) % RING)] = v;
        }
        _written += CHUNK;
    }

    static float Approach(float v, float target, float tau, float dt)
    {
        if (tau <= 0.0001f) return target;
        return v + (target - v) * (1f - Mathf.Exp(-dt / tau));
    }

    void RenderSpan(int at, int n)
    {
        float dt = (float)n / RATE;
        // smoothed controls, once per span (as Web Audio's setTargetAtTime ramps)
        if (_padGainWait > 0f) _padGainWait -= dt;
        else _padGain = Approach(_padGain, _padGainTarget, _padGainTau, dt);
        _cut = Approach(_cut, _cutTarget, _cutTau, dt);
        _lfoF = Approach(_lfoF, _lfoTarget, _lfoTau, dt);
        _subF = Approach(_subF, _subTarget, _subTau, dt);
        _subG = Approach(_subG, _subTargetG, 2f, dt);
        _grooveG = Approach(_grooveG, _grooveTarget, _grooveTau, dt);
        _delayLen = Approach(_delayLen, _delayLenTarget, 0.5f, dt);
        _outGain = Approach(_outGain, _outTarget, 0.3f, dt);
        for (int i = 0; i < n; i++) { _mix[at + i] = 0f; _echoIn[at + i] = 0f; _padMix[at + i] = 0f; }
        // the pad: five resampled loops, gains ramping, pitches gliding
        for (int p = _pads.Count - 1; p >= 0; p--)
        {
            var v = _pads[p];
            if (v.dying && _pos >= v.dieAt) { _pads.RemoveAt(p); continue; }
            v.f = Approach(v.f, v.fTarget, v.tau, dt);
            v.g = Approach(v.g, v.gTarget, v.gTau, dt);
            float g = v.g;
            if (g < 0.0005f) continue;
            var loop = v.loop;
            int L = loop.Length;
            double ph = v.ph;
            double inc = v.f / PAD_REF;
            for (int i = 0; i < n; i++)
            {
                int ip = (int)ph;
                float fr = (float)(ph - ip);
                float s0 = loop[ip];
                float s1 = loop[(ip + 1) % L];
                _padMix[at + i] += (s0 + (s1 - s0) * fr) * g;
                ph += inc;
                if (ph >= L) ph -= L;
            }
            v.ph = ph;
        }
        // the breathing lowpass over the pad, then the pad gain into the mix
        var co = Biquad("lowpass", _cut + 260f * Mathf.Sin(_lfoPh * Mathf.PI * 2f), 0.8f);
        _lfoPh = Mathf.Repeat(_lfoPh + _lfoF * dt, 1f);
        for (int i = 0; i < n; i++)
        {
            float x = _padMix[at + i];
            float y = co[0] * x + co[1] * _fx1 + co[2] * _fx2 - co[3] * _fy1 - co[4] * _fy2;
            _fx2 = _fx1; _fx1 = x; _fy2 = _fy1; _fy1 = y;
            _mix[at + i] += y * _padGain;
        }
        // the sub
        if (_subG > 0.0005f)
        {
            var tbl = _tables[_subWave];
            float inc = _subF / RATE;
            for (int i = 0; i < n; i++)
            {
                _mix[at + i] += tbl[(int)(_subPh * TABLE) % TABLE] * _subG;
                _subPh += inc;
                if (_subPh >= 1f) _subPh -= 1f;
            }
        }
        // the one-shots
        float gg = _grooveG;
        for (int vi = _voices.Count - 1; vi >= 0; vi--)
        {
            var v = _voices[vi];
            bool alive = true;
            float gv = v.groove ? gg : 1f;
            bool toEcho = v.echo;
            if (v.kind == "sample")
            {
                var buf = v.buf;
                int idx = v.i;
                float vol = v.vol * gv;
                for (int i = 0; i < n; i++)
                {
                    if (idx >= 0 && idx < buf.Length)
                    {
                        float s = buf[idx] * vol;
                        if (toEcho) _echoIn[at + i] += s; else _mix[at + i] += s;
                    }
                    idx++;
                }
                v.i = idx;
                alive = idx < buf.Length;
            }
            else if (v.kind == "note")
            {
                var tbl = v.tbl;
                float ph = v.ph, inc = v.f / RATE, a = v.a, d = v.d, peak = v.vol * gv, age = v.age;
                float decay = Mathf.Pow(0.0001f / Mathf.Max(peak, 0.0002f), 1f / Mathf.Max(1f, d * RATE));
                for (int i = 0; i < n; i++)
                {
                    float e = age < a ? 0.0001f + (peak - 0.0001f) * (age / a) : peak * Mathf.Pow(decay, (age - a) * RATE);
                    float s = tbl[(int)(ph * TABLE) % TABLE] * e;
                    if (toEcho) _echoIn[at + i] += s; else _mix[at + i] += s;
                    ph += inc;
                    if (ph >= 1f) ph -= 1f;
                    age += 1f / RATE;
                }
                v.ph = ph;
                v.age = age;
                alive = age < a + d;
            }
            else
            {
                var tbl = v.tbl;
                float ph = v.ph, inc = v.f / RATE, a = v.a, d = v.d, peak = v.vol * gv, age = v.age;
                bool sine = v.sine;
                float cutoff = sine ? 600f : 900f * Mathf.Pow(160f / 900f, Mathf.Min(1f, age / 0.2f));
                var bco = Biquad("lowpass", cutoff, sine ? 0.5f : 3f);
                float x1 = v.x1, x2 = v.x2, y1 = v.y1, y2 = v.y2;
                float decay = Mathf.Pow(0.0001f / Mathf.Max(peak, 0.0002f), 1f / Mathf.Max(1f, d * RATE));
                for (int i = 0; i < n; i++)
                {
                    float e = age < a ? 0.0001f + (peak - 0.0001f) * (age / a) : peak * Mathf.Pow(decay, (age - a) * RATE);
                    float x = tbl[(int)(ph * TABLE) % TABLE];
                    float y = bco[0] * x + bco[1] * x1 + bco[2] * x2 - bco[3] * y1 - bco[4] * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    _mix[at + i] += y * e;
                    ph += inc;
                    if (ph >= 1f) ph -= 1f;
                    age += 1f / RATE;
                }
                v.ph = ph; v.age = age; v.x1 = x1; v.x2 = x2; v.y1 = y1; v.y2 = y2;
                alive = age < 0.7f;
            }
            if (!alive) _voices.RemoveAt(vi);
        }
        if (_voices.Count > voicesPeak) voicesPeak = _voices.Count;
        // the echo: darkened and fed back, its time following the tempo; wet into the mix
        int dl = Mathf.Clamp((int)(_delayLen * RATE), 1, _delay.Length - 1);
        for (int i = 0; i < n; i++)
        {
            int rd = (_delayPos - dl + _delay.Length) % _delay.Length;
            float back = _delay[rd];
            _delayLp += (back - _delayLp) * 0.5f;   // a one-pole lowpass round 2.4 kHz at this rate
            _delay[_delayPos] = _echoIn[at + i] + _delayLp * 0.42f;
            _mix[at + i] += back * 0.45f;
            _delayPos = (_delayPos + 1) % _delay.Length;
        }
        // out
        for (int i = 0; i < n; i++) _mix[at + i] = Mathf.Clamp(_mix[at + i] * _outGain, -1f, 1f);
    }

    /// One line for the smoke log.
    public string Report()
    {
        float secs = Seconds;
        float cost = secs > 0.1f ? 100f * (renderTicks / (float)System.Diagnostics.Stopwatch.Frequency) / secs : 0f;
        return "ready=" + readyForSmoke + " · track " + TrackName + " · mode " + _mode + " · step " + _step + " · voices peak " + voicesPeak + " · out peak " + outPeak.ToString("0.000") + " · time " + secs.ToString("0.0") + " s · render cost " + cost.ToString("0") + "% of realtime · underruns " + underruns + " · mixer " + AudioSettings.outputSampleRate + " Hz";
    }
}
