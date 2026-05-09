using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World.Dungeon.Strategy;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Bootstrap
{
    /// <summary>
    /// Listens to <see cref="MapEditorManager.OnMapSlotsChanged"/> and, when
    /// the just-loaded slot's name signals a Udemy-style dungeon, runs the
    /// <see cref="UdemyDungeonStrategy"/> against the global tilemap and
    /// teleports the player to the dungeon entrance.
    ///
    /// Heuristic for "is this a Udemy slot?": the slot name starts with
    /// "Dungeon" (case-insensitive). Picks up the user's "Dungeon v1" slot
    /// without requiring a schema migration of <c>ZonePersistenceFile</c>.
    /// The full-fidelity per-slot <c>dungeonStrategyId</c> field is still on
    /// the follow-up list — this gives us the working flow today.
    ///
    /// Lifetime: instantiated once by <c>GameplaySceneSetup</c> after the
    /// MapEditor exists. Cleans up the previous strategy on every slot
    /// switch so we don't leak GameObjects between dungeons. Verbose-by-
    /// default logging because slot regeneration is a low-frequency event
    /// where silent failures are very confusing for a user holding F11.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonSlotBootstrap : MonoBehaviour
    {
        // Resources path (Resources.Load drops the leading "Resources/" folder).
        private const string DungeonLevelResourcePath = "Dungeon/Samples/DungeonLevel_Demo";
        private const string NodeTypeListResourcePath = "Dungeon/Samples/RoomNodeTypeList";
        private const string DungeonConfigResourcePath = "Dungeon/Samples/DungeonConfig_Default";

        private const string UdemySlotPrefix = "Dungeon";

        private UdemyDungeonStrategy _activeStrategy;
        private string _lastHandledSlot = "<none>"; // sentinel: never handled
        private bool _subscribed;

        private void Awake()
        {
            Debug.Log("[DungeonSlotBootstrap] Awake — installed and waiting for MapEditorManager.");
        }

        private void OnEnable() => TrySubscribe();

        private void OnDisable()
        {
            var mgr = MapEditorManager.Instance;
            if (mgr != null) mgr.OnMapSlotsChanged -= HandleSlotsChanged;
            _subscribed = false;
        }

        private void Update()
        {
            // Manager is created lazily — keep retrying until we can latch on.
            if (!_subscribed) TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            var mgr = MapEditorManager.Instance;
            if (mgr == null) return;

            mgr.OnMapSlotsChanged += HandleSlotsChanged;
            _subscribed = true;
            Debug.Log($"[DungeonSlotBootstrap] Subscribed to OnMapSlotsChanged. " +
                      $"ActiveMapSlot='{mgr.ActiveMapSlot}'.");

            // Force a first pass so we pick up the slot that was already
            // active when bootstrap finished (the "load on start" case).
            HandleSlotsChanged();
        }

        private void HandleSlotsChanged()
        {
            var mgr = MapEditorManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[DungeonSlotBootstrap] OnMapSlotsChanged fired but Instance is null.");
                return;
            }

            var slot = mgr.ActiveMapSlot ?? string.Empty;
            Debug.Log($"[DungeonSlotBootstrap] HandleSlotsChanged → slot='{slot}', last='{_lastHandledSlot}'.");
            if (slot == _lastHandledSlot) return;
            _lastHandledSlot = slot;

            // Tear down whatever the previous slot generated (Udemy or BSP).
            if (_activeStrategy != null)
            {
                Debug.Log("[DungeonSlotBootstrap] Cleaning up previous Udemy strategy.");
                _activeStrategy.Cleanup();
                _activeStrategy = null;
            }

            if (!IsUdemySlot(slot))
            {
                Debug.Log($"[DungeonSlotBootstrap] Slot '{slot}' is not a Udemy slot (no '{UdemySlotPrefix}*' prefix). Skipping.");
                return;
            }

            Debug.Log($"[DungeonSlotBootstrap] Slot '{slot}' matches Udemy prefix → generating dungeon.");
            TryGenerateUdemyDungeon();
        }

        private static bool IsUdemySlot(string slotName)
        {
            return !string.IsNullOrEmpty(slotName)
                && slotName.StartsWith(UdemySlotPrefix, System.StringComparison.OrdinalIgnoreCase);
        }

        private void TryGenerateUdemyDungeon()
        {
            var level = Resources.Load<DungeonLevelSO>(DungeonLevelResourcePath);
            var nodeTypes = Resources.Load<RoomNodeTypeListSO>(NodeTypeListResourcePath);
            var config = Resources.Load<DungeonConfigSO>(DungeonConfigResourcePath);

            if (level == null || nodeTypes == null || config == null)
            {
                Debug.LogError(
                    $"[DungeonSlotBootstrap] Missing sample assets — level={level != null}, " +
                    $"nodeTypes={nodeTypes != null}, config={config != null}. " +
                    "Run 'Valkur > Dungeon > Create Sample Assets' first.");
                return;
            }

            Debug.Log($"[DungeonSlotBootstrap] Loaded assets: level='{level.levelName}', " +
                      $"nodeTypes ({nodeTypes.List.Count} types), config (default penalty={config.defaultMovementPenalty}).");

            var gridBuilder = FindObjectOfType<WorldGridBuilder>();
            if (gridBuilder == null)
            {
                Debug.LogError("[DungeonSlotBootstrap] No WorldGridBuilder in scene; cannot stamp dungeon.");
                return;
            }

            _activeStrategy = new UdemyDungeonStrategy(level, nodeTypes, config);
            DungeonStrategyResolver.Register(_activeStrategy);

            var ctx = new DungeonGenerationContext
            {
                GridBuilder = gridBuilder,
                DungeonOffsetX = 0,
                DungeonOffsetY = 0,
                ZoneHeight = 50,
                Seed = -1,
                SceneContainer = transform,
                WorldSlug = MapEditorManager.Instance?.ActiveMapSlot,
            };

            if (!_activeStrategy.TryGenerate(ctx, out var result))
            {
                Debug.LogError(
                    $"[DungeonSlotBootstrap] UdemyDungeonStrategy failed: {result.FailureReason}");
                return;
            }

            TeleportPlayerToEntrance(result.EntrancePosition);
            Debug.Log(
                $"[DungeonSlotBootstrap] ✅ Generated Udemy dungeon for slot '{MapEditorManager.Instance?.ActiveMapSlot}': " +
                $"{result.RoomBounds.Count} rooms, entrance tile @ {result.EntrancePosition}.");
        }

        // Mirrors MapEditorManager.TeleportPlayerToWorldPosition — same camera reset path.
        private static void TeleportPlayerToEntrance(Vector2Int entranceTile)
        {
            var playerT = EntityRegistry.PlayerTransform;
            if (playerT == null)
            {
                Debug.LogWarning("[DungeonSlotBootstrap] EntityRegistry.PlayerTransform is null; teleport skipped.");
                return;
            }

            // Tile coords are world units in Valkur (PPU = cellSize = 1f), with
            // a half-tile offset so the player stands on the tile center rather
            // than its lower-left corner.
            Vector3 newPos = new Vector3(entranceTile.x + 0.5f, entranceTile.y + 0.5f, playerT.position.z);
            Vector3 oldPos = playerT.position;
            playerT.position = newPos;

            var camSetup = CameraSetup.Instance;
            if (camSetup != null)
            {
                camSetup.ReattachFollow();
                camSetup.SnapToFollowTarget(newPos - oldPos);
            }
            Debug.Log($"[DungeonSlotBootstrap] Teleported player from {oldPos} → {newPos}.");
        }
    }
}
