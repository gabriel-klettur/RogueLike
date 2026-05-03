using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Top-of-screen boss health bar. Tracks a single "active boss"
    /// (whichever <see cref="BossPhaseController"/> the system is bound
    /// to) and renders its HP fraction + phase label in a Canvas overlay.
    /// Hides itself when no boss is bound, when the bound boss dies, or
    /// when the boss leaves the camera range.
    ///
    /// The companion <see cref="WorldHealthBar"/> floats above each NPC's
    /// head; this HUD is the larger always-visible bar that signals "you
    /// are fighting THIS boss right now". Designer wires a singleton
    /// instance into the gameplay scene; combat code calls
    /// <see cref="BindToBoss"/> when a boss aggros the player.
    ///
    /// Built procedurally at runtime so designers don't have to author
    /// a Canvas prefab. <see cref="EnsureBuilt"/> creates the Canvas +
    /// Image + Text hierarchy on first show.
    /// </summary>
    public sealed class BossHealthBarHUD : SingletonMonoBehaviour<BossHealthBarHUD>
    {
        private BossPhaseController _boundBoss;
        private Health _boundHealth;

        private Canvas _canvas;
        private GameObject _root;
        private Image _fill;
        private UnityEngine.UI.Text _label;

        protected override bool Persist => false;

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Bind the HUD to a boss. The bar will refresh every frame from
        /// the boss's Health and BossPhaseController. Passing null hides
        /// the bar (used when the boss dies or leaves combat).
        /// </summary>
        public void BindToBoss(BossPhaseController boss)
        {
            UnbindCurrent();
            _boundBoss = boss;
            if (_boundBoss != null)
            {
                _boundHealth = boss.GetComponent<Health>();
                EnsureBuilt();
                if (_root != null) _root.SetActive(true);
                Refresh();
                _boundBoss.OnPhaseChanged += OnPhaseChanged;
                if (_boundHealth != null) _boundHealth.OnHpChanged += OnHpChanged;
            }
            else
            {
                if (_root != null) _root.SetActive(false);
            }
        }

        /// <summary>True when the HUD is currently bound to a live boss.</summary>
        public bool IsActive => _boundBoss != null && _boundHealth != null && !_boundHealth.IsDead;

        // ── Internals ──────────────────────────────────────────────────────────

        private void UnbindCurrent()
        {
            if (_boundBoss != null) _boundBoss.OnPhaseChanged -= OnPhaseChanged;
            if (_boundHealth != null) _boundHealth.OnHpChanged -= OnHpChanged;
            _boundBoss = null;
            _boundHealth = null;
        }

        private void OnHpChanged(int current, int max)
        {
            // Auto-unbind on death so the HUD doesn't linger.
            if (current <= 0)
            {
                BindToBoss(null);
                return;
            }
            Refresh();
        }

        private void OnPhaseChanged(int oldPhase, int newPhase)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_fill == null || _label == null) return;
            if (_boundBoss == null || _boundHealth == null)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            float frac = _boundHealth.MaxHp > 0
                ? Mathf.Clamp01((float)_boundHealth.CurrentHp / _boundHealth.MaxHp)
                : 0f;
            _fill.fillAmount = frac;

            string phaseLabel = _boundBoss.CurrentLabel;
            string bossName = _boundBoss.gameObject.name;
            _label.text = string.IsNullOrEmpty(phaseLabel)
                ? bossName
                : $"{bossName} — {phaseLabel}";
        }

        // Build the Canvas + Image + Text hierarchy once. Stays simple —
        // no fancy animation, no frame art. Designers can swap to a
        // prefab-driven implementation later by replacing this method.
        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("BossHealthBarHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            // Container — top of screen, full width minus margins.
            var bg = new GameObject("BG");
            bg.transform.SetParent(_root.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.15f, 0.92f);
            bgRt.anchorMax = new Vector2(0.85f, 0.97f);
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.7f);

            // Fill — clipped left-to-right.
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(bg.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            _fill = fillGo.AddComponent<Image>();
            _fill.color = new Color(0.78f, 0.18f, 0.18f, 0.95f);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 1f;

            // Label — overlaid on top of fill, centred.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(bg.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            _label = labelGo.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = Color.white;
            _label.fontSize = 22;
            _label.fontStyle = FontStyle.Bold;
            _label.text = "";

            _root.SetActive(false);
        }

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
        }

        protected override void OnDestroy()
        {
            UnbindCurrent();
            base.OnDestroy();
        }
    }
}
