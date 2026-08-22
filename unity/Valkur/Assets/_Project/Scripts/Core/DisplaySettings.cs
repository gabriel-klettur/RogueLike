using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// How the game window is presented. Persisted in <see cref="GameSettings"/>
    /// as an int, so the numeric values are part of the save format — append
    /// new modes, never renumber.
    /// </summary>
    public enum WindowMode
    {
        Windowed             = 0,
        BorderlessFullscreen = 1,
        ExclusiveFullscreen  = 2,
    }

    /// <summary>
    /// One selectable entry in the Video options list. Width/Height of 0 means
    /// "Native" — keep the desktop resolution and let
    /// <see cref="AspectRatioEnforcer"/> letterbox down to the target aspect.
    /// </summary>
    public readonly struct DisplayPreset
    {
        public readonly int Width;
        public readonly int Height;

        public DisplayPreset(int width, int height) { Width = width; Height = height; }

        public bool IsNative => Width <= 0 || Height <= 0;

        public string Label => IsNative ? "Native (letterboxed)" : Width + " x " + Height;
    }

    /// <summary>
    /// Canonical list of window resolutions Valkur offers, plus the code that
    /// applies the persisted choice to the actual window.
    ///
    /// WHY A CURATED LIST INSTEAD OF <c>Screen.resolutions</c>: the camera
    /// renders a fixed <see cref="TargetAspect"/> viewport. Any window that
    /// isn't already that aspect gets letterboxed by
    /// <see cref="AspectRatioEnforcer"/>, which quantises the viewport down to
    /// the largest exact-ratio integer-pixel box that fits — so part of the
    /// window is spent on bars. Every preset here is exactly
    /// <c>TargetAspect</c>, so the enforcer is a no-op and the whole window is
    /// game.
    ///
    /// This is also the seam story. A tilemap "seam line" appears when one art
    /// texel doesn't cover a whole number of screen pixels: the tile quad edge
    /// lands mid-pixel and the (black) camera background shows through.
    /// <c>CameraSetup.SnapOrthoSize</c> guarantees whole-pixel texels on the
    /// VERTICAL axis by solving ortho size from <c>pixelHeight</c>. The
    /// horizontal axis inherits that guarantee only when
    /// <c>pixelWidth / pixelHeight</c> is EXACTLY the target aspect — measured
    /// at 2.002933 on a 1366x768 window before the enforcer was made
    /// ratio-exact, which is the drift that produced vertical lines. Picking a
    /// preset takes the letterbox out of the equation entirely.
    /// </summary>
    public static class DisplaySettings
    {
        /// <summary>Aspect the camera viewport is locked to (matches <see cref="AspectRatioEnforcer"/> defaults).</summary>
        public const float TargetAspect = 2f;

        /// <summary>
        /// Selectable resolutions. Index 0 is Native. All others are exactly
        /// 2:1 and were chosen to sit inside the common desktop sizes:
        /// 1280x640 (720p windows), 1600x800, 1920x960 (1080p / 1200p),
        /// 2560x1280 (1440p), 3200x1600, 3840x1920 (4K).
        /// </summary>
        [SelfHealingStatic("Immutable lookup table of constant sizes. Holds no Unity objects and is never mutated after init, so it cannot go stale across a Play session.")]
        public static readonly DisplayPreset[] Presets =
        {
            new DisplayPreset(0,    0),
            new DisplayPreset(1280, 640),
            new DisplayPreset(1600, 800),
            new DisplayPreset(1920, 960),
            new DisplayPreset(2560, 1280),
            new DisplayPreset(3200, 1600),
            new DisplayPreset(3840, 1920),
        };

        [SelfHealingStatic("Immutable lookup table of constant sizes. Holds no Unity objects and is never mutated after init, so it cannot go stale across a Play session.")]
        public static readonly string[] WindowModeLabels =
        {
            "Windowed",
            "Borderless fullscreen",
            "Exclusive fullscreen",
        };

        /// <summary>
        /// True when a window of this size needs no letterbox at all — the
        /// viewport is the whole window and <c>pixelWidth / pixelHeight</c> is
        /// bit-exactly <see cref="TargetAspect"/>. The EditMode suite asserts
        /// every shipped preset satisfies this.
        /// </summary>
        public static bool IsSeamSafe(int width, int height)
            => width > 0 && height > 0 && width == Mathf.RoundToInt(height * TargetAspect);

        /// <summary>Index of the preset matching the persisted size, or 0 (Native) when unknown.</summary>
        public static int IndexOf(int width, int height)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Presets[i].Width == width && Presets[i].Height == height) return i;
            return 0;
        }

        /// <summary>Clamp an arbitrary int to a valid <see cref="Presets"/> index.</summary>
        public static int ClampIndex(int index) => Mathf.Clamp(index, 0, Presets.Length - 1);

        /// <summary>Preset currently selected by the given settings object.</summary>
        public static DisplayPreset PresetFor(GameSettings settings)
            => settings == null
                ? Presets[0]
                : Presets[ClampIndex(IndexOf(settings.resolutionWidth, settings.resolutionHeight))];

        public static string WindowModeLabel(WindowMode mode)
        {
            int i = (int)mode;
            return i >= 0 && i < WindowModeLabels.Length ? WindowModeLabels[i] : WindowModeLabels[0];
        }

        /// <summary>
        /// Push the persisted choice onto the real window.
        ///
        /// No-op in the Editor: <c>Screen.SetResolution</c> cannot resize the
        /// Game View, but it does move <c>Screen.width</c> for a frame, which
        /// makes the enforcer and the ortho snap thrash for nothing. In the
        /// Editor, set the Game View size by hand (Valkur > Display menu).
        /// </summary>
        public static void Apply(GameSettings settings)
        {
            if (settings == null) return;

            var preset = PresetFor(settings);
            var mode   = ToFullScreenMode(settings.windowMode);

            int width, height;
            if (preset.IsNative)
            {
                // Native: take the desktop mode. On a 16:9 / 16:10 desktop the
                // enforcer letterboxes to an exact 2:1 box — lossless, it just
                // spends some rows on black bars.
                var desktop = Screen.currentResolution;
                width  = desktop.width;
                height = desktop.height;
            }
            else
            {
                width  = preset.Width;
                height = preset.Height;
            }

            if (width <= 0 || height <= 0) return;

            if (Application.isEditor)
            {
                int w = width, h = height;
                VerboseLog.Log(VerboseLog.Category.Settings,
                    () => "[DisplaySettings] Editor: skipping SetResolution(" + w + "x" + h + ", " + mode +
                          "). Set the Game View size by hand.");
                return;
            }

            Screen.SetResolution(width, height, mode);
        }

        private static FullScreenMode ToFullScreenMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.BorderlessFullscreen: return FullScreenMode.FullScreenWindow;
                case WindowMode.ExclusiveFullscreen:  return FullScreenMode.ExclusiveFullScreen;
                default:                              return FullScreenMode.Windowed;
            }
        }

        /// <summary>
        /// Applies the saved display choice before the first frame is ever
        /// presented, so the player never sees a frame at the wrong size.
        /// Reading <see cref="GameSettings.Instance"/> here also warms the
        /// settings cache for everything that runs later in boot.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void ApplyOnBoot()
        {
            if (Application.isEditor) return;
            Apply(GameSettings.Instance);
        }
    }
}
