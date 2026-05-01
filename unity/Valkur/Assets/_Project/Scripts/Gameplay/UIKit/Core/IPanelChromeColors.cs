using UnityEngine;

namespace Valkur.UIKit
{
    /// <summary>
    /// Color source consumed by <see cref="PanelChrome"/> when it repaints
    /// its wired Image/Outline/TMP refs. Each editor or HUD that wants live
    /// theme tweaking provides its own implementation; the kit ships a
    /// default that pulls from <see cref="UITheme"/>.
    /// </summary>
    public interface IPanelChromeColors
    {
        Color PanelBg          { get; }
        Color HeaderBg         { get; }
        Color Separator        { get; }
        Color HeaderTitleColor { get; }
        Color Border           { get; }
        float OutlinePx        { get; }
    }

    /// <summary>
    /// Default PanelChrome colors pulled from <see cref="UITheme"/> tokens.
    /// Used when no custom source has been installed (e.g. MusicPlayerHUD,
    /// editors that don't expose live-tweak UI).
    /// </summary>
    public sealed class DefaultPanelChromeColors : IPanelChromeColors
    {
        public static readonly DefaultPanelChromeColors Instance = new DefaultPanelChromeColors();
        public Color PanelBg          => UITheme.BG_PANEL;
        public Color HeaderBg         => UITheme.BG_HEADER;
        public Color Separator        => UITheme.SEPARATOR;
        public Color HeaderTitleColor => UITheme.ACCENT;
        public Color Border           => UITheme.BORDER;
        public float OutlinePx        => 1f;
    }
}
