using UnityEngine;
using Valkur.Core;
using Valkur.Core.Boot;
using Valkur.Core.Input;
using Valkur.Core.Services;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The debug HUD: a measuring instrument drawn over the game, in the tool dialect of
    /// <c>.github/HUD_VISUAL_LANGUAGE.md</c> section 6. Audit and design:
    /// <c>.github/DEBUG_HUD_BEAUTY_AUDIT_2026-09-11.md</c>.
    ///
    /// <para><b>Four levels, one key (F1, bound in the asset as <c>Editors/ToggleDebugHUD</c>).</b>
    /// 0 hidden, 1 a one-row chip (FPS, ms, a small frame graph), 2 the panel, 3 the panel plus
    /// the world annotations (<c>ai on</c> and the combat ranges). A release player stops at 1:
    /// the chip is a harmless readout, the rest is a developer's tool
    /// (<see cref="RuntimeEditorPolicy"/>). The level is persisted per machine.</para>
    ///
    /// <para><b>What it replaced, measured.</b> A TMP string whose background measured 0 px
    /// tall (a ContentSizeFitter reading an Image with no sprite), so it was legible only when
    /// the world behind it happened to be dark; it covered 65 % of the minimap at a higher
    /// sorting order; it listed six neutral vendors as monsters and three empty spell slots as
    /// ready. This one reads every value from the seam the system itself uses (H8), draws on the
    /// player panel's texel grid with its pixel face (H2), is opaque (H3), sits in a declared
    /// band (H6) and can be clicked through everywhere except its headers (H5).</para>
    ///
    /// <para>Split by aspect: this file is lifecycle, level and input; <c>.Build</c> constructs
    /// and lays out; <c>.Readouts</c> fills the rows; <c>.Events</c> owns motes and the report.</para>
    /// </summary>
    public partial class DebugHUD : MonoBehaviour, IDebugOverlayService
    {
        public const int LevelHidden = 0;
        public const int LevelChip = 1;
        public const int LevelPanel = 2;
        public const int LevelInspector = 3;

        public const string PrefLevel = "valkur.debughud.level";
        public const string PrefLastLevel = "valkur.debughud.lastlevel";
        public const string PrefCollapsed = "valkur.debughud.collapsed";

        /// <summary>Active scene instance (assigned in <see cref="Start"/>).</summary>
        public static DebugHUD Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
        }

        private DebugHudStyle _style;
        private int _level;
        private int _lastVisibleLevel = LevelPanel;
        private bool _persist = true;
        private bool _built;
        private bool _inspectorOwnedAi;
        private bool _inspectorOwnedRanges;
        private float _selfCostMs;

        /// <summary>True at any level above 0.</summary>
        public bool IsVisible => _level > LevelHidden;

        public int Level => _level;

        public int MaxLevel => RuntimeEditorPolicy.AuthoringEditorsAvailable ? LevelInspector : LevelChip;

        /// <summary>Smoothed milliseconds this component's own script work costs per frame.</summary>
        public float SelfCostMs => _selfCostMs;

        public DebugHudStyle Style => _style;

        // -- Lifecycle -------------------------------------------------------------

        private void Start()
        {
            Instance = this;
            ServiceLocator.Register<IDebugOverlayService>(this);
            EnsureBuilt(persist: true);
            int saved = _persist ? PlayerPrefs.GetInt(PrefLevel, LevelHidden) : LevelHidden;
            _lastVisibleLevel = Mathf.Clamp(_persist ? PlayerPrefs.GetInt(PrefLastLevel, LevelPanel) : LevelPanel,
                                            LevelChip, LevelInspector);
            ApplyLevel(saved, save: false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                ServiceLocator.Unregister<IDebugOverlayService>();
                Instance = null;
            }
            ReleaseInspector();
            Teardown();
        }

        /// <summary>
        /// Builds the overlay once. Public so an EditMode test can build it without Start;
        /// <paramref name="persist"/> false keeps the test off this machine's PlayerPrefs,
        /// which survive the run, the Editor and the reboot.
        /// </summary>
        public void EnsureBuilt(bool persist = true)
        {
            _persist = persist;
            if (_built) return;
            _built = true;
            _style = DebugHudStyle.Active;
            Build();
            if (_persist) LoadCollapsed(PlayerPrefs.GetInt(PrefCollapsed, 0));
        }

        private void Update()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleDebugHUD))
                CycleLevel();

            if (_built) Tick(Time.unscaledDeltaTime);

            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            _selfCostMs = Mathf.Lerp(_selfCostMs, (float)ms, 0.05f);
        }

        // -- Levels ----------------------------------------------------------------

        /// <summary>F1: 0 → 1 → 2 → 3 → 0, stopping at <see cref="MaxLevel"/>.</summary>
        public void CycleLevel()
        {
            int next = _level + 1;
            if (next > MaxLevel) next = LevelHidden;
            SetLevel(next);
        }

        public void ToggleVisible() =>
            SetLevel(IsVisible ? LevelHidden : Mathf.Min(_lastVisibleLevel, MaxLevel));

        public void SetLevel(int level) => ApplyLevel(level, save: true);

        private void ApplyLevel(int level, bool save)
        {
            EnsureBuilt(_persist);
            level = Mathf.Clamp(level, LevelHidden, MaxLevel);
            int previous = _level;
            _level = level;
            if (level > LevelHidden) _lastVisibleLevel = level;

            if (level == LevelInspector) ClaimInspector();
            else if (previous == LevelInspector) ReleaseInspector();

            OnLevelChanged(previous, level);

            if (!save || !_persist) return;
            PlayerPrefs.SetInt(PrefLevel, level);
            PlayerPrefs.SetInt(PrefLastLevel, _lastVisibleLevel);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Level 3 turns on the two world annotations — but only the ones that were OFF, and it
        /// hands back exactly those on the way out. Switching off an overlay the author had
        /// turned on from the console would be the tool changing a setting it does not own.
        /// </summary>
        private void ClaimInspector()
        {
            if (!Application.isPlaying) return;
            if (!Valkur.Gameplay.Enemies.AIDebugOverlay.IsOn)
            {
                Valkur.Gameplay.Enemies.AIDebugOverlay.SetEnabled(true);
                _inspectorOwnedAi = true;
            }
            var ranges = Valkur.Gameplay.Combat.CombatRangeVisualizer.Instance;
            if (ranges != null && !ranges.IsVisible)
            {
                ranges.ToggleVisible();
                _inspectorOwnedRanges = true;
            }
        }

        private void ReleaseInspector()
        {
            if (_inspectorOwnedAi)
            {
                _inspectorOwnedAi = false;
                if (Application.isPlaying) Valkur.Gameplay.Enemies.AIDebugOverlay.SetEnabled(false);
            }
            if (_inspectorOwnedRanges)
            {
                _inspectorOwnedRanges = false;
                var ranges = Valkur.Gameplay.Combat.CombatRangeVisualizer.Instance;
                if (ranges != null && ranges.IsVisible) ranges.ToggleVisible();
            }
        }

        // -- Persistence of the section folds ---------------------------------------

        private void SaveCollapsed()
        {
            if (!_persist) return;
            PlayerPrefs.SetInt(PrefCollapsed, CollapsedMask());
            PlayerPrefs.Save();
        }

        /// <summary>The live binding's key cap, so the footer never names a key that moved.</summary>
        private static string ToggleKeyLabel()
        {
            var svc = InputService.Instance;
            var action = svc != null ? svc.Editors.ToggleDebugHUD : null;
            string label = action != null ? InputBindingResolver.PrimaryLabel(action) : null;
            return string.IsNullOrEmpty(label) ? "F1" : label;
        }
    }
}
