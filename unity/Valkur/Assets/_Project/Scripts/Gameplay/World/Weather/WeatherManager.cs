using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Singleton orchestrator for the weather effect components. Owns the live
    /// instances of <see cref="WindEffect"/> / <see cref="RainEffect"/> /
    /// <see cref="SnowEffect"/>, exposes a small <see cref="Set"/> /
    /// <see cref="IsActive"/> API, and fires <see cref="OnWeatherChanged"/> when
    /// any flag flips so HUD widgets and gameplay systems can react.
    ///
    /// Effects can stack freely — Wind + Rain reads as a wind-driven rainstorm.
    /// </summary>
    public sealed class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance         = null;
            OnWeatherChanged = null;
        }

        // Effect instances populated lazily on first request.
        private readonly Dictionary<WeatherType, WeatherEffect> _effects = new();

        public static event System.Action<WeatherType, bool> OnWeatherChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>True iff <paramref name="type"/> is currently fading-in or fully active.</summary>
        public bool IsActive(WeatherType type)
            => _effects.TryGetValue(type, out var fx) && fx != null && fx.IsActive;

        /// <summary>Activate or deactivate a weather. Multiple weathers may run simultaneously.</summary>
        public void Set(WeatherType type, bool active)
        {
            var fx = ResolveOrCreate(type);
            if (fx == null) return;
            if (active) fx.Activate(); else fx.Deactivate();
            OnWeatherChanged?.Invoke(type, active);
        }

        /// <summary>Toggle a weather. Returns the new state.</summary>
        public bool Toggle(WeatherType type)
        {
            bool next = !IsActive(type);
            Set(type, next);
            return next;
        }

        /// <summary>Disable every active weather in one call.</summary>
        public void ClearAll()
        {
            foreach (var kv in _effects)
            {
                if (kv.Value == null) continue;
                if (!kv.Value.IsActive) continue;
                kv.Value.Deactivate();
                OnWeatherChanged?.Invoke(kv.Key, false);
            }
        }

        // Lazy creation: each effect lives on its own child GameObject so the
        // ParticleSystem component sits on a dedicated transform (matches the
        // RequireComponent contract on each WeatherEffect subclass) and so
        // disabling a child is a clean kill switch when needed.
        private WeatherEffect ResolveOrCreate(WeatherType type)
        {
            if (_effects.TryGetValue(type, out var fx) && fx != null) return fx;

            var go = new GameObject($"Weather_{type}", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            switch (type)
            {
                case WeatherType.Wind: fx = go.AddComponent<WindEffect>(); break;
                case WeatherType.Rain: fx = go.AddComponent<RainEffect>(); break;
                case WeatherType.Snow: fx = go.AddComponent<SnowEffect>(); break;
                default:
                    Debug.LogWarning($"[WeatherManager] Unknown weather type {type}.");
                    Destroy(go);
                    return null;
            }
            _effects[type] = fx;
            return fx;
        }
    }
}
