using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet: one window, one tab per panel. Double-clicking the
    /// ability row in the bottom-left HUD opens it, and the tab strip switches
    /// between the skill tree and the statistics panel — both of which already
    /// exist as standalone HUDs, so this only decides which one is on screen and
    /// keeps them from stacking.
    ///
    /// Tabs are a list, not a pair: adding a third panel (equipment, bestiary)
    /// is one entry in <see cref="BuildTabs"/> and the strip lays itself out
    /// around it.
    /// </summary>
    public sealed partial class CharacterSheetController : SingletonMonoBehaviour<CharacterSheetController>
    {
        /// <summary>One entry in the tab strip and the panel it drives.</summary>
        private sealed class SheetTab
        {
            public string Label;
            public Action Show;
            public Action Hide;
        }

        private readonly List<SheetTab> _tabs = new List<SheetTab>();
        private int _activeTab = -1;

        protected override bool Persist => false;

        /// <summary>True while the sheet is on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Index of the visible tab, or -1 while closed.</summary>
        public int ActiveTab => _activeTab;

        /// <summary>Number of tabs currently configured.</summary>
        public int TabCount => _tabs.Count;

        // ── Lifecycle ─────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            BuildTabs();
            EnsureBuilt();
            ApplyOpenState(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (KeyboardInputManager.WasEscapePressedThisFrame()) Close();
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Open the sheet on <paramref name="tabIndex"/> (clamped).</summary>
        public void Open(int tabIndex = 0)
        {
            if (_tabs.Count == 0) return;

            EnsureBuilt();
            ApplyOpenState(true);
            SelectTab(Mathf.Clamp(tabIndex, 0, _tabs.Count - 1));
        }

        /// <summary>Close the sheet and every panel it owns.</summary>
        public void Close()
        {
            if (!IsOpen && _activeTab < 0) return;

            HideAllTabs();
            _activeTab = -1;
            ApplyOpenState(false);
        }

        /// <summary>Open on the first tab, or close if already open.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open(_lastTabOpened);
        }

        /// <summary>Show one tab and hide the others. No-op while closed.</summary>
        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            if (!IsOpen) return;

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (i == index) _tabs[i].Show?.Invoke();
                else _tabs[i].Hide?.Invoke();
            }

            _activeTab = index;
            _lastTabOpened = index;
            RefreshTabVisuals(index);
        }

        // ── Tab wiring ────────────────────────────────────────────────────

        private int _lastTabOpened;

        private void BuildTabs()
        {
            _tabs.Clear();

            _tabs.Add(new SheetTab
            {
                Label = "SKILLS",
                Show  = () => EnsurePanel<SkillTreeHUD>("SkillTreeHUD").Open(),
                Hide  = () => EnsurePanel<SkillTreeHUD>("SkillTreeHUD").Close(),
            });

            _tabs.Add(new SheetTab
            {
                Label = "STATS",
                Show  = () => EnsurePanel<StatisticsHUD>("StatisticsHUD").Open(),
                Hide  = () => EnsurePanel<StatisticsHUD>("StatisticsHUD").Close(),
            });
        }

        private void HideAllTabs()
        {
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].Hide?.Invoke();
        }

        /// <summary>
        /// Finds the panel singleton, creating it under <c>[UI]</c> when nothing
        /// else has. Parenting matters: it is what makes the panel hide along
        /// with the rest of the HUD when a runtime editor opens.
        /// </summary>
        private static T EnsurePanel<T>(string goName) where T : MonoBehaviour
        {
            var existing = FindObjectOfType<T>(true);
            if (existing != null) return existing;

            var go = new GameObject(goName);
            var uiContainer = GameObject.Find("[UI]");
            if (uiContainer != null) go.transform.SetParent(uiContainer.transform, false);
            return go.AddComponent<T>();
        }
    }
}
