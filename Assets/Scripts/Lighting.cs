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
        if (zoneId == "hub") return new Profile { color = Data.Hex("#fff6eb"), intensity = 5.0f, radius = 0.0085f, exposure = 1.05f };
        return new Profile { color = Data.Hex("#fff3e3"), intensity = 5.3f, radius = 0.0090f, exposure = 1.05f };
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
        // the browser's fill is a faint hemisphere bounce and an ambient of 0.025 through a Lambert with 1/pi: a fixed
        // colour of about that strength here, with the hulls' reflections from the sky
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.61f, 0.60f) * 0.06f;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 128;
        RenderSettings.fog = false;
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
        sun.intensity = 1.7f * p.intensity / 5.2f;
        if (post != null) post.exposure = p.exposure;
        if (sky != null)
        {
            sky.SetVector("_SunDir", dir);
            sky.SetFloat("_SunRadius", p.radius);
            sky.SetColor("_SunColor", Color.Lerp(p.color, Color.white, 0.3f));
            sky.SetColor("_BaseColor", Color.Lerp(z.bg, new Color(0.02f, 0.027f, 0.063f), 0.5f));
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
    public float exposure = 1.05f;
    public float threshold = 1.15f;
    public float intensity = 0.55f;
    Material _mat;

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
        int w = src.width / 4, h = src.height / 4;
        var a = RenderTexture.GetTemporary(w, h, 0, src.format);
        var b = RenderTexture.GetTemporary(w, h, 0, src.format);
        Graphics.Blit(src, a, _mat, 0);
        Graphics.Blit(a, b, _mat, 1);
        Graphics.Blit(b, a, _mat, 2);
        Graphics.Blit(a, b, _mat, 1);
        Graphics.Blit(b, a, _mat, 2);
        _mat.SetTexture("_Bloom", a);
        Graphics.Blit(src, dst, _mat, 3);
        RenderTexture.ReleaseTemporary(a);
        RenderTexture.ReleaseTemporary(b);
    }
}
