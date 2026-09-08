using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.UI
{
    /// <summary>
    /// Minimal in-game death feedback. Replaces the old <c>DeathScreenUI</c>
    /// red overlay + Restart/Menu modal.
    ///
    /// Behaviour:
    ///   - On <see cref="GameEvents.OnPlayerDied"/>: flash "HAS MUERTO"
    ///     centred for <see cref="flashDuration"/> seconds, then transition
    ///     to a thin persistent banner pinned to the top of the screen
    ///     reading "Encuentra el altar para revivir".
    ///   - On <see cref="GameEvents.OnPlayerRevived"/>: fade everything out.
    ///
    /// Does NOT pause time, does NOT consume input, does NOT block raycasts.
    /// The player has to be free to move as a spirit.
    /// </summary>
    public partial class DeathBannerUI : SingletonMonoBehaviour<DeathBannerUI>
    {
        [Header("Timings")]
        [SerializeField] private float flashDuration   = 0.6f;
        [SerializeField] private float flashFadeIn     = 0.15f;
        [SerializeField] private float flashFadeOut    = 0.4f;
        [SerializeField] private float bannerFadeIn    = 0.3f;
        [SerializeField] private float bannerFadeOut   = 0.4f;

        /// <summary>The standing instruction, and the fallback for every state that is not "stuck".</summary>
        private const string BannerFindAltar = "Encuentra el altar para revivir";

        /// <summary>
        /// How close the rescue has to be before the banner shows its clock. Below this it is
        /// information; above it, it is a countdown over the player's whole walk.
        /// </summary>
        private const float RescueCountdownVisibleSeconds = 20f;

        [Header("Style")]
        [SerializeField] private Color flashColor   = new Color(0.9f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color bannerColor  = new Color(0.95f, 0.85f, 0.45f, 0.85f);
        [SerializeField] private Color bannerStripBg = new Color(0f, 0f, 0f, 0.55f);

        private Canvas _canvas;
        private CanvasGroup _flashGroup;
        private CanvasGroup _bannerGroup;
        private TextMeshProUGUI _flashText;
        private TextMeshProUGUI _bannerText;
        private Coroutine _activeFlash;
        private Coroutine _activeBanner;

        protected override void OnSingletonAwake()
        {
            BuildUI();
            HideImmediate();
        }

        private void OnEnable()
        {
            GameEvents.OnPlayerDied    += OnPlayerDied;
            GameEvents.OnPlayerRevived += OnPlayerRevived;
        }

        protected override void OnDestroy()
        {
            GameEvents.OnPlayerDied    -= OnPlayerDied;
            GameEvents.OnPlayerRevived -= OnPlayerRevived;
            base.OnDestroy();
        }

        private void OnPlayerDied()
        {
            if (_activeFlash != null) StopCoroutine(_activeFlash);
            _activeFlash = StartCoroutine(FlashRoutine());

            if (_activeBanner != null) StopCoroutine(_activeBanner);
            _activeBanner = StartCoroutine(BannerFadeRoutine(toAlpha: 1f, bannerFadeIn, delay: flashDuration * 0.5f));
        }

        /// <summary>
        /// Safety net: if the banner is visible but the player is alive AND not
        /// in spirit form, the OnPlayerRevived event was probably missed (e.g.
        /// mid-Play recompile, devconsole resurrect on the legacy path). Hide
        /// the banner so it doesn't stay pinned forever.
        /// </summary>
        private void Update()
        {
            if (_bannerGroup == null || _bannerGroup.alpha < 0.05f) return;

            RefreshBannerMessage();

            var player = EntityRegistry.Player;
            if (player == null) return;
            var health = player.GetComponent<Health>();
            var spirit = player.GetComponent<PlayerSpiritState>();
            bool isSpirit = spirit != null && spirit.IsSpirit;
            bool isDead   = health != null && health.IsDead;
            if (!isSpirit && !isDead)
            {
                if (_activeFlash  != null) StopCoroutine(_activeFlash);
                if (_activeBanner != null) StopCoroutine(_activeBanner);
                _activeFlash  = StartCoroutine(GroupFadeRoutine(_flashGroup, 0f, flashFadeOut));
                _activeBanner = StartCoroutine(GroupFadeRoutine(_bannerGroup, 0f, bannerFadeOut));
            }
        }

        /// <summary>
        /// What the banner SAYS, re-decided every frame it is up.
        ///
        /// <para>It shipped as one fixed line — "Encuentra el altar para revivir" — pointing at a
        /// thing that, in the world as shipped, did not exist: zero altars over 301 buildings. A
        /// standing instruction that cannot be followed is worse than no instruction, because the
        /// player spends the whole spirit walk believing the failure is theirs. When there is no
        /// altar the banner says so and COUNTS DOWN the rescue, so the one guarantee the flow
        /// makes is visible while it is being kept rather than only after.</para>
        /// </summary>
        private void RefreshBannerMessage()
        {
            if (_bannerText == null) return;

            var death = ServiceLocator.Get<DeathSequenceController>();
            if (death == null || death.CurrentPhase != DeathSequenceController.Phase.Spirit)
            {
                _bannerText.text = BannerFindAltar;
                return;
            }

            float eta = death.RescueEta;
            bool noAltar = death.NoAltarInWorld;

            if (noAltar)
            {
                _bannerText.text = float.IsPositiveInfinity(eta)
                    ? "No hay altar en este mundo"
                    : $"No hay altar aqui — volveras en {Mathf.CeilToInt(eta)} s";
                return;
            }

            // The countdown only appears once it is short enough to be information rather than
            // pressure: a ninety-second number ticking from the moment of death turns the whole
            // walk into a timer, which is the opposite of what the safety net is for.
            if (!float.IsPositiveInfinity(eta) && eta <= RescueCountdownVisibleSeconds)
            {
                _bannerText.text = $"{BannerFindAltar} — {Mathf.CeilToInt(eta)} s";
                return;
            }

            _bannerText.text = BannerFindAltar;
        }

        private void OnPlayerRevived()
        {
            if (_activeFlash != null) StopCoroutine(_activeFlash);
            _activeFlash = StartCoroutine(GroupFadeRoutine(_flashGroup, 0f, flashFadeOut));

            if (_activeBanner != null) StopCoroutine(_activeBanner);
            _activeBanner = StartCoroutine(BannerFadeRoutine(toAlpha: 0f, bannerFadeOut, delay: 0f));
        }

        private IEnumerator FlashRoutine()
        {
            yield return GroupFadeRoutine(_flashGroup, 1f, flashFadeIn);
            float hold = Mathf.Max(0f, flashDuration - flashFadeIn - flashFadeOut);
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            yield return GroupFadeRoutine(_flashGroup, 0f, flashFadeOut);
        }

        private IEnumerator BannerFadeRoutine(float toAlpha, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            yield return GroupFadeRoutine(_bannerGroup, toAlpha, duration);
        }

        private IEnumerator GroupFadeRoutine(CanvasGroup group, float to, float duration)
        {
            if (group == null) yield break;
            float from = group.alpha;
            float t = 0f;
            float clamped = Mathf.Max(0.0001f, duration);
            while (t < clamped)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / clamped));
                yield return null;
            }
            group.alpha = to;
        }

        private void HideImmediate()
        {
            if (_flashGroup  != null) _flashGroup.alpha = 0f;
            if (_bannerGroup != null) _bannerGroup.alpha = 0f;
        }

        private partial void BuildUI();
    }
}
