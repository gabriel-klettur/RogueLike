using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Combo badge for the bottom-left HUD column — it sits directly above the
    /// unified player panel and shows the live hit streak: the count, the tier
    /// title it has earned, a pip per rung of the ladder, and a bar draining
    /// with the real combo window.
    ///
    /// Drives entirely off <see cref="ComboCounter"/> events; the per-frame work
    /// is limited to the fade, the punch spring and the drain bar, and stops
    /// completely (panel deactivated) once the badge has faded out.
    ///
    /// Binding is self-healing: if no counter is supplied, or the bound one is
    /// destroyed with the player, the badge re-resolves it from
    /// <see cref="EntityRegistry.Player"/> a few times a second instead of every
    /// frame. The visual ladder lives in <c>ComboHUD.Tiers.cs</c>, the hierarchy
    /// in <c>ComboHUD.UIBuilder.cs</c>, the sprites in
    /// <c>ComboHUD.SpriteFactory.cs</c>.
    /// </summary>
    public sealed partial class ComboHUD : MonoBehaviour
    {
        [Header("Visibility")]
        [SerializeField, Tooltip("Streak length required before the badge appears at all. " +
                                 "2 keeps single hits from flashing it on constantly.")]
        private int minCountToShow = 2;

        [SerializeField, Tooltip("Seconds the badge lingers after the combo breaks, so the " +
                                 "player gets to read the final number.")]
        private float holdAfterBreak = 0.9f;

        [SerializeField, Tooltip("Fade in/out rate. Higher is snappier.")]
        private float fadeSpeed = 11f;

        [Header("Punch spring")]
        [SerializeField, Tooltip("How hard the number is pulled back to rest scale.")]
        private float punchStiffness = 220f;

        [SerializeField, Tooltip("Spring damping. Lower overshoots more.")]
        private float punchDamping = 17f;

        [Header("Break flash")]
        [SerializeField, Tooltip("Colour the number flashes to when the streak drops.")]
        private Color breakColor = new Color(0.96f, 0.26f, 0.20f, 1f);

        [SerializeField, Tooltip("Seconds the break flash + shake runs for.")]
        private float breakFlashDuration = 0.45f;

        [SerializeField, Tooltip("Peak horizontal shake of the panel on a break, in pixels.")]
        private float breakShakeAmplitude = 5f;

        [SerializeField, Tooltip("Shake oscillations per second on a break.")]
        private float breakShakeFrequency = 34f;

        [Header("Drain bar")]
        [SerializeField, Tooltip("Colour the drain bar shifts to as the window runs out.")]
        private Color timerDangerColor = new Color(1f, 0.29f, 0.24f, 1f);

        [SerializeField, Range(0f, 1f), Tooltip("Window fraction below which the bar turns to the danger colour.")]
        private float timerDangerThreshold = 0.32f;

        // ── Runtime state ─────────────────────────────────────────────────
        private const float RebindInterval = 0.35f;
        private const float MaxSpringStep  = 0.05f;   // clamps a hitching frame

        private ComboCounter _combo;
        private ComboTier    _tier;
        private int          _displayedCount;
        private float        _alpha;
        private float        _scale = 1f;
        private float        _scaleVelocity;
        private float        _breakTimer;
        private float        _holdTimer;
        private float        _rebindTimer;
        private bool         _panelVisible = true;

        // ── Public surface ────────────────────────────────────────────────

        /// <summary>The counter currently driving the badge, or null when unbound.</summary>
        public ComboCounter BoundCounter => _combo;

        /// <summary>Streak the badge is showing (may lag the counter during a break hold).</summary>
        public int DisplayedCount => _displayedCount;

        /// <summary>Current fade level: 0 fully hidden, 1 fully shown.</summary>
        public float Alpha => _alpha;

        /// <summary>
        /// Bind to a combo counter. Passing null unbinds and lets the badge
        /// auto-resolve the player's counter on its next poll.
        /// </summary>
        public void Bind(ComboCounter combo)
        {
            // Callers bind straight after AddComponent. Awake happens to have run
            // by then in play mode, but relying on that leaves the label and pips
            // unwritten anywhere Awake does not fire — EditMode tests included.
            EnsureBuilt();

            Unbind();
            _combo = combo;
            if (_combo == null) return;

            _combo.OnComboChanged += HandleComboChanged;
            _combo.OnComboReset   += HandleComboReset;

            _displayedCount = _combo.Current;
            ApplyTier(ResolveTier(_displayedCount), force: true);
            if (_countText != null) _countText.text = CountLabel(_displayedCount);
            UpdatePips(_displayedCount);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            EnsureBuilt();
            ApplyTier(ResolveTier(0), force: true);
            SetPanelVisible(false);
        }

        private void OnEnable()
        {
            if (_combo == null) TryAutoBind();
        }

        private void OnDestroy() => Unbind();

        private void Update() => Tick(Time.deltaTime);

        // ── Frame driver (public seam so EditMode tests can step it) ──────

        /// <summary>Advance the badge by <paramref name="deltaTime"/> seconds.</summary>
        public void Tick(float deltaTime)
        {
            MaintainBinding(deltaTime);

            bool streakLive = _combo != null && _combo.IsActive && _combo.Current >= minCountToShow;
            if (streakLive) _holdTimer = holdAfterBreak;
            else if (_holdTimer > 0f) _holdTimer = Mathf.Max(0f, _holdTimer - deltaTime);

            float target = (streakLive || _holdTimer > 0f) ? 1f : 0f;

            // Frame-rate independent exponential approach, then snap so the
            // panel can actually reach 0 and switch itself off.
            _alpha = Mathf.Lerp(_alpha, target, 1f - Mathf.Exp(-fadeSpeed * deltaTime));
            if (Mathf.Abs(_alpha - target) < 0.004f) _alpha = target;
            if (_canvasGroup != null) _canvasGroup.alpha = _alpha;

            if (_alpha <= 0f && target <= 0f)
            {
                if (_panelVisible) ResetTransientVisuals();
                return;
            }

            SetPanelVisible(true);
            TickPunchSpring(deltaTime);
            TickDrainBar(deltaTime);
            TickBreakFlash(deltaTime);
            TickGlow();
        }

        // ── Per-frame pieces ──────────────────────────────────────────────

        private void TickPunchSpring(float deltaTime)
        {
            float step = Mathf.Min(deltaTime, MaxSpringStep);
            float accel = (1f - _scale) * punchStiffness - _scaleVelocity * punchDamping;
            _scaleVelocity += accel * step;
            _scale += _scaleVelocity * step;

            if (_countRt != null)
                _countRt.localScale = new Vector3(_scale, _scale, 1f);
        }

        private void TickDrainBar(float deltaTime)
        {
            if (_timerFillImage == null) return;

            float remaining = _combo != null ? _combo.WindowRemaining01 : 0f;
            float current   = _timerFillImage.fillAmount;

            // Draining is linear and truthful; refilling after a hit is eased so
            // the bar snaps back without a visual jump.
            _timerFillImage.fillAmount = remaining > current
                ? Mathf.Lerp(current, remaining, 1f - Mathf.Exp(-22f * deltaTime))
                : remaining;

            float danger = timerDangerThreshold > 0f
                ? 1f - Mathf.Clamp01(remaining / timerDangerThreshold)
                : 0f;
            _timerFillImage.color = Color.Lerp(CurrentTier.Color, timerDangerColor, danger);
        }

        private void TickBreakFlash(float deltaTime)
        {
            if (_breakTimer <= 0f) return;

            _breakTimer = Mathf.Max(0f, _breakTimer - deltaTime);
            float k = breakFlashDuration > 0f ? Mathf.Clamp01(_breakTimer / breakFlashDuration) : 0f;

            if (_countText != null)
                _countText.color = Color.Lerp(CurrentTier.Color, breakColor, k);

            if (_panelRt != null)
            {
                float offset = Mathf.Sin(_breakTimer * breakShakeFrequency) * breakShakeAmplitude * k;
                _panelRt.anchoredPosition = new Vector2(offset, 0f);
            }

            if (_breakTimer <= 0f) ClearBreakVisuals();
        }

        private void TickGlow()
        {
            if (_glowImage == null) return;

            var tier = CurrentTier;
            // Slow breathing pulse, plus a kick proportional to the punch so a
            // fresh hit lights the halo up before the spring settles.
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 3.6f);
            float kick  = Mathf.Max(0f, _scale - 1f) * 1.6f;
            float a     = Mathf.Clamp01((tier.GlowStrength * pulse) + kick) * _alpha;

            var color = tier.GlowColor;
            color.a = a;
            _glowImage.color = color;
        }

        // ── Counter events ────────────────────────────────────────────────

        private void HandleComboChanged(int count)
        {
            _displayedCount = count;

            var tier = ResolveTier(count);
            ApplyTier(tier, force: false);

            if (_countText != null) _countText.text = CountLabel(count);
            UpdatePips(count);

            // A fresh hit cancels a break flash that was still playing.
            if (_breakTimer > 0f) { _breakTimer = 0f; ClearBreakVisuals(); }

            // Snap to the tier's punch scale and let the spring pull it back —
            // that overshoot on the way down is what makes it read as a hit.
            _scale = tier.PunchScale;
            _scaleVelocity = 0f;
        }

        private void HandleComboReset(int finalCount)
        {
            if (finalCount < minCountToShow)
            {
                _holdTimer = 0f;
                return;
            }

            _displayedCount = finalCount;
            if (_countText != null) _countText.text = CountLabel(finalCount);

            _breakTimer = breakFlashDuration;
            _holdTimer  = holdAfterBreak;
        }

        // ── Painting ──────────────────────────────────────────────────────

        private void ApplyTier(ComboTier tier, bool force)
        {
            if (tier == null) tier = FallbackTier;
            if (!force && ReferenceEquals(tier, _tier)) return;
            _tier = tier;

            if (_countText != null && _breakTimer <= 0f) _countText.color = tier.Color;
            if (_titleText != null)
            {
                _titleText.text  = tier.Title;
                var titleColor   = tier.Color;
                titleColor.a     = 0.82f;
                _titleText.color = titleColor;
            }
            if (_accentImage != null) _accentImage.color = tier.Color;
            if (_edgeImage != null)
            {
                var edge = tier.Color;
                edge.a = 0.34f;
                _edgeImage.color = edge;
            }
        }

        private void UpdatePips(int count)
        {
            if (_pipImages == null) return;

            int reached = ResolveTierIndex(count);
            for (int i = 0; i < _pipImages.Length; i++)
            {
                var pip = _pipImages[i];
                if (pip == null) continue;

                if (i <= reached)
                {
                    var lit = CurrentTier.Color;
                    lit.a = 0.95f;
                    pip.color = lit;
                }
                else
                {
                    pip.color = PipOffColor;
                }
            }
        }

        private void ClearBreakVisuals()
        {
            _breakTimer = 0f;
            if (_countText != null) _countText.color = CurrentTier.Color;
            if (_panelRt != null) _panelRt.anchoredPosition = Vector2.zero;
        }

        private void ResetTransientVisuals()
        {
            ClearBreakVisuals();
            _scale = 1f;
            _scaleVelocity = 0f;
            if (_countRt != null) _countRt.localScale = Vector3.one;
            if (_timerFillImage != null) _timerFillImage.fillAmount = 0f;
            SetPanelVisible(false);
        }

        private void SetPanelVisible(bool visible)
        {
            if (_panelVisible == visible) return;
            _panelVisible = visible;
            if (_panelGo != null) _panelGo.SetActive(visible);
            if (_edgeImage != null) _edgeImage.gameObject.SetActive(visible);
        }

        // ── Binding ───────────────────────────────────────────────────────

        private void MaintainBinding(float deltaTime)
        {
            // Unity-null covers the counter dying with the player on respawn.
            if (_combo != null) return;

            _rebindTimer -= deltaTime;
            if (_rebindTimer > 0f) return;
            _rebindTimer = RebindInterval;

            TryAutoBind();
        }

        private void TryAutoBind()
        {
            var player = EntityRegistry.Player;
            if (player == null) return;

            var combo = player.GetComponent<ComboCounter>();
            if (combo != null) Bind(combo);
        }

        private void Unbind()
        {
            if (_combo != null)
            {
                _combo.OnComboChanged -= HandleComboChanged;
                _combo.OnComboReset   -= HandleComboReset;
            }
            _combo = null;
        }

        // ── Label cache ───────────────────────────────────────────────────
        // The count changes several times a second; building the string with an
        // interpolation each time would allocate on every hit for no reason.

        private const string CountSuffix = "<size=55%>x</size>";
        private static readonly string[] CountLabelCache = new string[100];

        private static string CountLabel(int count)
        {
            if (count < 0) count = 0;
            if (count >= CountLabelCache.Length) return count.ToString() + CountSuffix;

            return CountLabelCache[count] ??= count.ToString() + CountSuffix;
        }
    }
}
