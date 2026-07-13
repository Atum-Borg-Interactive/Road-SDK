using UnityEngine;

namespace RoadPro
{
    public static class ShaderCache
    {
        static Shader _urpUnlit;
        static Shader _unlitColor;
        static Shader _standard;
        static Shader _spritesDefault;
        static Shader _roadLit;
        static Shader _roadPreview;

        static bool _initialized;

        public static Shader URPUnlit => Get(ref _urpUnlit, "Universal Render Pipeline/Unlit");
        public static Shader UnlitColor => Get(ref _unlitColor, "Unlit/Color");
        public static Shader Standard => Get(ref _standard, "Standard");
        public static Shader SpritesDefault => Get(ref _spritesDefault, "Sprites/Default");
        public static Shader RoadLit => Get(ref _roadLit, "RoadPro/RoadLit");
        public static Shader RoadPreview => Get(ref _roadPreview, "RoadPro/RoadPreview");

        public static void WarmUp()
        {
            if (_initialized) return;
            _initialized = true;

            _urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            _unlitColor = Shader.Find("Unlit/Color");
            _standard = Shader.Find("Standard");
            _spritesDefault = Shader.Find("Sprites/Default");
            _roadLit = Shader.Find("RoadPro/RoadLit");
            _roadPreview = Shader.Find("RoadPro/RoadPreview");

            Debug.Log($"[ShaderCache] URP Unlit: {(_urpUnlit != null ? "OK" : "MISSING")}");
            Debug.Log($"[ShaderCache] RoadLit: {(_roadLit != null ? "OK" : "MISSING")}");
            Debug.Log($"[ShaderCache] RoadPreview: {(_roadPreview != null ? "OK" : "MISSING")}");
        }

        static Shader Get(ref Shader cached, string name)
        {
            if (cached == null)
            {
                cached = Shader.Find(name);
                if (cached == null)
                    Debug.LogWarning($"[ShaderCache] Shader not found: {name}");
            }
            return cached;
        }

        public static Shader GetUnlitFallback()
        {
            return URPUnlit ?? UnlitColor ?? Standard;
        }

        public static Material CreateLitNoReflection(Color color)
        {
            var shader = RoadLit ?? URPUnlit ?? UnlitColor;
            if (shader == null) return new Material(Shader.Find("Standard"));
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        public static Material CreateUnlitMaterial(Color color)
        {
            var shader = GetUnlitFallback();
            if (shader == null)
            {
                Debug.LogError("[ShaderCache] No usable unlit shader found!");
                return new Material(Shader.Find("Standard"));
            }
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        public static Material CreateTransparentMaterial(Color color)
        {
            var shader = GetUnlitFallback();
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            return mat;
        }
    }
}
