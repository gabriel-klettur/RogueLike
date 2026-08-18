using System;
using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime-mutable visual theme for all Tile Editor floating panels.
    /// All chrome elements (panel bg, panel outline, header bg, header
    /// separator, menu-bar elements) read their color/alpha from this
    /// central theme object. The UX panel edits these fields and calls
    /// <see cref="ApplyToAll"/> to push the change to every registered
    /// <see cref="PanelChrome"/> instance live.
    ///
    /// On first access, installs itself as the color source for the kit's
    /// <see cref="PanelChrome"/> and <see cref="MenuBarChrome"/> so the
    /// existing live-tweak workflow keeps working after the chrome moved
    /// to <c>Valkur.UIKit</c>.
    /// </summary>
    public static class TileEditorTheme
    {
        private static readonly Color  _defPanelBg    = new Color(0.08f, 0.08f, 0.10f, 0.82f);
        private static readonly Color  _defHeaderBg   = new Color(0.06f, 0.06f, 0.08f, 0.92f);
        private static readonly Color  _defBorder     = new Color(0.20f, 0.22f, 0.28f, 0.65f);
        private static readonly Color  _defSeparator  = new Color(0.30f, 0.32f, 0.38f, 0.55f);
        private static readonly Color  _defAccent     = new Color(0.93f, 0.93f, 0.96f, 1f);
        private static readonly Color  _defText       = new Color(0.60f, 0.62f, 0.68f, 1f);
        private static readonly Color  _defMenuBarBg  = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private const            float _defOutlinePx  = 1f;
        private const            float _defHdrAlpha   = 0.92f;

        public static Color  PanelBg     = _defPanelBg;
        public static Color  HeaderBg    = _defHeaderBg;
        public static Color  Border      = _defBorder;
        public static Color  Separator   = _defSeparator;
        public static Color  HeaderTitle = _defAccent;
        public static Color  SectionText = _defText;
        public static Color  MenuBarBg   = _defMenuBarBg;
        public static float  OutlinePx   = _defOutlinePx;

        /// <summary>Raised after any field is mutated, so dependent UI sliders can refresh.</summary>
        /// <summary>
        /// Editor UI from the previous Play session would otherwise still be listening.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEventOnPlayModeEnter()
        {
            OnChanged = null;
        }

        public static event Action OnChanged;

        // Static ctor wires the kit chrome to read THIS theme. Runs the
        // first time any field of TileEditorTheme is touched.
        static TileEditorTheme()
        {
            PanelChrome.ColorSource   = new TileEditorPanelChromeColors();
            MenuBarChrome.ColorSource = new TileEditorMenuBarChromeColors();
        }

        /// <summary>
        /// Push the current theme values to every registered <see cref="PanelChrome"/>
        /// (and the menu bar via <see cref="MenuBarChrome"/>) and notify listeners.
        /// Call this AFTER mutating one or more fields above.
        /// </summary>
        public static void ApplyToAll()
        {
            PanelChrome.ApplyThemeToAll();
            MenuBarChrome.ApplyThemeToAll();
            OnChanged?.Invoke();
        }

        /// <summary>Restore every field to the value captured at build time.</summary>
        public static void ResetToDefaults()
        {
            PanelBg     = _defPanelBg;
            HeaderBg    = _defHeaderBg;
            Border      = _defBorder;
            Separator   = _defSeparator;
            HeaderTitle = _defAccent;
            SectionText = _defText;
            MenuBarBg   = _defMenuBarBg;
            OutlinePx   = _defOutlinePx;
            ApplyToAll();
        }

        // Adapters that expose this theme to the kit's chrome classes.
        private sealed class TileEditorPanelChromeColors : IPanelChromeColors
        {
            public Color PanelBg          => TileEditorTheme.PanelBg;
            public Color HeaderBg         => TileEditorTheme.HeaderBg;
            public Color Separator        => TileEditorTheme.Separator;
            public Color HeaderTitleColor => TileEditorTheme.HeaderTitle;
            public Color Border           => TileEditorTheme.Border;
            public float OutlinePx        => TileEditorTheme.OutlinePx;
        }

        private sealed class TileEditorMenuBarChromeColors : IMenuBarChromeColors
        {
            public Color MenuBarBg => TileEditorTheme.MenuBarBg;
            public Color Border    => TileEditorTheme.Border;
            public float OutlinePx => TileEditorTheme.OutlinePx;
        }
    }
}
