// A stand-in for the slice of the Unity API the game uses, so the scripts compile (and the pure logic runs) without
// an editor. Signatures follow Unity 6 (UnityEngine, UnityEngine.UI, UnityEngine.EventSystems). The maths is real;
// everything that needs an engine (objects, rendering, input, files) does nothing.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-12f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static Vector3 Reflect(Vector3 a, Vector3 n) => a - n * (2f * Dot(a, n));
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
        public string ToString(string f) => "(" + x.ToString(f) + ", " + y.ToString(f) + ", " + z.ToString(f) + ")";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
        public override bool Equals(object o) => o is Vector4 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ w.GetHashCode();
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1f };
        public static Quaternion Inverse(Quaternion q) => q;
        public static Quaternion Euler(float x, float y, float z) => identity;
        public static Quaternion LookRotation(Vector3 f, Vector3 up) => identity;
        public static Quaternion LookRotation(Vector3 f) => identity;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion AngleAxis(float a, Vector3 axis) => identity;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity => new Matrix4x4 { m00 = 1, m11 = 1, m22 = 1, m33 = 1 };
        public static Matrix4x4 zero => new Matrix4x4();
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => new Matrix4x4 { m00 = s.x, m11 = s.y, m22 = s.z, m33 = 1, m03 = p.x, m13 = p.y, m23 = p.z };
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color magenta => new Color(1, 0, 1);
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a);
    }

    public struct Bounds
    {
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Asin(float x) => (float)Math.Asin(x);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float x) => (float)Math.Exp(x);
        public static float Abs(float x) => Math.Abs(x);
        public static float Sign(float x) => x >= 0f ? 1f : -1f;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Round(float x) => (float)Math.Round(x);
        public static float Floor(float x) => (float)Math.Floor(x);
        public static float Ceil(float x) => (float)Math.Ceiling(x);
        public static int RoundToInt(float x) => (int)Math.Round(x);
        public static int FloorToInt(float x) => (int)Math.Floor(x);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Repeat(float t, float len) => t - (float)Math.Floor(t / len) * len;
    }

    public static class Random
    {
        static readonly System.Random _r = new System.Random(1);
        public static float value => (float)_r.NextDouble();
        public static Vector3 onUnitSphere => new Vector3(value * 2 - 1, value * 2 - 1, value * 2 - 1).normalized;
        public static float Range(float a, float b) => a + (b - a) * value;
        public static int Range(int a, int b) => a + _r.Next(Math.Max(1, b - a));
    }

    public static class Time
    {
        public static float deltaTime => 1f / 60f;
        public static float smoothDeltaTime => 1f / 60f;
        public static float realtimeSinceStartup => (float)(DateTime.Now - _t0).TotalSeconds;
        public static float time => realtimeSinceStartup;
        public static int frameCount => 0;
        static readonly DateTime _t0 = DateTime.Now;
    }

    public static class Screen
    {
        public static int width => 1280;
        public static int height => 720;
    }

    public enum KeyCode { A, B, C, D, E, F, G, H, I, L, N, Q, R, S, T, W, X, Space, Escape, Return, Tab, F5, UpArrow, DownArrow, LeftShift, RightShift }

    public static class Input
    {
        public static Vector3 mousePosition => Vector3.zero;
        public static bool GetKey(KeyCode k) => false;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButton(int b) => false;
    }

    public enum Space { World, Self }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum CameraClearFlags { Skybox, SolidColor, Depth, Nothing }
    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Hard, Soft }
    public enum ShadowQuality { Disable, HardOnly, All }
    public enum ShadowResolution { Low, Medium, High, VeryHigh }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public enum FindObjectsSortMode { None, InstanceID }

    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object o) { }
        public static T Instantiate<T>(T o) where T : Object => o;
        public static T Instantiate<T>(T o, Transform parent) where T : Object => o;
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsByType<T>() where T : Object => new T[0];
        public static implicit operator bool(Object o) => o != null;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => 0;
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] comps) { }
        public Transform transform => null;
        public string tag { get; set; }
        public bool activeSelf => true;
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default(T);
        public T[] GetComponentsInChildren<T>(bool inactive) => new T[0];
        public T GetComponentInChildren<T>() => default(T);
        public void SetActive(bool v) { }
        public static GameObject CreatePrimitive(PrimitiveType t) => null;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default(T);
        public T GetComponentInChildren<T>() => default(T);
        public T[] GetComponentsInChildren<T>(bool inactive) => new T[0];
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour { }

    public class Transform : Component, System.Collections.IEnumerable
    {
        public System.Collections.IEnumerator GetEnumerator() => new List<Transform>().GetEnumerator();
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 forward => Vector3.forward;
        public Vector3 up => Vector3.up;
        public Vector3 right => Vector3.right;
        public Transform parent { get; set; }
        public int childCount => 0;
        public Transform GetChild(int i) => null;
        public Vector3 InverseTransformPoint(Vector3 p) => p;
        public void SetParent(Transform t, bool keep) { }
        public void Rotate(Vector3 axis, float angle, Space s) { }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
    }

    public class Camera : Behaviour
    {
        public static Camera main => null;
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public float fieldOfView { get; set; }
        public bool allowHDR { get; set; }
    }

    public class AudioListener : Behaviour
    {
        public static float volume { get; set; }
    }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public Color color { get; set; }
        public float intensity { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public float shadowBias { get; set; }
        public float shadowNormalBias { get; set; }
        public float range { get; set; }
        public float spotAngle { get; set; }
        public float innerSpotAngle { get; set; }
    }

    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static bool fog { get; set; }
        public static Material skybox { get; set; }
        public static Rendering.DefaultReflectionMode defaultReflectionMode { get; set; }
        public static int defaultReflectionResolution { get; set; }
    }

    public static class QualitySettings
    {
        public static float shadowDistance { get; set; }
        public static int shadowCascades { get; set; }
        public static ShadowQuality shadows { get; set; }
        public static ShadowResolution shadowResolution { get; set; }
    }

    public class Shader : Object
    {
        public bool isSupported => true;
        public static Shader Find(string name) => new Shader { name = name };
        public static void SetGlobalFloat(string name, float v) { }
    }

    public class Material : Object
    {
        public Material(Shader s) { }
        public Material(Material m) { }
        public Shader shader { get; set; }
        public Color color { get; set; }
        public bool enableInstancing { get; set; }
        public bool HasProperty(string n) => false;
        public Texture GetTexture(string n) => null;
        public void SetTexture(string n, Texture t) { }
        public void SetVector(string n, Vector4 v) { }
        public Color GetColor(string n) => new Color();
        public float GetFloat(string n) => 0f;
        public void SetFloat(string n, float v) { }
        public void SetColor(string n, Color c) { }
        public void EnableKeyword(string k) { }
    }

    public class MaterialPropertyBlock
    {
        public void SetVectorArray(string n, Vector4[] v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetFloat(string n, float v) { }
    }

    public class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public int[] triangles { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public int subMeshCount => 1;
        public Bounds bounds { get; set; }
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetTriangles(List<int> t, int sub) { triangles = t.ToArray(); }
        public void RecalculateNormals() { }
        public void RecalculateBounds()
        {
            if (vertices == null || vertices.Length == 0) return;
            var lo = vertices[0]; var hi = vertices[0];
            foreach (var v in vertices)
            {
                lo = new Vector3(Math.Min(lo.x, v.x), Math.Min(lo.y, v.y), Math.Min(lo.z, v.z));
                hi = new Vector3(Math.Max(hi.x, v.x), Math.Max(hi.y, v.y), Math.Max(hi.z, v.z));
            }
            bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
        }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Collider : Component { }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material[] sharedMaterials { get; set; }
        public void SetPropertyBlock(MaterialPropertyBlock b) { }
        public Material sharedMaterial { get; set; }
        public Material material { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public class LineRenderer : Renderer
    {
        public bool useWorldSpace { get; set; }
        public int positionCount { get; set; }
        public float startWidth { get; set; }
        public float endWidth { get; set; }
        public Color startColor { get; set; }
        public Color endColor { get; set; }
        public bool enabled { get; set; }
        public void SetPosition(int i, Vector3 p) { }
    }

    public struct RenderParams
    {
        public RenderParams(Material m) { worldBounds = new Bounds(); matProps = null; shadowCastingMode = Rendering.ShadowCastingMode.On; receiveShadows = true; layer = 0; }
        public Bounds worldBounds;
        public MaterialPropertyBlock matProps;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public int layer;
    }

    public static class SystemInfo
    {
        public static string graphicsDeviceType => "stub";
        public static bool supportsInstancing => true;
    }

    public static class Graphics
    {
        public static int calls;
        public static void Blit(Texture src, RenderTexture dst) { }
        public static void Blit(Texture src, RenderTexture dst, Material m, int pass) { }
        public static void RenderMeshInstanced<T>(RenderParams rp, Mesh mesh, int sub, T[] data, int count, int start) where T : unmanaged { calls++; }
        public static void DrawMeshInstanced(Mesh mesh, int sub, Material mat, Matrix4x4[] mats, int count, MaterialPropertyBlock mpb, Rendering.ShadowCastingMode sc, bool rs, int layer, Camera cam) { calls++; }
    }

    public static class Debug
    {
        public static void Log(object o) { Console.WriteLine(o); }
        public static void LogWarning(object o) { Console.WriteLine("WARNING: " + o); }
    }

    public static class Application
    {
        public static string persistentDataPath => System.IO.Path.GetTempPath();
        public static void Quit() { }
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => "{}";
        public static T FromJson<T>(string s) => default(T);
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string s, out Color c)
        {
            c = new Color();
            if (s.Length != 7 || s[0] != '#') return false;
            c.r = Convert.ToInt32(s.Substring(1, 2), 16) / 255f;
            c.g = Convert.ToInt32(s.Substring(3, 2), 16) / 255f;
            c.b = Convert.ToInt32(s.Substring(5, 2), 16) / 255f;
            c.a = 1f;
            return true;
        }
    }

    public class Font : Object { }
    public class Texture : Object { }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
        public static T Load<T>(string path) where T : Object => null;
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string path) { }
    }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
    }

    namespace Rendering
    {
        public enum AmbientMode { Skybox, Trilight, Flat, Custom }
        public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
        public enum IndexFormat { UInt16, UInt32 }
        public enum DefaultReflectionMode { Skybox, Custom }
    }

    namespace Events
    {
        public class UnityEvent
        {
            public void AddListener(Action a) { }
        }
    }

    namespace EventSystems
    {
        public class EventSystem : MonoBehaviour { }
        public class StandaloneInputModule : MonoBehaviour { }
    }

    namespace UI
    {
        public class CanvasScaler : Behaviour
        {
            public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
            public ScaleMode uiScaleMode { get; set; }
            public Vector2 referenceResolution { get; set; }
            public float matchWidthOrHeight { get; set; }
        }
        public class GraphicRaycaster : Behaviour { }
        public class Graphic : Behaviour
        {
            public Color color { get; set; }
            public bool raycastTarget { get; set; }
            public RectTransform rectTransform => null;
        }
        public class Image : Graphic { }
        public class Text : Graphic
        {
            public Font font { get; set; }
            public int fontSize { get; set; }
            public TextAnchor alignment { get; set; }
            public string text { get; set; }
            public HorizontalWrapMode horizontalOverflow { get; set; }
            public VerticalWrapMode verticalOverflow { get; set; }
        }
        public struct ColorBlock
        {
            public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor;
        }
        public class Button : Behaviour
        {
            public bool interactable { get; set; }
            public ColorBlock colors { get; set; }
            public Events.UnityEvent onClick => new Events.UnityEvent();
        }
    }
}

