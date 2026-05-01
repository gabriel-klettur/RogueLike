using System;
using UnityEngine;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor
    {
        private const int MIN_GRID_DIM = 1;
        private const int MAX_GRID_DIM = 32;

        private void RefreshGridResolutionLabels()
        {
            if (_gridColsVal == null && _gridRowsVal == null) return;

            ColliderGridData grid = null;
            if (_activeBuilding != null)
            {
                var session = EnsureActiveColliderSession();
                if (session != null) grid = session.WorkingGrid;
            }

            if (grid == null)
            {
                if (_gridColsVal != null) _gridColsVal.text = "-";
                if (_gridRowsVal != null) _gridRowsVal.text = "-";
                return;
            }

            if (_gridColsVal != null) _gridColsVal.text = grid.width.ToString();
            if (_gridRowsVal != null) _gridRowsVal.text = grid.height.ToString();
        }

        // Manually changes the LOGICAL N×M resolution of the active collider grid.
        // - For CG: rewrites the shared image grid → propagates to every instance
        //   sharing the same image (each renders cells proportional to its own world rect).
        // - For CU: rewrites the per-instance grid only.
        // Resampling is the only place a logical grid is rebuilt; all other selection /
        // apply paths leave the topology untouched.
        private void AdjustGridResolution(int dCols, int dRows)
        {
            if (_activeBuilding == null) { Toast("Select a building first."); return; }
            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null)
            {
                Toast("No active collider grid.");
                return;
            }

            int oldCols = session.WorkingGrid.width;
            int oldRows = session.WorkingGrid.height;
            int newCols = Mathf.Clamp(oldCols + dCols, MIN_GRID_DIM, MAX_GRID_DIM);
            int newRows = Mathf.Clamp(oldRows + dRows, MIN_GRID_DIM, MAX_GRID_DIM);
            if (newCols == oldCols && newRows == oldRows) return;

            ColliderAuthoringScope scope = session.Scope;
            string imageKey = session.ImageKey;
            int instanceId  = session.InstanceId;

            var before = CloneGrid(GetStoredGrid(scope, imageKey, instanceId));
            var resized = ResampleGridToResolution(session.WorkingGrid, newCols, newRows);

            string label = $"Grid {newCols}x{newRows}";
            _undo.Do(label,
                () => ApplyGridSnapshot(scope, imageKey, instanceId, resized),
                () => ApplyGridSnapshot(scope, imageKey, instanceId, before));
        }

        // Logical resampler: maps an N×M grid onto a new N'×M' grid by sampling
        // proportionally. Decoupled from pixel size so it can be invoked from
        // explicit user actions (UI steppers) without coupling to per-instance
        // size. A cell in the destination is solid if ANY overlapped source cell
        // is solid (conservative — preserves player-perceived collision shape).
        private static ColliderGridData ResampleGridToResolution(ColliderGridData source, int newCols, int newRows)
        {
            if (source == null) return null;
            newCols = Mathf.Max(1, newCols);
            newRows = Mathf.Max(1, newRows);
            if (newCols == source.width && newRows == source.height)
                return CloneGrid(source);

            var newGrid = CreateEmptyGrid(newCols, newRows, source.gridRefSize);
            if (source.collision == null) return newGrid;

            for (int dr = 0; dr < newRows; dr++)
            {
                float srcRowStart = (float)dr / newRows * source.height;
                float srcRowEnd   = (float)(dr + 1) / newRows * source.height;
                for (int dc = 0; dc < newCols; dc++)
                {
                    float srcColStart = (float)dc / newCols * source.width;
                    float srcColEnd   = (float)(dc + 1) / newCols * source.width;

                    bool solid = false;
                    int sr0 = Mathf.FloorToInt(srcRowStart);
                    int sr1 = Mathf.CeilToInt(srcRowEnd);
                    int sc0 = Mathf.FloorToInt(srcColStart);
                    int sc1 = Mathf.CeilToInt(srcColEnd);
                    for (int sr = sr0; sr < sr1 && sr < source.height && !solid; sr++)
                    {
                        if (sr < 0 || source.collision[sr] == null) continue;
                        for (int sc = sc0; sc < sc1 && sc < source.width; sc++)
                        {
                            if (sc < 0 || sc >= source.collision[sr].Length) continue;
                            if (source.collision[sr][sc] == "#") { solid = true; break; }
                        }
                    }
                    newGrid.collision[dr][dc] = solid ? "#" : ".";
                }
            }
            return newGrid;
        }
    }
}
