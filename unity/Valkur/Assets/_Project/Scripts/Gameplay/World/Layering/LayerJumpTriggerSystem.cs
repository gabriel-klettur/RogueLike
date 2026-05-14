using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Runtime tick that watches the player's cell position and fires a layer
    /// transition (<see cref="VisualLayerOccupant.SetVisualLayer(int)"/>) when
    /// they enter a cell with a <see cref="LayerJumpMap"/> entry painted via
    /// the Tile Editor's LAYER JUMPS panel (M1.8).
    ///
    /// Detection mode: <b>cell-enter</b>. We track the player's <c>_lastCell</c>
    /// across frames; the fire only happens once per fresh entry into a jump
    /// cell. Standing on a jump cell does NOT re-fire. Walking away and back
    /// DOES re-fire — there is no per-cell consumed state.
    ///
    /// Bootstraps itself via <see cref="RuntimeInitializeOnLoadMethod"/> so any
    /// gameplay scene picks it up without manual wiring. The system is always
    /// active — including with F8 open — so authors can paint a jump tile and
    /// walk the player onto it immediately for feedback.
    ///
    /// Reads the live <see cref="LayerJumpMap"/> off
    /// <see cref="TileEditorManager.LayerJumps"/>. This couples the runtime to
    /// the editor namespace by design — the Tile Editor is the single owner of
    /// every map (Collision, terrains, collision-tags, layer-jumps), and the
    /// <see cref="OverlayLoader"/> populates them whether or not the user ever
    /// presses F8.
    /// </summary>
    public sealed class LayerJumpTriggerSystem : SingletonMonoBehaviour<LayerJumpTriggerSystem>
    {
        protected override bool Persist => false;

        private VisualLayerOccupant _player;
        private LayerJumpMap _jumps;
        private Vector2Int? _lastCell;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterSceneLoad() => EnsureExists();

        /// <summary>
        /// Idempotent spawner. Safe to call from anywhere — does nothing if the
        /// singleton already exists.
        ///
        /// Why a public hook beyond <see cref="BootstrapAfterSceneLoad"/>:
        /// <c>RuntimeInitializeOnLoadMethod(AfterSceneLoad)</c> only fires on
        /// scene loads, NOT on script reloads. If the user is mid-Play-Mode
        /// when M1.8 shipped this class for the first time, the bootstrap path
        /// would never run for their session. <see cref="TileEditorManager"/>
        /// calls this from its <c>OnSingletonAwake</c> as belt-and-suspenders
        /// so the trigger system is always present alongside the editor.
        /// </summary>
        public static void EnsureExists()
        {
            if (HasInstance) return;
            var go = new GameObject(nameof(LayerJumpTriggerSystem));
            go.AddComponent<LayerJumpTriggerSystem>();
        }

        private void Update()
        {
            // Resolve refs lazily — the system survives Player respawn / scene
            // transitions without explicit re-wiring.
            if (_player == null)
                _player = FindObjectOfType<VisualLayerOccupant>();
            if (_player == null) return;

            if (_jumps == null)
            {
                if (!TileEditorManager.HasInstance) return;
                _jumps = TileEditorManager.Instance.LayerJumps;
            }
            if (_jumps == null || _jumps.Count == 0) return;

            var pos = _player.transform.position;
            var cell = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
            EvaluateCellTransition(cell);
        }

        /// <summary>
        /// Pure cell-enter evaluation extracted from <see cref="Update"/>. Returns
        /// <c>true</c> when this call resulted in a real <c>SetVisualLayer</c> fire,
        /// otherwise <c>false</c> (idle cell, no entry in the map, target equals
        /// current, or the player has been standing on the same cell across ticks).
        /// Refactored out so EditMode tests can drive the state machine directly
        /// without needing a TileEditorManager singleton.
        /// </summary>
        private bool EvaluateCellTransition(Vector2Int currentCell)
        {
            if (_player == null || _jumps == null) return false;

            // Cell-enter detection: skip when the player hasn't crossed a cell
            // boundary since the last tick. Comparing nullable<Vector2Int> first
            // also covers the initial frame after the system spawns (lastCell
            // null → record current and bail; first real transition fires on
            // the next cell-cross, which matches "the player entered this cell").
            if (_lastCell.HasValue && _lastCell.Value == currentCell) return false;
            _lastCell = currentCell;

            string targetStr = _jumps.Get(currentCell);
            if (string.IsNullOrEmpty(targetStr)) return false;
            if (!int.TryParse(targetStr, out int target)) return false;
            if (target == _player.CurrentVisualLayer) return false;

            _player.SetVisualLayer(target);
            return true;
        }

        /// <summary>
        /// Reset the cell-enter tracker — call when the player teleports / respawns
        /// at a new position and we want a fresh "first entry" semantic next frame.
        /// Internal use only; gameplay code shouldn't need this today.
        /// </summary>
        internal void ResetTracker()
        {
            _lastCell = null;
        }

        // ── Test-only hooks (InternalsVisibleTo Valkur.Tests.EditMode) ───────

        /// <summary>
        /// Bind explicit dependencies + reset the cell tracker so EditMode tests
        /// can evaluate the state machine without spinning up the real player
        /// scene + TileEditorManager singleton. NEVER call from production code.
        /// </summary>
        internal void TestBind(VisualLayerOccupant player, LayerJumpMap jumps)
        {
            _player = player;
            _jumps = jumps;
            _lastCell = null;
        }

        /// <summary>
        /// Drive a synthetic "the player is at this cell" tick through the cell-
        /// enter detector. Returns whether the call resulted in a real
        /// <c>SetVisualLayer</c> fire.
        /// </summary>
        internal bool TestStepToCell(Vector2Int cell) => EvaluateCellTransition(cell);
    }
}
