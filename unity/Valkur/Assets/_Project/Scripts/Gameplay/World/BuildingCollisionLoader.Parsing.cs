using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public partial class BuildingCollisionLoader : MonoBehaviour
    {

        private static CollisionGrid ParseGrid(Dictionary<string, object> dict)
        {
            int width = dict.TryGetValue("width", out var w) ? Convert.ToInt32(w) : 0;
            int height = dict.TryGetValue("height", out var h) ? Convert.ToInt32(h) : 0;
            if (width <= 0 || height <= 0) return null;

            if (!dict.TryGetValue("collision", out var collObj) ||
                !(collObj is List<object> rows))
                return null;

            var collision = new string[rows.Count][];
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r] is List<object> cols)
                {
                    collision[r] = new string[cols.Count];
                    for (int c = 0; c < cols.Count; c++)
                        collision[r][c] = cols[c]?.ToString() ?? ".";
                }
                else
                {
                    collision[r] = new string[width];
                    for (int c = 0; c < width; c++)
                        collision[r][c] = ".";
                }
            }

            Vector2Int gridRefSize = Vector2Int.zero;
            if (dict.TryGetValue("grid_ref_size", out var grs) && grs is List<object> grsList && grsList.Count >= 2)
            {
                gridRefSize = new Vector2Int(Convert.ToInt32(grsList[0]), Convert.ToInt32(grsList[1]));
            }

            return new CollisionGrid
            {
                width = width,
                height = height,
                collision = collision,
                gridRefSize = gridRefSize
            };
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        private CollisionGrid ResolveGrid(BuildingObject bObj)
        {
            bool usePerInstanceScope = string.Equals(
                bObj.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);

            if (usePerInstanceScope)
            {
                string instanceKey = bObj.InstanceId.ToString();
                if (_inlineInstanceOverrides != null &&
                    _inlineInstanceOverrides.TryGetValue(instanceKey, out var inlineOverride))
                    return inlineOverride;

                if (_byInstanceId != null && _byInstanceId.TryGetValue(instanceKey, out var byInst))
                    return byInst;
            }

            // Priority 2: Per-spawn-id (future use; currently empty in base world)
            // Would need spawn_id on BuildingObject — skip for now

            if (bObj.Template != null && _byImage != null)
            {
                string assetKey = bObj.Template.sourceImagePath;
                if (!string.IsNullOrEmpty(assetKey))
                {
                    if (_byImage.TryGetValue(assetKey, out var byImg))
                        return byImg;
                    string normalizedKey = assetKey.Replace("\\", "/");
                    if (_byImage.TryGetValue(normalizedKey, out byImg))
                        return byImg;
                    string windowsKey = assetKey.Replace("/", "\\");
                    if (_byImage.TryGetValue(windowsKey, out byImg))
                        return byImg;
                }
            }

            return null;
        }

        private static bool HasSolidCells(CollisionGrid grid)
        {
            foreach (var row in grid.collision)
            {
                foreach (var cell in row)
                {
                    if (cell == "#") return true;
                }
            }

            return false;
        }

        private static string ResolveCollisionFilePath(string fileName, bool isGlobalData)
        {
            foreach (var candidate in GetCollisionFileCandidates(fileName, isGlobalData))
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string ResolveInstancesFilePath()
        {
            foreach (var candidate in GetInstanceFileCandidates())
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetCollisionFileCandidates(string fileName, bool isGlobalData)
        {
            yield return Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, fileName);

            string repoRoot = TryGetRepoRootPath();
            if (string.IsNullOrEmpty(repoRoot)) yield break;

            if (isGlobalData)
                yield return Path.Combine(repoRoot, "python", "data", "buildings", fileName);
            else
                yield return Path.Combine(repoRoot, "python", "data", "worlds", "base", "buildings", fileName);
        }

        private static IEnumerable<string> GetInstanceFileCandidates()
        {
            yield return Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILE);

            string repoRoot = TryGetRepoRootPath();
            if (string.IsNullOrEmpty(repoRoot)) yield break;

            yield return Path.Combine(repoRoot, "python", "data", "worlds", "base", "buildings", INSTANCES_FILE);
        }

        private static string TryGetRepoRootPath()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            }
            catch
            {
                return null;
            }
        }

        // Grid Application + ResampleGrid + CollisionGrid are in BuildingCollisionLoader.Grid.cs
    }
}