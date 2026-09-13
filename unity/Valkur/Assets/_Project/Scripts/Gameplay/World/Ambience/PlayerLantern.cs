using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Sky;

namespace Valkur.Gameplay.World.Ambience
{
    /// <summary>
    /// The player's own light after dusk: a warm point light carried at chest height that
    /// swings a little with the walk, fading in as the daylight goes and out as it returns.
    ///
    /// Before this, a player outside the three lit zones stood at midnight in the ambient
    /// floor (<c>minIntensity 0.08</c>) and could not see the ground under their feet — not
    /// night, a monitor switched off. The lantern is what makes every other zone walkable at
    /// night and what makes the dark READ as dark: a pool of light with an edge is a night;
    /// a uniform dim frame is a broken monitor.
    ///
    /// Monsters carry none. In the dark they are meant to be found by the player's light,
    /// which is the whole tension of a night walk.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLantern : MonoBehaviour
    {
        /// <summary>Additive blend style, the one every placed torch uses. See WorldLightLoader.</summary>
        private const int AdditiveBlendStyle = 1;

        public const float OuterRadius = 5.5f;
        public const float InnerRadius = 0.0f;
        public const float Falloff     = 0.85f;

        /// <summary>
        /// Where the light hangs, relative to the player's feet. BELOW them, on the ground the
        /// player is about to walk on: a light carried at chest height sat the body at the
        /// pool's brightest point and washed it out — the character is what must stay legible
        /// in the dark, and the ground is what the lantern is for.
        /// </summary>
        private const float CarryHeight = -0.25f;

        /// <summary>
        /// Intensity at full night. Measured live: at 0.62 the pool blew the character out to a
        /// white blob — an additive light on a lit sprite ADDS on top of the ambient, and with
        /// the bloom's night threshold just above it the whole pool blossomed. 0.26 lights the
        /// ground to walk on and leaves the body legible; the town's torches stay brighter.
        /// </summary>
        public const float NightIntensity = 0.24f;

        /// <summary>How fast the light follows its target, per second. Dusk is a ramp, not a switch.</summary>
        private const float FollowRate = 1.6f;

        private static readonly Color LanternColour = new Color(1f, 0.80f, 0.52f, 1f);

        private Light2D _light;
        private Transform _carrier;
        private float _intensity;
        private float _time;
        private bool _built;

        /// <summary>The light itself. Test seam.</summary>
        public Light2D Light => _light;

        /// <summary>The intensity the lantern is heading for this frame. Test seam.</summary>
        public float TargetIntensity { get; private set; }

        /// <summary>Attach a lantern to <paramref name="player"/>. Idempotent.</summary>
        public static PlayerLantern Attach(GameObject player)
        {
            if (player == null) return null;
            var lantern = player.GetComponent<PlayerLantern>();
            if (lantern == null) lantern = player.AddComponent<PlayerLantern>();
            lantern.EnsureBuilt();
            return lantern;
        }

        private void Awake() => EnsureBuilt();

        /// <summary>
        /// Build the light. Public because Unity calls no Awake on a component added in Edit
        /// Mode, and the fixture has to reach the same code Play Mode does.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            var existing = transform.Find("Lantern");
            var go = existing != null ? existing.gameObject : new GameObject("Lantern");
            if (existing == null) go.transform.SetParent(transform, false);
            _carrier = go.transform;
            _carrier.localPosition = new Vector3(0f, CarryHeight, 0f);

            _light = go.GetComponent<Light2D>();
            if (_light == null) _light = go.AddComponent<Light2D>();
            _light.lightType             = Light2D.LightType.Point;
            _light.blendStyleIndex       = AdditiveBlendStyle;
            _light.pointLightOuterRadius = OuterRadius;
            _light.pointLightInnerRadius = InnerRadius;
            _light.falloffIntensity      = Falloff;
            _light.color                 = LanternColour;
            _light.intensity             = 0f;
            _light.shadowsEnabled        = false;
            _light.enabled               = false;
        }

        /// <summary>
        /// The intensity a lantern wants for a given daylight, whether the world's lights are
        /// on, and the switch. Pure, so the fixture can pin dusk without a clock.
        /// </summary>
        public static float IntensityFor(float daylight01, bool lightsOn, bool enabled)
        {
            if (!enabled || !lightsOn) return 0f;
            return NightIntensity * (1f - Mathf.Clamp01(daylight01));
        }

        private void Update()
        {
            bool lightsOn = !DayNightCycle.HasInstance || DayNightCycle.Instance.LightsEnabledNow;
            Tick(Time.deltaTime, SunShadowState.Daylight, lightsOn);
        }

        /// <summary>Advance one frame. Public so a test can drive dusk by hand.</summary>
        public void Tick(float dt, float daylight01, bool lightsOn)
        {
            if (_light == null) return;
            _time += dt;

            TargetIntensity = IntensityFor(daylight01, lightsOn, WorldLookSettings.Lantern);
            _intensity = Mathf.MoveTowards(_intensity, TargetIntensity, FollowRate * dt);

            bool on = _intensity > 0.005f;
            if (_light.enabled != on) _light.enabled = on;
            if (!on) return;

            _light.intensity = _intensity;
            // A carried light swings. Small, slow, two frequencies so it never metronomes.
            _carrier.localPosition = new Vector3(Mathf.Sin(_time * 1.3f) * 0.08f,
                                                 CarryHeight + Mathf.Sin(_time * 2.1f) * 0.04f, 0f);

            // A Light2D under a scaled root renders at authored radius x lossyScale, and the
            // classes do not share a scale (115 px / PPU 64 against 256 / 96, plus
            // scaleConfig.scaleIdle). Counter-scale so every class carries the same lantern —
            // the same undo WorldLightLoader applies to a fixture's light.
            var ls = transform.lossyScale;
            _carrier.localScale = new Vector3(1f / Mathf.Max(0.01f, ls.x), 1f / Mathf.Max(0.01f, ls.y), 1f);
        }
    }
}
