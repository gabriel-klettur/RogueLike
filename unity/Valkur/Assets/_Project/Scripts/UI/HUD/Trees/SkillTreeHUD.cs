using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The talents board: the class's authored tree drawn as a BOARD, with its prerequisite
    /// edges, over the player panel's stone and on the player panel's texel grid.
    ///
    /// <para><b>What it replaces, and why the shape mattered more than the paint.</b> The shipped
    /// panel was a vertical list of legacy <c>UnityEngine.UI.Text</c> rows. Measured live at
    /// 1600x800: its canvas scaled at factor 2.000 against every other HUD surface's 1.000, all
    /// SEVEN rows were truncated (mean 44 of 92 characters — 52 % of every sentence thrown away),
    /// seven rows of 64 px overflowed a 476 px list with no <c>ScrollRect</c>, and its
    /// 0.85-alpha plate let through 38 % of the world's sRGB so the panel's own background varied
    /// x3.04 in luminance depending on where the player stood. Full audit:
    /// <c>.github/SKILLS_TAB_BEAUTY_AUDIT_2026-09-12.md</c>.</para>
    ///
    /// <para><b>The board needed no new data.</b> All 35 shipped nodes already carried a
    /// hand-authored <c>row</c> and <c>column</c> — three roots, three middles, one capstone —
    /// and the old panel used them as a sort key for a flat list. <see cref="SkillTreeLayout"/>
    /// turns the same two fields into positions and elbows.</para>
    ///
    /// <para><b>Nothing is truncated because the long text is not on the board.</b> A node shows
    /// an icon, its pips, its name and its cost; the description, the effect at the current rank,
    /// the effect at the NEXT rank and every lock reason live in the card down the right-hand
    /// side, which has a column to itself.</para>
    ///
    /// <para><b>It lives in <c>Valkur.UI</c>.</b> <c>Valkur.UI -> Valkur.Gameplay</c> is allowed
    /// and the reverse is not, so moving it here is what lets it use <c>HudTooltip</c> and the
    /// rest of the kit that stayed in this assembly — the shortcut the inventory could not take.
    /// <c>CharacterSheetController</c>, which builds it, is in this assembly already.</para>
    /// </summary>
    public sealed partial class SkillTreeHUD : SingletonMonoBehaviour<SkillTreeHUD>
    {
        [Tooltip("Player's LearnedSkills component. Auto-resolved via " +
                 "EntityRegistry.PlayerTransform when null.")]
        [SerializeField] private LearnedSkills skills;

        private int _playerLevel = 1;
        private Experience _experience;

        private readonly List<SkillLock> _locks = new List<SkillLock>(4);
        private readonly StringBuilder _sb = new StringBuilder(96);

        /// <summary>True while the board is on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The bound model, for the tests and for the console probe.</summary>
        public LearnedSkills Skills => skills;

        protected override bool Persist => false;

        // ── Binding ───────────────────────────────────────────────────────────

        public void BindLearnedSkills(LearnedSkills ls, int level)
        {
            UnbindCurrent();
            skills = ls;
            _playerLevel = Mathf.Max(1, level);
            if (skills != null) skills.OnLoadoutChanged += OnLoadoutChanged;
            if (IsOpen) Rebuild();
        }

        private void UnbindCurrent()
        {
            if (skills != null) skills.OnLoadoutChanged -= OnLoadoutChanged;
        }

        private void AutoResolveSkills()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            _experience = player.GetComponent<Experience>();
            BindLearnedSkills(player.GetComponent<LearnedSkills>(), _experience != null ? _experience.Level : 1);
        }

        /// <summary>
        /// The character's level, re-read every refresh rather than cached at bind time.
        ///
        /// <para>The old panel took it once in <c>BindLearnedSkills</c> and never again, so a
        /// node gated on level 8 went on saying "requires level 8" after the player reached 8 —
        /// until they closed the window and opened it again. A level is not a property of the
        /// binding.</para>
        /// </summary>
        private int ResolveLevel()
        {
            if (_experience == null)
            {
                var player = EntityRegistry.PlayerTransform;
                if (player != null) _experience = player.GetComponent<Experience>();
            }
            if (_experience != null) _playerLevel = Mathf.Max(1, _experience.Level);
            return _playerLevel;
        }

        private void OnLoadoutChanged()
        {
            if (!IsOpen) return;
            Rebuild();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
            // A static event with Domain Reload off: unsubscribed in OnDestroy, or a destroyed
            // board stays alive for the rest of the session repainting nothing.
            GameLanguage.OnChanged += OnLanguageChanged;
        }

        protected override void OnDestroy()
        {
            GameLanguage.OnChanged -= OnLanguageChanged;
            UnbindCurrent();
            EscapeOwnership.Release(this);
            DisposeViews();
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(_additive);
            base.OnDestroy();
        }

        private void OnLanguageChanged(string _)
        {
            if (!_built) return;
            // Nothing to defer: Open() rebuilds unconditionally, so a panel that is shut
            // when the language changes is already correct the moment it is opened. The flag
            // that used to be set here was written in three places and read in none.
            if (IsOpen) Rebuild();
        }

        // ── Open / close ──────────────────────────────────────────────────────

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            EnsureBuilt();
            if (skills == null) AutoResolveSkills();
            IsOpen = true;
            if (_root != null) _root.SetActive(true);
            EscapeOwnership.Claim(this);
            Rebuild();
            _fadeTarget = 1f;
        }

        public void Close()
        {
            IsOpen = false;
            EscapeOwnership.Release(this);
            _fadeTarget = 0f;
            DisarmRespec();
            if (_motes != null) _motes.Clear();
            // Hidden immediately rather than after the fade: the sheet's tab strip switches
            // panels in one frame, and a board still fading out under the next tab is a ghost.
            if (_root != null) _root.SetActive(false);
            _group.alpha = 0f;
            _fade = 0f;
        }

        private void Update()
        {
            if (!_built) return;
            TickFade();
            if (!IsOpen) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < _views.Count; i++) _views[i].Tick(now);
            TickRespecArm(now);
            TickEdgeLight(now);
            if (_motes != null) _motes.Tick(Time.unscaledDeltaTime);
            Refit();
        }

        /// <summary>
        /// Fades in and out rather than jumping (R7). The board is hidden on the frame it closes,
        /// so this only ever ramps up in practice — which is the half that reads.
        /// </summary>
        private void TickFade()
        {
            if (Mathf.Approximately(_fade, _fadeTarget)) return;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, _style.fadeSeconds);
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, step);
            _group.alpha = _fade;
        }
    }
}
