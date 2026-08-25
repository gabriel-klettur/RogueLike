using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        /// <summary>
        /// Builds the Tile Editor's "?" hotkey overlay — docked top-right of the
        /// canvas by <see cref="TutorialOverlay"/>, hidden by default, toggled
        /// from the menu-bar "?" button (see <see cref="BuildMenuBar"/>).
        ///
        /// The Tile Editor binds more shortcuts than any other runtime editor
        /// (tool keys, three redo aliases, clipboard, perf probe, zone framing),
        /// so the flat (key, action) list every other editor's tutorial uses
        /// would read as an unbroken wall of twenty lines. <see cref="TutorialOverlay"/>
        /// has no header concept of its own, so section breaks are plain rows
        /// whose "key" slot carries a short caps label (TOOLS / SELECT / HISTORY /
        /// CAMERA) and an empty action — the same trick <c>TimeWeatherEditor</c>'s
        /// tutorial already uses for non-key labels ("Slider", "DEFAULT", …), just
        /// applied as a divider instead of a described control.
        ///
        /// Every line below was read off the live bindings, not documentation
        /// (this repo's docs still said F6 for the toggle that is actually F8):
        ///   • TileEditorInputHandler.cs           — PollToolShortcut (B/E/F/I/S/A),
        ///     PollZoom (mouse wheel), PollUndoRedo (Ctrl+Z, Ctrl+Shift+Z, and the
        ///     Ctrl+Y alias shared with every other runtime editor's redo binding).
        ///   • TileEditorManager.InputHandlers.cs  — HandleMouseInput's per-tool LMB
        ///     dispatch, and the Select-tool-only Ctrl+C/X/V + Esc clipboard block
        ///     inside HandleUndoRedo.
        ///   • TileEditorManager.cs                — the F8 toggle (Update()), the
        ///     Shift+F8 perf-probe toggle, and HandleDoubleClickFrame (zone framing).
        ///     Middle-mouse pan is EditorCameraPanController, shared by every
        ///     runtime editor.
        /// There is no keyboard binding for switching layers, brush size, or the
        /// Colliders/Layer-Jumps edit modes — those are menu-bar buttons and
        /// dropdown toggles only (TileEditorUIBuilder.MenuBar.cs / .CollidersPanel.cs
        /// / .LayerJumpsPanel.cs) — so this overlay does not invent a hotkey for them.
        /// </summary>
        private static GameObject BuildTileEditorTutorial(Transform canvasT)
        {
            var go = TutorialOverlay.Build(canvasT, "TILE EDITOR HOTKEYS", new[]
            {
                // ── Tools ──
                ("TOOLS",     ""),
                ("F8",        "Toggle Tile Editor"),
                ("B",         "Brush tool"),
                ("E",         "Eraser tool"),
                ("F",         "Fill tool"),
                ("I",         "Eyedropper tool"),
                ("S",         "Select tool"),
                ("A",         "Auto-Tile Region tool"),
                ("LMB",       "Paint / erase / fill / pick / select (per tool)"),
                ("Wheel",     "Zoom camera"),

                // ── Selection & clipboard (Select tool only) ──
                ("SELECT",    ""),
                ("Ctrl+C",    "Copy selection"),
                ("Ctrl+X",    "Cut selection"),
                ("Ctrl+V",    "Paste at cursor"),
                ("Esc",       "Clear selection"),

                // ── History ──
                ("HISTORY",       ""),
                ("Ctrl+Z",        "Undo"),
                ("Ctrl+Shift+Z",  "Redo"),
                ("Ctrl+Y",        "Redo (alias)"),

                // ── Camera ──
                ("CAMERA",        ""),
                ("MMB drag",      "Pan the camera"),
                ("Double-click",  "Center + frame the clicked zone"),
                ("Shift+F8",      "Toggle the perf probe overlay"),
            });
            go.SetActive(false);
            return go;
        }
    }
}
