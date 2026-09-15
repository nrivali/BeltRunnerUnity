using UnityEngine;

/// The look of space, ported from Astra's lighting module and the browser's sky (via the Godot port): an ACES-mapped
/// sun at the zone's colour and exposure with one soft shadow box round the ship, a faint ambient, reflections off the
/// hulls from the sky, a visible sun disc with an optical glare, a nebula backdrop with a dust band, and a couple of
/// thousand stars. The sky is a skybox shader; the glow and tone mapping are an image effect on the camera.
public class Lighting
{
    public class Profile
    {
        public Color color;
        public float intensity, radius, exposure;
    }

    /// Per-zone sun profiles (the HTML's `profiles`): colour, strength, the disc's angular radius, and the exposure.
    public static Profile ProfileFor(string zoneId)
    {
        // the look the user asked for (2026-09-15, from a reference frame): a warm, low, golden sun with a wide glare
        if (zoneId == "hub") return new Profile { color = Data.Hex("#ffe9cf"), intensity = 5.0f, radius = 0.0085f, exposure = 1.0f };
        return new Profile { color = Data.Hex("#fff1e2"), intensity = 5.8f, radius = 0.0085f, exposure = 1.0f };
    }

    public const float SHADOW_REACH = 3300f;          // the browser's 2,400 u shadow box round a focus 900 u ahead of the camera
    public const float SHADOW_REACH_CARRIER = 6500f;  // 5,600 u near the carrier, so the hangar and the hull shadow properly
    public const float CARRIER_NEAR = 11000f;

    public Light sun;
    public Material sky;
    public Post post;
    Camera _cam;

    public void Setup(Camera cam)
    {
        _cam = cam;
        foreach (var l in Object.FindObjectsByType<Light>()) l.enabled = false;   // a template scene's own light would double the sun
        var go = new GameObject("Sun");
        sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Data.Hex("#fff4e4");
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.92f;
        sun.shadowBias = 0.1f;
        sun.shadowNormalBias = 0.6f;
        // shadows as the browser casts them: one box a few kilometres round the ship, never the whole belt
        QualitySettings.shadowDistance = SHADOW_REACH;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        // the sky
        var sh = Shader.Find("BeltRunner/Sky");
        if (sh != null)
        {
            sky = new Material(sh);
            RenderSettings.skybox = sky;
            cam.clearFlags = CameraClearFlags.Skybox;
        }
        // the fill: nearly none, and what there is runs cool, so the shadow side of a rock goes to a deep blue-black
        // and the sun does all the shaping (the reference frame's contrast); the hulls still reflect the sky
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.48f, 0.62f) * 0.04f;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 128;
        // a faint dark haze with distance on the rock alone (the rock shader's finalcolor): far rocks sink toward the
        // sky and near ones stand out, while the planet, the carrier and the ships stay clear at any range
        RenderSettings.fog = false;
        Shader.SetGlobalFloat("_HazeDensity", 0.000012f);
        Shader.SetGlobalColor("_HazeColor", new Color(0.030f, 0.026f, 0.028f));
        // the finish: ACES at the zone's exposure and the glow
        if (Shader.Find("BeltRunner/Post") != null)
        {
            post = cam.gameObject.AddComponent<Post>();
        }
    }

    /// The zone's sun: direction, colour, strength, disc size, exposure; and its sky colours. The reflection map is
    /// baked here with a gentler sun disc.
    public void SetZone(Data.Zone z)
    {
        var p = ProfileFor(z.id);
        var dir = z.sunDir.normalized;
        sun.transform.rotation = Quaternion.LookRotation(-dir, Mathf.Abs(dir.y) < 0.98f ? Vector3.up : Vector3.forward);
        sun.color = p.color;
        sun.intensity = 2.4f * p.intensity / 5.2f;
        Shader.SetGlobalVector("_BeltSunDir", new Vector4(dir.x, dir.y, dir.z, 0f));
        Shader.SetGlobalColor("_SpeckSunColor", p.color);
        if (post != null) { post.exposure = p.exposure; post.sunDir = dir; post.sun = sun; post.sunColor = p.color; }
        if (sky != null)
        {
            sky.SetVector("_SunDir", dir);
            sky.SetFloat("_SunRadius", p.radius);
            sky.SetColor("_SunColor", Color.Lerp(p.color, Color.white, 0.15f));
            sky.SetColor("_BaseColor", Color.Lerp(z.bg, Color.black, 0.65f));   // space is black; the zone's tint is a whisper
            sky.SetFloat("_DiscGain", 1.6f);
            DynamicGI.UpdateEnvironment();
            sky.SetFloat("_DiscGain", 6f);
        }
    }

    /// Every frame: the shadow box grows near the carrier, as the browser's does.
    public void Update(Vector3 camPos, Vector3 carrierPos)
    {
        float reach = (camPos - carrierPos).magnitude < CARRIER_NEAR ? SHADOW_REACH_CARRIER : SHADOW_REACH;
        if (QualitySettings.shadowDistance != reach) QualitySettings.shadowDistance = reach;
    }
}