namespace UnityEngine
{
    public class AudioClip : Object { }
    public enum AudioReverbPreset { Off, Generic, Hangar, Room }
    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public float spatialBlend { get; set; }
        public bool isPlaying => false;
        public void Play() { }
        public void PlayDelayed(float s) { }
        public void Stop() { }
    }
    public class AudioHighPassFilter : Behaviour { public float cutoffFrequency { get; set; } }
    public class AudioLowPassFilter : Behaviour { public float cutoffFrequency { get; set; } }
    public class AudioDistortionFilter : Behaviour { public float distortionLevel { get; set; } }
    public class AudioReverbFilter : Behaviour { public AudioReverbPreset reverbPreset { get; set; } public float dryLevel { get; set; } public float room { get; set; } }
}

namespace UnityEngine
{
    public class RenderTexture : Texture
    {
        public int width => 0;
        public int height => 0;
        public RenderTextureFormat format => RenderTextureFormat.Default;
        public static RenderTexture GetTemporary(int w, int h, int depth, RenderTextureFormat f) => null;
        public static void ReleaseTemporary(RenderTexture t) { }
    }
    public enum RenderTextureFormat { Default, ARGBHalf }
    public static class DynamicGI
    {
        public static void UpdateEnvironment() { }
    }
    public static partial class GraphicsExt { }
}
