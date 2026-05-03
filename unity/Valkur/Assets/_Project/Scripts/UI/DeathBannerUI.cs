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