/// The image effect: a quarter-size bright pass, a two-tap separable blur, and the composite with exposure and ACES.
public class Post : MonoBehaviour
{
    public float exposure = 1.0f;
    public float threshold = 0.95f;   // the sun, the engines, the hot veins and the beam bloom; lit hull does not
    public float intensity = 0.8f;
    public float burn;                              // the afterburner: 0..1, the radial blur and the fringing
    public Vector2 burnCenter = new Vector2(0.5f, 0.5f);   // its centre in uv, leading into a turn
    // the god rays: the sun's place in the frame from its direction (Lighting sets it), the source masked round it,
    // blurred toward it twice; faded out as the sun leaves the frame, and off while it is behind the camera
    public Vector3 sunDir = Vector3.up;
    public float rays = 0.4f;
    // the volumetric dust (BeltRunner/Volumetric): a ray march through thin dust lit by the sun and shadowed by its
    // shadow map, at half resolution; V toggles it. The shadow map is copied to a global after the sun draws it.
    public Light sun;
    public Color sunColor = Color.white;
    public bool volumetric = true;
    public float dustDensity = 0.00002f;   // per world unit
    public float dustReach = 6000f;        // how far the march goes
    public int dustSteps = 48;
    public float dustIntensity = 0.3f;
    public float dustAniso = 0.75f;        // Henyey-Greenstein g: forward-peaked toward the sun
    Material _mat, _volMat;
    Camera _camera;
    UnityEngine.Rendering.CommandBuffer _shadowCopy;
    bool _volTried;

    void OnEnable()
    {
        var c = GetComponent<Camera>();
        if (c != null) c.depthTextureMode |= DepthTextureMode.Depth;   // the march stops at the surfaces
    }

    void OnDisable()
    {
        if (sun != null && _shadowCopy != null) sun.RemoveCommandBuffer(UnityEngine.Rendering.LightEvent.AfterShadowMap, _shadowCopy);
        _shadowCopy = null;
    }

    void HookSun()
    {
        if (sun == null || _shadowCopy != null) return;
        _shadowCopy = new UnityEngine.Rendering.CommandBuffer();
        _shadowCopy.name = "Belt shadow map for the dust";
        _shadowCopy.SetGlobalTexture("_BeltCascadeShadow", UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive);
        sun.AddCommandBuffer(UnityEngine.Rendering.LightEvent.AfterShadowMap, _shadowCopy);
    }

