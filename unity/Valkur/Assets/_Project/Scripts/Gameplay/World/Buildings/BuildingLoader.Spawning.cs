using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    public partial class BuildingLoader : MonoBehaviour
    {

        // ── Spawning ───────────────────────────────────────────────────────────────

        private bool SpawnInstance(BuildingInstanceDto inst)
        {
            var template = _catalog.GetById(inst.TemplateId);
            if (template == null)
            {
                Debug.LogWarning(
                    $"[BuildingLoader] Template id={inst.TemplateId} not found " +
                    $"(instance id={inst.Id}, zone={inst.Zone}).");
                return false;
            }

            if (!_zoneManager.TryGetZone(inst.Zone, out var zoneDef))
            {
                Debug.LogWarning(
                    $"[BuildingLoader] Zone '{inst.Zone}' not registered in ZoneManager " +
                    $"(instance id={inst.Id}). Add it to the ZoneManager component.");
                return false;
            }

            // Effective pixel dimensions (instance override or template default)
            int effW = (inst.ScaleOverride.x > 0) ? inst.ScaleOverride.x : template.originalScale.x;
            int effH = (inst.ScaleOverride.y > 0) ? inst.ScaleOverride.y : template.originalScale.y;

            // ── Coordinate conversion ────────────────────────────────────────────
            // Python: top-left of building at (zone_gridOffset_tiles + rel_px/32), Y-down.
            // Unity:  bottom-center of building, Y-up.
            //
            // unityX = gridOffset.x + (rel_x + effW/2) / PPU
            // unityY = gridOffset.y + (zoneHeight - 1) - (rel_y + effH) / PPU
            //   (mirrors OverlayLoader.flippedY = zoneHeight-1 - rowIndex)
            int   zoneH  = _zoneManager.ZoneHeightTiles;
            float worldX = zoneDef.gridOffset.x + (inst.RelX + effW * 0.5f) / PPU;
            float worldY = zoneDef.gridOffset.y + (zoneH - 1) - (inst.RelY + effH) / PPU;

            return SpawnAtCore(inst.Id, inst.Zone, template, new Vector3(worldX, worldY, 0f),
                inst.ScaleOverride, inst.SplitRatioOverride, inst.ColliderScopeOverride,
                inst.ZBottom, inst.ZTop, inst.DoorSpec, inst.InteractableOverride) != null;
        }

        /// <summary>
        /// Public entry point for code paths that already know the desired bottom-center
        /// world position (e.g. the Map Editor biome generator) and don't need the
        /// pixel/Y-flip math used when loading from <c>buildings_instances.json</c>.
        /// Returns the spawned <see cref="BuildingObject"/>, or <c>null</c> on failure.
        /// </summary>
        public BuildingObject SpawnAtWorldPosition(int templateId, string zoneName,
            Vector3 worldPosition, int instanceId)
        {
            if (_catalog == null)
            {
                Debug.LogWarning("[BuildingLoader] Catalog not assigned; cannot spawn at world position.");
                return null;
            }
            var template = _catalog.GetById(templateId);
            if (template == null)
            {
                Debug.LogWarning($"[BuildingLoader] Template id={templateId} not found (programmatic spawn).");
                return null;
            }
            return SpawnAtCore(instanceId, zoneName, template, worldPosition,
                Vector2Int.zero, -1f, string.Empty,
                SortingConfig.DEFAULT_PROP_Z_BOTTOM, SortingConfig.DEFAULT_PROP_Z_TOP,
                doorSpec: null, interactableOverride: -1);
        }

        private BuildingObject SpawnAtCore(int instanceId, string zoneName,
            BuildingTemplateData template, Vector3 worldPos,
            Vector2Int scaleOverride, float splitRatioOverride,
            string colliderScopeOverride, int zBottom, int zTop,
            BuildingDoorSpec doorSpec, int interactableOverride = -1)
        {
            Transform root = _buildingsRoot != null ? _buildingsRoot : transform;

            var go = new GameObject($"Building_{instanceId}_{template.name}");
            go.transform.SetParent(root, worldPositionStays: false);
            go.transform.position = worldPos;
            go.layer = _buildingPhysicsLayer;

            var bObj = go.AddComponent<BuildingObject>();
            bObj.ZoneName             = zoneName;
            bObj.InstanceId           = instanceId;
            bObj.Apply(template, scaleOverride, splitRatioOverride);
            bObj.ColliderScopeOverride = colliderScopeOverride;
            bObj.InteractableOverride  = interactableOverride;
            bObj.ZBottom = zBottom;
            bObj.ZTop    = zTop;

            // After Apply(): the doorway rect is derived from the building's world bounds,
            // which only exist once the renderers have been built. The factory refuses (and
            // reports) any combination that cannot produce a working door.
            BuildingDoorFactory.TryAttach(bObj, doorSpec);

            // Durability is opt-in per template. The overwhelming majority declare no
            // profile, and those pay nothing: no component, and no entry in the obstacle
            // registry every swing in the game would then have to walk past.
            // Durability is what makes a building breakable by a BLOW, and only a Destroy-mode
            // profile is. A Deplete-mode node (a mine, an ore seam) deliberately never enters
            // the obstacle registry: you cannot delete a hillside with a stray fireball, and a
            // seam that could be would be exhausted by accident from across the room.
            BuildingDurability durability = null;
            if (template.destruction != null &&
                template.destruction.harvestMode == HarvestMode.Destroy)
            {
                durability = bObj.gameObject.AddComponent<BuildingDurability>();
                durability.Initialize(template.destruction, bObj);
            }

            // Harvesting is opt-in on top of that: a barricade can be destructible without
            // being workable by hand, and a mine workable without being destructible.
            HarvestNode node = null;
            if (template.destruction != null && template.destruction.harvestable)
            {
                node = bObj.gameObject.AddComponent<HarvestNode>();
                node.Initialize(template.destruction, bObj, durability);
            }

            // The save layer adopts LAST, because a restore writes through both components'
            // own clamping entry points and cannot run before they exist. It is also the only
            // place this run's damage is put back: the building's PLACEMENT came from authored
            // world data, and what the player did to it did not.
            if ((durability != null || node != null) &&
                ServiceLocator.TryGet<WorldDamageService>(out var worldDamage) && worldDamage != null)
            {
                worldDamage.Adopt(durability, node);
            }

            _spawnedBuildings.Add(bObj);
            return bObj;
        }

        // ── JSON parsing ────────────────────────────────────────────────────────────

        private static List<BuildingInstanceDto> ParseInstances(string json)
        {
            var result = new List<BuildingInstanceDto>();

            var raw = MiniJsonRuntime.Deserialize(json) as List<object>;
            if (raw == null)
            {
                Debug.LogError("[BuildingLoader] Failed to parse instances JSON — expected a JSON array.");
                return result;
            }

            foreach (var item in raw)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                var inst = new BuildingInstanceDto
                {
                    Id               = GetInt(dict, "id"),
                    ZBottom          = SortingConfig.DEFAULT_PROP_Z_BOTTOM,
                    ZTop             = SortingConfig.DEFAULT_PROP_Z_TOP,
                    TemplateId       = GetInt(dict, "template_id"),
                    Zone             = GetString(dict, "zone", "Lobby"),
                    RelX             = GetInt(dict, "rel_x"),
                    RelY             = GetInt(dict, "rel_y"),
                    SplitRatioOverride = -1f,          // default: no override
                    InteractableOverride = -1,          // default: inherit template
                };

                // Optional 'overrides' block
                if (dict.TryGetValue("overrides", out var ovRaw) &&
                    ovRaw is Dictionary<string, object> overrides)
                {
                    if (overrides.TryGetValue("scale", out var scaleRaw) &&
                        scaleRaw is List<object> scaleList && scaleList.Count >= 2)
                    {
                        inst.ScaleOverride = new Vector2Int(
                            Convert.ToInt32(scaleList[0]),
                            Convert.ToInt32(scaleList[1]));
                    }

                    if (overrides.TryGetValue("split_ratio", out var srRaw))
                        inst.SplitRatioOverride = Convert.ToSingle(srRaw);

                    if (overrides.TryGetValue("collider_scope", out var scopeRaw) && scopeRaw != null)
                        inst.ColliderScopeOverride = scopeRaw.ToString();

                    // Z is a tile-layer index (0..8) and rides on layer_bottom / layer_top.
                    // The keys it replaced, z_bottom / z_top, held a signed TIER whose only
                    // effect on the sorting LAYER was its sign: a positive bottom promoted the
                    // footprint onto WallsTop, a negative top demoted the canopy onto
                    // WallsBottom. A row still carrying them is read through that same rule so
                    // it comes back at the depth it was authored at, and the next save rewrites
                    // it in the new keys. The magnitude is dropped on purpose: it ordered
                    // buildings against each other inside one slot, and the Y-sort owns that.
                    if (overrides.TryGetValue("layer_bottom", out var lbRaw) && lbRaw != null)
                        inst.ZBottom = Convert.ToInt32(lbRaw);
                    else if (overrides.TryGetValue("z_bottom", out var zBotRaw) && zBotRaw != null)
                        inst.ZBottom = LegacyZBottomToLayer(Convert.ToInt32(zBotRaw));

                    if (overrides.TryGetValue("layer_top", out var ltRaw) && ltRaw != null)
                        inst.ZTop = Convert.ToInt32(ltRaw);
                    else if (overrides.TryGetValue("z_top", out var zTopRaw) && zTopRaw != null)
                        inst.ZTop = LegacyZTopToLayer(Convert.ToInt32(zTopRaw));

                    if (overrides.TryGetValue("interactable", out var iaRaw) && iaRaw != null)
                        inst.InteractableOverride = Convert.ToInt32(iaRaw);

                    if (overrides.TryGetValue("door", out var doorRaw) &&
                        doorRaw is Dictionary<string, object> doorDict)
                        inst.DoorSpec = ParseDoorSpec(doorDict);
                }

                result.Add(inst);
            }

            return result;
        }

        /// <summary>
        /// Read one <c>overrides.door</c> block. Returns null for a record with no usable
        /// destination rather than an inert spec: the factory treats null as "this placement
        /// leads nowhere", which is the correct reading of a door entry someone emptied.
        ///
        /// The keys are the exact ones BuildingsRuntimeEditor.SaveInstancesToJson writes.
        /// Read and write are a PAIR — a change to either side without the other is the
        /// failure mode that shipped the spawner coordinate drift
        /// (.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md), and
        /// BuildingDoorPersistenceRoundTripTests exists to make the pair fail loudly.
        /// </summary>
        private static BuildingDoorSpec ParseDoorSpec(Dictionary<string, object> door)
        {
            string target = GetString(door, "target");
            if (string.IsNullOrWhiteSpace(target)) return null;

            var spec = new BuildingDoorSpec { target = target };

            if (door.TryGetValue("use_default_spawn", out var defRaw) && defRaw != null)
                spec.useDefaultSpawn = Convert.ToBoolean(defRaw);

            if (door.TryGetValue("spawn_x", out var sxRaw) && sxRaw != null)
                spec.spawnX = Convert.ToSingle(sxRaw);

            if (door.TryGetValue("spawn_y", out var syRaw) && syRaw != null)
                spec.spawnY = Convert.ToSingle(syRaw);

            spec.prompt = GetString(door, "prompt");
            return spec;
        }

        // ── JSON helpers ────────────────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return fallback;
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s)
                return s;
            return fallback;
        }

        // ── DTO ─────────────────────────────────────────────────────────────────────

        /// <summary>Parsed representation of one buildings_instances.json entry.</summary>
        /// <summary>
        /// The slot a legacy signed <c>z_bottom</c> tier resolved to: any positive value promoted
        /// the footprint onto WallsTop (= above layer 6), anything else left it on WallsBottom.
        /// </summary>
        public static int LegacyZBottomToLayer(int zBottomTier)
            => zBottomTier > 0 ? SortingConfig.DEFAULT_PROP_Z_TOP : SortingConfig.DEFAULT_PROP_Z_BOTTOM;

        /// <summary>
        /// The slot a legacy signed <c>z_top</c> tier resolved to: any negative value demoted
        /// the canopy onto WallsBottom (= above layer 4), anything else left it on WallsTop.
        /// </summary>
        public static int LegacyZTopToLayer(int zTopTier)
            => zTopTier < 0 ? SortingConfig.DEFAULT_PROP_Z_BOTTOM : SortingConfig.DEFAULT_PROP_Z_TOP;

        private struct BuildingInstanceDto
        {
            public int        Id;
            public int        TemplateId;
            public string     Zone;
            public int        RelX;
            public int        RelY;
            /// <summary>(0,0) = use template.originalScale.</summary>
            public Vector2Int ScaleOverride;
            /// <summary>Negative = use template.splitRatio.</summary>
            public float      SplitRatioOverride;
            /// <summary>Empty = use template.colliderScope.</summary>
            public string     ColliderScopeOverride;
            /// <summary>Tile layer (0..8) the footprint sits directly above.</summary>
            public int        ZBottom;
            /// <summary>Tile layer (0..8) the canopy sits directly above.</summary>
            public int        ZTop;
            /// <summary>-1 = inherit template.interactable; 0 = off; 1 = on.</summary>
            public int        InteractableOverride;
            /// <summary>Per-instance doorway destination, or null when this placement has none.</summary>
            public BuildingDoorSpec DoorSpec;
        }
    }
}