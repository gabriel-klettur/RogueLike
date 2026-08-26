using System.Reflection;
using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// FireballVisual — URP Light2D reflection partial.
    /// Owns all static PropertyInfo caches and creates the per-instance dynamic light.
    /// Domain Reload OFF: static reflection cache is reset via SubsystemRegistration.
    /// </summary>
    public partial class FireballVisual
    {
        // ── URP Light2D reflection (shared) ───────────────────────────
        private static System.Type _light2DType;
        private static PropertyInfo _l2dLightType;
        private static PropertyInfo _l2dColor;
        private static PropertyInfo _l2dIntensity;
        private static PropertyInfo _l2dOuter;
        private static PropertyInfo _l2dInner;
        private static PropertyInfo _l2dFalloff;
        private static bool _l2dResolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticLight()
        {
            _light2DType  = null;
            _l2dLightType = null;
            _l2dColor     = null;
            _l2dIntensity = null;
            _l2dOuter     = null;
            _l2dInner     = null;
            _l2dFalloff   = null;
            _l2dResolved  = false;
        }

        private static void ResolveLight2D()
        {
            if (_l2dResolved) return;
            _l2dResolved = true;
            _light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (_light2DType == null) return;

            var flags = BindingFlags.Public | BindingFlags.Instance;
            _l2dLightType = _light2DType.GetProperty("lightType",              flags);
            _l2dColor     = _light2DType.GetProperty("color",                  flags);
            _l2dIntensity = _light2DType.GetProperty("intensity",              flags);
            _l2dOuter     = _light2DType.GetProperty("pointLightOuterRadius",  flags);
            _l2dInner     = _light2DType.GetProperty("pointLightInnerRadius",  flags);
            _l2dFalloff   = _light2DType.GetProperty("falloffIntensity",       flags);
        }

        private void CreateDynamicLight()
        {
            if (_light2DType == null) return;

            _light2DGo = new GameObject("FireballLight");
            _light2DGo.transform.SetParent(transform, false);
            _light2DGo.transform.localPosition = Vector3.zero;

            try
            {
                _light2DComponent = _light2DGo.AddComponent(_light2DType);
                if (_l2dLightType != null)
                {
                    var enumType = _l2dLightType.PropertyType;
                    _l2dLightType.SetValue(_light2DComponent, System.Enum.ToObject(enumType, 3)); // 3 = Point (URP 14: Sprite=2)
                }
                if (_l2dColor     != null) _l2dColor.SetValue(_light2DComponent, new Color(1f, 0.55f, 0.15f, 1f));
                if (_l2dIntensity != null) _l2dIntensity.SetValue(_light2DComponent, LightIntensity);
                if (_l2dOuter     != null) _l2dOuter.SetValue(_light2DComponent, LightOuterRadius);
                if (_l2dInner     != null) _l2dInner.SetValue(_light2DComponent, LightInnerRadius);
                if (_l2dFalloff   != null) _l2dFalloff.SetValue(_light2DComponent, 0.9f);
            }
            catch
            {
                if (_light2DGo != null) Destroy(_light2DGo);
                _light2DGo        = null;
                _light2DComponent = null;
            }
        }

        private void TickLightFlicker(float t)
        {
            if (_light2DComponent == null || _l2dIntensity == null) return;
            try
            {
                float intensity = LightIntensity * (0.85f + 0.15f * Mathf.Sin(t * 24f) + 0.10f * Mathf.Sin(t * 13f));
                _l2dIntensity.SetValue(_light2DComponent, intensity);
            }
            catch { /* reflection safety */ }
        }

        // ── Accessors forwarded to FireballImpactFX ───────────────────
        internal static System.Type GetLight2DType()           { ResolveLight2D(); return _light2DType; }
        internal static PropertyInfo GetLight2DLightTypeProp() { ResolveLight2D(); return _l2dLightType; }
        internal static PropertyInfo GetLight2DColorProp()     { ResolveLight2D(); return _l2dColor; }
        internal static PropertyInfo GetLight2DIntensityProp() { ResolveLight2D(); return _l2dIntensity; }
        internal static PropertyInfo GetLight2DOuterProp()     { ResolveLight2D(); return _l2dOuter; }
        internal static PropertyInfo GetLight2DInnerProp()     { ResolveLight2D(); return _l2dInner; }
        internal static PropertyInfo GetLight2DFalloffProp()   { ResolveLight2D(); return _l2dFalloff; }
    }
}
