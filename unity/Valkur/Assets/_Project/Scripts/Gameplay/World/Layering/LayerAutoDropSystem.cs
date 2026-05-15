using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Runtime tick that auto-drops the player to a lower visual layer when they
    /// walk off an elevated tile. Mirrors <see cref="LayerJumpTriggerSystem"/>'s
    /// architecture (singleton, cell-enter tracker, AfterSceneLoad bootstrap +
    /// <see cref="EnsureExists"/> belt-and-suspenders) so the two systems can
    /// coexist without sharing state.
    ///
    /// <para>
    /// <b>Semantic (gameplay design):</b> jump tiles authorise the player to
    /// move <i>up</i> (M1.8); gravity, modelled here, takes care of moving them
    /// <i>down</i>. When the player enters a new cell and the topmost painted
    /// visual layer at that cell is STRICTLY BELOW their current
    /// <see cref="VisualLayerOccupant.CurrentVisualLayer"/>, the player snaps
    /// directly to that lower layer. Walking onto a tile at the same or higher
    /// layer is a no-op (climbing still requires an explicit jump tile).
    /// </para>
    ///
    /// <para>
    /// <b>Coexistence with <see cref="LayerJumpTriggerSystem"/>:</b> if the cell
    /// has a layer-jump entry, the jump system owns the result for that
    /// cell-enter — auto-drop yields. The author's explicit "set me to layer N"
    /// wins over the implicit gravity rule.
    /// </para>
    ///
    /// <para>
    /// <b>Void cells:</b> a cell with no tile in any visible layer (underfoot
    /// returns -1) is NOT a drop target; it is a "you can't walk here" wall.
    /// Movement-level enforcement of that contract lives in
    /// <c>PlayerController.Movement.ClampInputAgainstVoid</c> — by the time
    /// this system samples the player position, the movement system has
    /// already prevented entry, so a void cell never reaches the cell-enter
    /// detector here in practice. The defensive <c>underfoot &lt; 0</c>
    /// early-out still fires for edge cases (teleport, scene reload, no
    /// WorldGridBuilder yet).
    /// </para>
    /// </summary>
    public sealed class LayerAutoDropSystem : SingletonMonoBehaviour<LayerAutoDropSystem>
    {
        protected override bool Persist => false;

        private VisualLayerOccupant _player;
        private WorldGridBuilder _grid;
        private LayerJumpMap _jumps;        // null until TileEditorManager boots
        private Vector2Int? _lastCell;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterSceneLoad() => EnsureExists();

        /// <summary>
        /// Idempotent spawner. Safe to call from anywhere — does nothing if the
        /// singleton already exists. <see cref="TileEditorManager.OnSingletonAwake"/>
        /// calls this as belt-and-suspenders so the auto-drop survives a
        /// script-reload mid-Play-Mode (AfterSceneLoad does not fire on
        /// recompiles, only on real scene loads).
        /// </summary>
        public static void EnsureExists()
        {
            if (HasInstance) return;
            var go = new GameObject(nameof(LayerAutoDropSystem));
            go.AddComponent<LayerAutoDropSystem>();
        }

        private void Update()
        {
            // Resolve refs lazily — the system survives Player respawn / scene
            // transitions without explicit re-wiring (mirrors LayerJumpTriggerSystem).
            if (_player == null) _player = FindObjectOfType<VisualLayerOccupant>();
            if (_player == null) return;
            if (_grid == null)   _grid = FindObjectOfType<WorldGridBuilder>();
            if (_grid == null) return;

            // Jumps map is optional — if TileEditorManager hasn't booted yet,
            // every cell behaves as "no jump tile here" and auto-drop owns it.
            if (_jumps == null && TileEditorManager.HasInstance)
                _jumps = TileEditorManager.Instance.LayerJumps;

            var pos = _player.transform.position;
            var cell = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
            EvaluateCellTransition(cell);
        }

        /// <summary>
        /// Pure cell-enter evaluation extracted from <see cref="Update"/>.
        /// Returns <c>true</c> when this call resulted in a real
        /// <see cref="VisualLayerOccupant.SetVisualLayer(int)"/> fire,
        /// otherwise <c>false</c> (same cell as last tick, jump tile owns it,
        /// underfoot is void / equal / higher than the current layer).
        /// Refactored out so EditMode tests can drive the state machine
        /// directly without needing a TileEditorManager singleton + scene.
        /// </summary>
        private bool EvaluateCellTransition(Vector2Int currentCell)
        {
            if (_player == null || _grid == null) return false;

            // Cell-enter detection mirrors LayerJumpTriggerSystem: skip when
            // the player hasn't crossed a cell boundary since the last tick.
            if (_lastCell.HasValue && _lastCell.Value == currentCell) return false;
            _lastCell = currentCell;

            // Jump tile wins (decision 5 of M1.9). The jump system fires its
            // own SetVisualLayer this same frame — auto-drop must NOT also fire
            // or we'd race on the same target.
            if (_jumps != null && !string.IsNullOrEmpty(_jumps.Get(currentCell))) return false;

            // Sample underfoot at the player's WORLD position (not just the
            // grid cell), so the probe uses each tilemap's WorldToCell — this
            // matches how VisualLayerProbe is used everywhere else and avoids
            // off-by-one issues at zone boundaries.
            int underfoot = VisualLayerProbe.GetTopmostLayer(_player.transform.position, _grid);

            // Decision 2: a void cell is handled by movement clamp, never reached
            // in practice — but if it does (teleport, save reload), we no-op
            // rather than dropping to -1 / 0.
            if (underfoot < 0) return false;

            // Decision 3: ONLY drop. Equal or higher underfoot does not auto-climb.
            if (underfoot >= _player.CurrentVisualLayer) return false;

            _player.SetVisualLayer(underfoot);
            return true;
        }

        /// <summary>
        /// Reset the cell-enter tracker. Call when the player teleports /
        /// respawns at a new position and we want a fresh "first entry"
        /// semantic next frame.
        /// </summary>
        internal void ResetTracker() => _lastCell = null;

        // ── Test-only hooks (InternalsVisibleTo Valkur.Tests.EditMode) ───────

        /// <summary>
        /// Bind explicit dependencies + reset the cell tracker so EditMode
        /// tests can evaluate the state machine without spinning up the real
        /// player scene + WorldGridBuilder + TileEditorManager. The probe
        /// reads the grid via <see cref="VisualLayerProbe.GetTopmostLayer"/>,
        /// so the test still needs a real <see cref="WorldGridBuilder"/> +
        /// painted tilemap, but it can pass a freshly-built one rather than
        /// the scene's authoritative instance.
        /// </summary>
        internal void TestBind(VisualLayerOccupant player, WorldGridBuilder grid, LayerJumpMap jumps)
        {
            _player = player;
            _grid = grid;
            _jumps = jumps;
            _lastCell = null;
        }

        /// <summary>
        /// Drive a synthetic "the player is at this cell" tick through the
        /// cell-enter detector. Returns whether the call resulted in a real
        /// <c>SetVisualLayer</c> fire.
        /// </summary>
        internal bool TestStepToCell(Vector2Int cell) => EvaluateCellTransition(cell);
    }
}
