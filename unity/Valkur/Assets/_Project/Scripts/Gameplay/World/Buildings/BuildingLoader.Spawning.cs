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
                inst.ZBottomOffset, inst.ZTopOffset, inst.DoorSpec, inst.InteractableOverride) != null;
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
                Vector2Int.zero, -1f, string.Empty, 0, 0, doorSpec: null, interactableOverride: -1);
        }

        private BuildingObject SpawnAtCore(int instanceId, string zoneName,
            BuildingTemplateData template, Vector3 worldPos,
            Vector2Int scaleOverride, float splitRatioOverride,
            string colliderScopeOverride, int zBottomOffset, int zTopOffset,
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
            if (zBottomOffset != 0) bObj.ZBottomOffset = zBottomOffset;
            if (zTopOffset    != 0) bObj.ZTopOffset    = zTopOffset;

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

                    if (overrides.TryGetValue("z_bottom", out var zBotRaw) && zBotRaw != null)
                        inst.ZBottomOffset = Convert.ToInt32(zBotRaw);

                    if (overrides.TryGetValue("z_top", out var zTopRaw) && zTopRaw != null)
                        inst.ZTopOffset = Convert.ToInt32(zTopRaw);

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
            /// <summary>Sorting order delta for the bottom (WallsBottom) renderer. 0 = no override.</summary>
            public int        ZBottomOffset;
            /// <summary>Sorting order delta for the top (WallsTop) renderer. 0 = no override.</summary>
            public int        ZTopOffset;
            /// <summary>-1 = inherit template.interactable; 0 = off; 1 = on.</summary>
            public int        InteractableOverride;
            /// <summary>Per-instance doorway destination, or null when this placement has none.</summary>
            public BuildingDoorSpec DoorSpec;
        }
    }
}