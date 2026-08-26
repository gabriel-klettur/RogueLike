using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Single owner of the same-scene world swap: leave the base world for an overlay
    /// (an interior, a dungeon) and come back out again.
    ///
    /// Extracted from <see cref="ZonePortal"/> when building doors arrived. Both callers go
    /// through here, so there is exactly ONE implementation of "move the player to another
    /// world" to keep correct — a second copy would drift the first time one of them learned
    /// about a subsystem the other did not.
    ///
    /// A swap is far more than repainting tiles, and the old two-line version was wrong in
    /// five separate ways that only show up end to end:
    ///
    ///   • WORLD CONTENT IS NOT TILES. <c>WorldGridBuilder.ClearWorld</c> calls
    ///     <c>ClearAllTiles</c> on the tilemaps and destroys nothing else, so every placed
    ///     building, light, spawner, particle emitter and their colliders used to survive
    ///     into the interior and float over it, walls and all.
    ///   • CLEARING BEFORE VALIDATING IS NOT TRANSACTIONAL. A typo'd destination wiped the
    ///     world and then failed to load anything, leaving the player in a black void with
    ///     no way back. The destination is now parsed BEFORE anything is torn down.
    ///   • THE PINNED ZONE NAME SURVIVED ONE FRAME. <c>ZoneManager.Update</c> re-detects a
    ///     base-world zone from the player's interior coordinates and overwrites it, taking
    ///     the music with it. Detection is suspended for as long as an interior is loaded.
    ///   • A TELEPORT LEAVES RESIDUAL VELOCITY, and a Dynamic body that has come to rest
    ///     goes to sleep — a sleeping body starts no new contacts, so the exit trigger the
    ///     player is standing on would never fire.
    ///   • THERE WAS NO WAY OUT. Entering recorded a return point that nothing consumed.
    ///     Every entry now also drops an <see cref="InteriorExit"/> on the arrival tile.
    /// </summary>
    public static class WorldTransitionService
    {
        /// <summary>Fallback landing spot when a caller asks for the destination's default.</summary>
        public static readonly Vector2 DEFAULT_SPAWN = new Vector2(25f, 25f);

        /// <summary>Name of the scene object that parents the interior's exit trigger.</summary>
        public const string EXIT_ROOT_NAME = "[InteriorExit]";

        /// <summary>
        /// Overlay currently swapped in, or empty for the base world assembled by
        /// <c>WorldLoader</c> from its per-zone overlays. Not a substitute for
        /// <c>ZoneManager</c>'s zone name — this tracks the swap, not the player's location.
        /// </summary>
        public static string CurrentOverlay { get; private set; } = "";

        /// <summary>
        /// True from the moment the base world's content is torn down for a transition until
        /// it has been rebuilt.
        ///
        /// This exists because the swap creates a window in which the SCENE legitimately holds
        /// no buildings, lights, spawners or emitters while their FILES still hold hundreds.
        /// Every runtime editor force-saves the scene on each edit, and an autosave that fires
        /// in that window writes the emptiness over the authored world. Their own anti-wipe
        /// guards catch the obvious shapes of that, but a guard that infers intent from counts
        /// can only ever guess; this states the fact.
        /// </summary>
        public static bool IsBaseWorldContentSuspended { get; private set; }

        /// <summary>
        /// Common refusal for any save path that must not run while the world is torn down.
        /// Returns true when the caller should abandon the write.
        /// </summary>
        public static bool RefuseWorldContentWrite(string subsystem)
        {
            if (!IsBaseWorldContentSuspended) return false;

            Debug.LogWarning(
                $"[WorldTransition] Refusing a {subsystem} save: the base world is torn down for " +
                "a transition, so the scene holds none of it. Writing now would persist that " +
                "emptiness over the authored world. Leave the interior first.");
            return true;
        }

        private static bool    s_hasReturnPoint;
        private static string  s_returnOverlay = "";
        private static Vector2 s_returnPosition;

        // Domain Reload is OFF in this project, so static state survives Play-mode restarts
        // and would hand session N+1 a return point recorded in session N — sending the
        // player to a position in a world that is no longer loaded.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            CurrentOverlay   = "";
            s_hasReturnPoint = false;
            s_returnOverlay  = "";
            s_returnPosition = Vector2.zero;
            IsBaseWorldContentSuspended = false;
        }

        // ── Return point ────────────────────────────────────────────────────────────

        /// <summary>Where the player came from, if anything recorded it.</summary>
        public readonly struct ReturnPoint
        {
            public readonly string  Overlay;
            public readonly Vector2 WorldPosition;

            public ReturnPoint(string overlay, Vector2 worldPosition)
            {
                Overlay       = overlay ?? "";
                WorldPosition = worldPosition;
            }

            /// <summary>An empty overlay means the BASE world, which is a valid destination.</summary>
            public bool IsBaseWorld => string.IsNullOrEmpty(Overlay);
        }

        public static bool HasReturnPoint => s_hasReturnPoint;

        /// <summary>
        /// Remember where to come back to. Overwrites any previous record rather than
        /// stacking: a stack would let an unfinished trip leak into the next one, and
        /// nothing in the game nests interiors two deep. Phase 3 revisits this if it does.
        /// </summary>
        public static void RecordReturnPoint(string overlay, Vector2 worldPosition)
        {
            s_returnOverlay  = overlay ?? "";
            s_returnPosition = worldPosition;
            s_hasReturnPoint = true;
        }

        /// <summary>Take the recorded return point and clear it. False when none was recorded.</summary>
        public static bool TryConsumeReturnPoint(out ReturnPoint point)
        {
            if (!s_hasReturnPoint)
            {
                point = default;
                return false;
            }
            point = new ReturnPoint(s_returnOverlay, s_returnPosition);
            s_hasReturnPoint = false;
            s_returnOverlay  = "";
            s_returnPosition = Vector2.zero;
            return true;
        }

        public static void ClearReturnPoint()
        {
            s_hasReturnPoint = false;
            s_returnOverlay  = "";
            s_returnPosition = Vector2.zero;
        }

        // ── Destination validation ──────────────────────────────────────────────────

        /// <summary>Absolute path an overlay filename resolves to. Mirrors OverlayLoader.</summary>
        public static string ResolveOverlayPath(string overlayFileName)
            => Path.Combine(Application.streamingAssetsPath, "Maps", overlayFileName ?? "");

        /// <summary>
        /// True when the destination exists on disk, parses, and declares the one key the
        /// loader actually reads. Checked BEFORE anything is torn down: OverlayLoader's own
        /// failure path only logs and returns, which — after the world had already been
        /// cleared — left the player in an empty world with no way back.
        /// </summary>
        public static bool IsOverlayLoadable(string overlayFileName)
        {
            if (string.IsNullOrWhiteSpace(overlayFileName)) return false;
            var root = OverlayLoader.ParseOverlay(ResolveOverlayPath(overlayFileName));
            return root != null && root.ContainsKey("layers");
        }

        // ── Entering an overlay ─────────────────────────────────────────────────────

        /// <summary>
        /// Swap the loaded world for <paramref name="overlayFileName"/> and place
        /// <paramref name="player"/> at <paramref name="spawn"/> (or
        /// <see cref="DEFAULT_SPAWN"/> when <paramref name="useDefaultSpawn"/>).
        ///
        /// Returns false — and changes NOTHING — when the destination is unusable or the
        /// scene is not in a state that can perform the swap.
        /// </summary>
        public static bool EnterOverlay(string overlayFileName,
                                        Vector2 spawn,
                                        bool useDefaultSpawn,
                                        GameObject player,
                                        WorldGridBuilder gridBuilder = null,
                                        ZoneManager zoneManager = null)
        {
            if (string.IsNullOrWhiteSpace(overlayFileName))
            {
                Debug.LogWarning("[WorldTransition] Refused a transition with no destination overlay.");
                return false;
            }

            var builder = gridBuilder != null ? gridBuilder : Object.FindObjectOfType<WorldGridBuilder>();
            if (builder == null)
            {
                Debug.LogError($"[WorldTransition] No WorldGridBuilder in the scene — cannot load " +
                               $"'{overlayFileName}'. The world is left untouched.");
                return false;
            }

            if (!IsOverlayLoadable(overlayFileName))
            {
                Debug.LogError($"[WorldTransition] Destination overlay '{overlayFileName}' is missing, " +
                               $"unparsable, or has no 'layers' key ({ResolveOverlayPath(overlayFileName)}). " +
                               "The world is left untouched — clearing it first would strand the player " +
                               "in an empty world with no way back.");
                return false;
            }

            Vector2 destination = useDefaultSpawn ? DEFAULT_SPAWN : spawn;
            string  zoneName    = Path.GetFileNameWithoutExtension(overlayFileName);

            // Base-world GameObjects are not part of the interior. They outlive ClearWorld,
            // which only touches Tilemaps, so they have to be destroyed explicitly.
            ClearBaseWorldContent();
            DespawnInteriorExit();

            RepaintTiles(builder, () => OverlayLoader.LoadOverlay(overlayFileName, builder));

            TeleportPlayer(player, destination);

            var zm = zoneManager != null ? zoneManager : Object.FindObjectOfType<ZoneManager>();
            if (zm != null) zm.SuspendDetection(zoneName);

            ServiceLocator.Get<IAudioService>()?.OnZoneChanged(zoneName);

            CurrentOverlay = overlayFileName;
            SpawnInteriorExit(destination);

            Debug.Log($"[WorldTransition] Entered overlay '{overlayFileName}'. Player at {destination}.");
            return true;
        }

        // ── Leaving an overlay ──────────────────────────────────────────────────────

        /// <summary>
        /// Take the player back where the last transition came from. Returns false when no
        /// return point was recorded, or when the trip back could not be performed — in
        /// which case the return point is PUT BACK, so the exit stays usable instead of
        /// stranding the player in the interior forever.
        /// </summary>
        public static bool ReturnToCaller(GameObject player,
                                          WorldGridBuilder gridBuilder = null,
                                          ZoneManager zoneManager = null)
        {
            if (!TryConsumeReturnPoint(out var point))
            {
                Debug.LogWarning("[WorldTransition] Nothing to return to — no return point was recorded.");
                return false;
            }

            if (!point.IsBaseWorld)
            {
                bool nested = EnterOverlay(point.Overlay, point.WorldPosition,
                                           useDefaultSpawn: false, player, gridBuilder, zoneManager);
                if (!nested) RecordReturnPoint(point.Overlay, point.WorldPosition);
                return nested;
            }

            var builder = gridBuilder != null ? gridBuilder : Object.FindObjectOfType<WorldGridBuilder>();
            var world   = Object.FindObjectOfType<WorldLoader>();
            if (builder == null || world == null)
            {
                Debug.LogError("[WorldTransition] WorldGridBuilder or WorldLoader missing — cannot rebuild " +
                               "the base world. The return point is kept so the exit stays usable.");
                RecordReturnPoint(point.Overlay, point.WorldPosition);
                return false;
            }

            DespawnInteriorExit();

            // The base world is many overlays plus its collision grids, so it is rebuilt
            // through WorldLoader rather than through a single LoadOverlay — the same recipe
            // the `reloadtiles` console command uses.
            RepaintTiles(builder, () => world.LoadFullWorld());

            // World content was destroyed on the way in and has to come back with the tiles.
            ReloadBaseWorldContent();

            TeleportPlayer(player, point.WorldPosition);

            CurrentOverlay = "";

            // Resume AFTER the player is back in base-world coordinates, so the first
            // detection reads the zone they actually returned to.
            var zm = zoneManager != null ? zoneManager : Object.FindObjectOfType<ZoneManager>();
            if (zm != null) zm.ResumeDetection();

            Debug.Log($"[WorldTransition] Returned to the base world at {point.WorldPosition}.");
            return true;
        }

        // ── Steps ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Wipe every tilemap, run <paramref name="paint"/>, then re-bake the tile colliders.
        /// The baker also self-heals from tilemapTileChanged, but an explicit rebuild keeps
        /// the world walkable on the SAME frame the player is dropped into it.
        /// </summary>
        private static void RepaintTiles(WorldGridBuilder builder, System.Action paint)
        {
            TerrainCatalogLoader.InvalidateCache();
            builder.ClearWorld();
            paint();
            if (WorldCollisionBaker.HasInstance)
                WorldCollisionBaker.Instance.RebuildAll();
        }

        /// <summary>
        /// Destroy the base world's placed buildings, spawners, lights and particle emitters.
        /// They are GameObjects, so ClearWorld leaves every one of them — including each
        /// building's per-cell BoxCollider2Ds, which would keep blocking movement inside an
        /// interior that has no buildings at all.
        /// </summary>
        private static void ClearBaseWorldContent()
        {
            var manager = Object.FindObjectOfType<MapEditorManager>();
            if (manager == null)
            {
                Debug.LogWarning("[WorldTransition] No MapEditorManager in the scene — the base world's " +
                                 "buildings, lights, spawners and particles will remain visible inside " +
                                 "the destination.");
                return;
            }
            // Raised BEFORE the clear, not after: ClearAllSpawnedWorldContent destroys the
            // components, and anything that reacts to that by saving has to already see the
            // suspension.
            IsBaseWorldContentSuspended = true;
            manager.ClearAllSpawnedWorldContent();
        }

        /// <summary>Re-spawn the active map slot's world content after returning.</summary>
        private static void ReloadBaseWorldContent()
        {
            var manager = Object.FindObjectOfType<MapEditorManager>();
            if (manager == null)
            {
                Debug.LogError("[WorldTransition] No MapEditorManager in the scene — the base world's " +
                               "buildings, lights, spawners and particles cannot be restored. Run " +
                               "'reloadworld' in the dev console to recover.");
                return;
            }
            manager.ClearAllSpawnedWorldContent();
            manager.ReloadAllWorldContent();

            // Only now is the scene a faithful mirror of the files again, so saves are safe.
            IsBaseWorldContentSuspended = false;
        }

        /// <summary>
        /// Put the player down, and leave the rigidbody in a state that can start new contacts.
        ///
        /// Two failure modes hide here. Residual velocity: locomotion writes
        /// <c>Rigidbody2D.velocity</c> every FixedUpdate and a teleport does not clear it, so
        /// the player arrives still moving in whatever direction they walked into the doorway.
        /// And sleep: a Dynamic body that has come to rest stops simulating, and a SLEEPING
        /// BODY STARTS NO NEW CONTACTS — the exit trigger under a player who arrives at rest
        /// would never fire.
        /// </summary>
        private static void TeleportPlayer(GameObject player, Vector2 destination)
        {
            var playerTransform = player != null ? player.transform : EntityRegistry.PlayerTransform;
            if (playerTransform == null)
            {
                Debug.LogWarning("[WorldTransition] World swapped but no player transform was resolvable — " +
                                 "the player keeps its previous position.");
                return;
            }

            playerTransform.position = new Vector3(destination.x, destination.y, 0f);

            var body = playerTransform.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.velocity        = Vector2.zero;
                body.angularVelocity = 0f;
                body.WakeUp();
            }
        }

        // ── The way out ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Drop an <see cref="InteriorExit"/> on the tile the player arrives on.
        ///
        /// Placing it on the arrival tile is what removes ALL authoring burden: an interior
        /// is a hand-drawn overlay with no components in it, and asking an author to also
        /// place an exit is a step they will forget once and then be trapped by. Arriving on
        /// the trigger is handled by the exit itself, which arms only once the player has
        /// stepped off it.
        /// </summary>
        private static void SpawnInteriorExit(Vector2 worldPosition)
        {
            DespawnInteriorExit();

            var go = new GameObject(EXIT_ROOT_NAME);
            go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
            go.AddComponent<InteriorExit>();
        }

        /// <summary>Remove the interior's exit trigger, if one is present.</summary>
        public static void DespawnInteriorExit()
        {
            var existing = Object.FindObjectOfType<InteriorExit>();
            if (existing == null) return;

            if (Application.isPlaying) Object.Destroy(existing.gameObject);
            else                       Object.DestroyImmediate(existing.gameObject);
        }
    }
}
