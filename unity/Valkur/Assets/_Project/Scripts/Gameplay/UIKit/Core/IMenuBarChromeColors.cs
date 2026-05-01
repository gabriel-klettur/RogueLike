using UnityEngine;

namespace Valkur.UIKit
{
    /// <summary>
    /// Color source consumed by <see cref="MenuBarChrome"/>. Pulled out
    /// of the chrome class so the tile editor's live-tweak theme and
    /// other editors / HUDs can each plug a different palette.
    /// </summary>
    public interface IMenuBarChromeColors
    {
        Color MenuBarBg { get; }
        Color Border    { get; }
        float OutlinePx { get; }
    }

    /// <summary>Default menu-bar colors pulled from <see cref="UITheme"/>.</summary>
    public sealed class DefaultMenuBarChromeColors : IMenuBarChromeColors
    {
        public static readonly DefaultMenuBarChromeColors Instance = new DefaultMenuBarChromeColors();
        public Color MenuBarBg => UITheme.BG_HEADER;
        public Color Border    => UITheme.BORDER;
        public float OutlinePx => 1f;
    }
}
