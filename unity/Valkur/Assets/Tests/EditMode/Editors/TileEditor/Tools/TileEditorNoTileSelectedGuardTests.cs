using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Tools
{
    /// <summary>
    /// Regression coverage for the newest, highest-risk, completely untested fix in
    /// the Tile Editor: Brush and Fill used to return SILENTLY when
    /// <see cref="TileEditorState.SelectedTile"/> was null — the exact bug the user
    /// reported as "the brush doesn't paint anything", with zero feedback about why.
    /// The fix (<c>TileEditorManager.InputHandlers.cs::HandleBrushInput</c> and
    /// <c>TileEditorManager.BrushHandlers.cs::HandleFillInput</c>) adds an early guard
    /// that emits <see cref="TileEditorConstants.NoTileSelectedHint"/> via
    /// <c>SetStatus</c> — but ONLY on the actual click frame (the guard runs every
    /// frame the tool is selected, so an unconditional message would spam the status
    /// line once per frame while the mouse just sits there with Brush/Fill active).
    ///
    /// This can NOT be exercised behaviourally in EditMode: both guarded methods are
    /// gated end-to-end on <c>MouseInputManager.WasLeftMouseButtonPressedThisFrame()</c>,
    /// which falls through to <c>UnityEngine.Input</c> and is always false outside
    /// Play Mode (documented project-wide — see <c>ColliderTagUndoTests</c> /
    /// <c>LayerJumpsUndoTests</c>). Invoking the private handler by reflection with no
    /// real click event proves nothing either way: with the guard, it returns
    /// immediately; without it, EVERY branch after the guard is ALSO gated on the same
    /// always-false click check, so the method still does nothing — a passing test
    /// either way, i.e. zero discriminating power.
    ///
    /// The only thing that genuinely fails when this fix regresses is the SOURCE
    /// STRUCTURE of the two methods: the null-tile guard must (a) exist, (b) run
    /// BEFORE the first call to <c>TileBrush.Paint</c> / <c>TileBrush.FloodFill</c>
    /// (skipping it would let a null-tile click fall through to <c>TileBrush.Paint</c>
    /// with <c>tile: null</c> — which is byte-for-byte identical to
    /// <c>TileBrush.Erase</c>, i.e. a silent eraser masquerading as a broken brush),
    /// and (c) gate the <c>SetStatus</c> call behind the click check so it doesn't
    /// spam every frame. This mirrors the project's own precedent for "can't be
    /// exercised behaviourally, so pin structurally" cases —
    /// <c>Game/Meta/BraceBalanceRegressionTests</c> reads production source text for
    /// the same reason.
    /// </summary>
    [TestFixture]
    public class TileEditorNoTileSelectedGuardTests
    {
        private const string InputHandlersRelativePath =
            "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.InputHandlers.cs";
        private const string BrushHandlersRelativePath =
            "_Project/Scripts/Gameplay/Editors/Tile/TileEditorManager.BrushHandlers.cs";

        private static string ReadProductionSource(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"Production file not found at expected path: {path}");
            return File.ReadAllText(path);
        }

        // ════════════════════════════════════════════════════════════════════
        // Constant contract — locks the message itself from going silently empty.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void NoTileSelectedHint_IsNonEmptyAndMentionsTilesPanel()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(TileEditorConstants.NoTileSelectedHint),
                "NoTileSelectedHint must not be blank — an empty SetStatus call reads exactly like " +
                "the pre-fix silent failure the user reported.");
            StringAssert.Contains("TILES", TileEditorConstants.NoTileSelectedHint.ToUpperInvariant(),
                "The hint should point the user at the TILES panel (where SelectedTile comes from), " +
                "not just say something failed.");
        }

        // ════════════════════════════════════════════════════════════════════
        // HandleBrushInput — TileEditorManager.InputHandlers.cs
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HandleBrushInput_NullTileGuard_RunsBeforeAnyPaintCall()
        {
            string source = ReadProductionSource(InputHandlersRelativePath);

            int idxMethod = source.IndexOf("private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)");
            int idxGuard  = source.IndexOf("if (_state.SelectedTile == null)");
            int idxHint   = source.IndexOf("TileEditorConstants.NoTileSelectedHint");
            int idxPaint  = source.IndexOf("TileBrush.Paint(tilemap, cellPos, _state.SelectedTile");

            Assert.Greater(idxMethod, -1, "HandleBrushInput declaration not found — did the signature change?");
            Assert.Greater(idxGuard, -1, "HandleBrushInput must guard on `_state.SelectedTile == null`.");
            Assert.Greater(idxHint, -1, "HandleBrushInput must reference TileEditorConstants.NoTileSelectedHint.");
            Assert.Greater(idxPaint, -1, "HandleBrushInput must still call TileBrush.Paint on the happy path.");

            Assert.Less(idxMethod, idxGuard, "The guard must live inside HandleBrushInput, not some other method.");
            Assert.Less(idxGuard, idxHint,
                "The SelectedTile==null check must come BEFORE the NoTileSelectedHint reference — " +
                "otherwise the hint isn't actually inside the guard block.");
            Assert.Less(idxHint, idxPaint,
                "BUG GUARD: the null-tile guard must run and return BEFORE TileBrush.Paint is ever " +
                "called. TileBrush.Paint(tile: null, ...) is identical to TileBrush.Erase — if this " +
                "ordering regresses, a click with no tile selected silently ERASES the map instead of " +
                "doing nothing.");

            string guardToHint = source.Substring(idxGuard, idxHint - idxGuard);
            StringAssert.Contains("WasLeftMouseButtonPressedThisFrame()", guardToHint,
                "The SetStatus(NoTileSelectedHint) call must be nested inside a " +
                "WasLeftMouseButtonPressedThisFrame() check — this guard runs every frame Brush is " +
                "selected, so an ungated SetStatus would spam the status line once per frame while the " +
                "mouse just sits idle with no tile picked.");

            string hintToPaint = source.Substring(idxHint, idxPaint - idxHint);
            StringAssert.Contains("return;", hintToPaint,
                "After emitting the hint the method must return — falling through would still reach " +
                "TileBrush.Paint with a null tile.");
        }

        [Test]
        public void HandleFillInput_NullTileGuard_RunsBeforeFloodFillCall()
        {
            string source = ReadProductionSource(BrushHandlersRelativePath);

            int idxMethod = source.IndexOf("private void HandleFillInput(Tilemap tilemap, Vector3Int cellPos)");
            int idxGuard  = source.IndexOf("if (_state.SelectedTile == null)");
            int idxHint   = source.IndexOf("TileEditorConstants.NoTileSelectedHint");
            int idxFill   = source.IndexOf("TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile");

            Assert.Greater(idxMethod, -1, "HandleFillInput declaration not found — did the signature change?");
            Assert.Greater(idxGuard, -1, "HandleFillInput must guard on `_state.SelectedTile == null`.");
            Assert.Greater(idxHint, -1, "HandleFillInput must reference TileEditorConstants.NoTileSelectedHint.");
            Assert.Greater(idxFill, -1, "HandleFillInput must still call TileBrush.FloodFill on the happy path.");

            Assert.Less(idxMethod, idxGuard, "The guard must live inside HandleFillInput, not some other method.");
            Assert.Less(idxGuard, idxHint,
                "The SelectedTile==null check must come BEFORE the NoTileSelectedHint reference.");
            Assert.Less(idxHint, idxFill,
                "BUG GUARD: the null-tile guard must run and return BEFORE TileBrush.FloodFill is ever " +
                "called — a Fill click with no tile selected must never reach the flood-fill routine.");

            string guardToHint = source.Substring(idxGuard, idxHint - idxGuard);
            StringAssert.Contains("WasLeftMouseButtonPressedThisFrame()", guardToHint,
                "Same contract as HandleBrushInput: SetStatus(NoTileSelectedHint) must be nested inside " +
                "a WasLeftMouseButtonPressedThisFrame() check, not fired unconditionally every frame.");

            string hintToFill = source.Substring(idxHint, idxFill - idxHint);
            StringAssert.Contains("return;", hintToFill,
                "After emitting the hint the method must return before reaching TileBrush.FloodFill.");
        }
    }
}