    /// The dust pass into a half-size texture, or null when it is off or the shader is missing.
    RenderTexture Dust(RenderTexture src)
    {
        if (!volumetric || _camera == null) return null;
        if (_volMat == null && !_volTried)
        {
            _volTried = true;
            var sh = Shader.Find("BeltRunner/Volumetric");
            if (sh != null) _volMat = new Material(sh);
        }
        if (_volMat == null) return null;
        HookSun();
        float far = _camera.farClipPlane;
        var cp = _camera.transform.position;
        _volMat.SetVector("_VolC0", _camera.ViewportToWorldPoint(new Vector3(0f, 0f, far)) - cp);
        _volMat.SetVector("_VolC1", _camera.ViewportToWorldPoint(new Vector3(1f, 0f, far)) - cp);
        _volMat.SetVector("_VolC2", _camera.ViewportToWorldPoint(new Vector3(0f, 1f, far)) - cp);
        _volMat.SetVector("_VolC3", _camera.ViewportToWorldPoint(new Vector3(1f, 1f, far)) - cp);
        _volMat.SetVector("_VolSun", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        _volMat.SetColor("_VolSunColor", sunColor);
        _volMat.SetVector("_VolParams", new Vector4(dustDensity, dustReach, dustSteps, dustIntensity));
        _volMat.SetVector("_VolParams2", new Vector4(dustAniso, 1f / 700f, Time.time, 0.6f));
        var v = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(src, v, _volMat, 0);
        return v;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (_mat == null)
        {
            var sh = Shader.Find("BeltRunner/Post");
            if (sh == null) { Graphics.Blit(src, dst); return; }
            _mat = new Material(sh);
        }
        _mat.SetFloat("_Exposure", exposure);
        _mat.SetFloat("_Threshold", threshold);
        _mat.SetFloat("_Intensity", intensity);
        _mat.SetFloat("_Burn", burn);
        _mat.SetVector("_BurnCenter", new Vector4(burnCenter.x, burnCenter.y, 0f, 0f));
        int w = src.width / 4, h = src.height / 4;
        var a = RenderTexture.GetTemporary(w, h, 0, src.format);
        var b = RenderTexture.GetTemporary(w, h, 0, src.format);
        Graphics.Blit(src, a, _mat, 0);
        Graphics.Blit(a, b, _mat, 1);
        Graphics.Blit(b, a, _mat, 2);
        Graphics.Blit(a, b, _mat, 1);
        Graphics.Blit(b, a, _mat, 2);
        _mat.SetTexture("_Bloom", a);
        // the god rays
        if (_camera == null) _camera = GetComponent<Camera>();
        var dust = Dust(src);
        _mat.SetTexture("_Volume", dust != null ? (Texture)dust : Texture2D.blackTexture);
        float gain = 0f;
        var r = RenderTexture.GetTemporary(w, h, 0, src.format);
        if (_camera != null && rays > 0f)
        {
            var vp = _camera.WorldToViewportPoint(_camera.transform.position + sunDir * 100000f);
            if (vp.z > 0f)
            {
                float off = Mathf.Max(Mathf.Abs(vp.x - 0.5f), Mathf.Abs(vp.y - 0.5f));
                gain = rays * Mathf.Clamp01(1.5f - off * 2f);   // full in the frame, gone a quarter frame past the edge
            }
            if (gain > 0.001f)
            {
                _mat.SetVector("_SunUV", new Vector4(vp.x, vp.y, 0f, 0f));
                var r2 = RenderTexture.GetTemporary(w, h, 0, src.format);
                Graphics.Blit(src, r, _mat, 4);
                Graphics.Blit(r, r2, _mat, 5);
                Graphics.Blit(r2, r, _mat, 5);
                RenderTexture.ReleaseTemporary(r2);
            }
        }
        _mat.SetFloat("_RayGain", gain);
        _mat.SetTexture("_Rays", gain > 0.001f ? (Texture)r : Texture2D.blackTexture);
        Graphics.Blit(src, dst, _mat, 3);
        if (dust != null) RenderTexture.ReleaseTemporary(dust);
        RenderTexture.ReleaseTemporary(r);
        RenderTexture.ReleaseTemporary(a);
        RenderTexture.ReleaseTemporary(b);
    }
}
