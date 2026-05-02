using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World.Worlds
{
    /// <summary>
    /// Trigger collider that hands off the player to a different world via
    /// the runtime <see cref="IWorldManager"/>. Companion to the existing
    /// same-world <c>ZonePortal</c>: where ZonePortal swaps overlays inside
    /// the active world, WorldPortal switches the active <c>IWorldContext</c>
    /// itself.
    ///
    /// Phase 1 scope: load (or reuse) the destination world's context and
    /// call <c>ActivateAsync</c>. The actual scene rebuild (tilemap repaint,
    /// player respawn, Cinemachine bounds reset) is the responsibility of
    /// listeners on <c>IWorldManager.ActiveWorldChanged</c> — the portal
    /// itself stays a thin trigger so designers can reuse it across worlds
    /// without touching code.
    ///
    /// Coexists with ZonePortal: a designer that only needs an overlay swap
    /// keeps using ZonePortal; a designer crossing a dimension boundary
    /// drops a WorldPortal instead.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class WorldPortal : MonoBehaviour
    {
        [Header("Destination")]
        [Tooltip("Descriptor of the world this portal leads to. Required.")]
        [SerializeField] private WorldDescriptor destinationWorld;

        [Tooltip("Spawn position (tile coords) when arriving in the destination world. " +
                 "Falls back to the destination's WorldDescriptor.DefaultSpawnTile when zero.")]
        [SerializeField] private Vector2Int spawnTileOverride = Vector2Int.zero;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait between trigger and activation (visual breath room).")]
        [SerializeField] private float activationDelay = 0.35f;

        [Tooltip("Player tag that fires the portal. Other colliders are ignored.")]
        [SerializeField] private string playerTag = "Player";

        private bool _triggered;

        public WorldDescriptor Destination => destinationWorld;
        public Vector2Int SpawnTileOverride => spawnTileOverride;

        public Vector2Int ResolveSpawnTile()
        {
            if (spawnTileOverride != Vector2Int.zero) return spawnTileOverride;
            return destinationWorld != null ? destinationWorld.DefaultSpawnTile : Vector2Int.zero;
        }

        // ── Test seams ───────────────────────────────────────────────────────────
        // Tests cannot stand up a real OnTriggerEnter2D in EditMode; this
        // public entry point exposes the same activation flow so contract
        // tests can drive it directly.
        public IEnumerator ActivateForTest(IWorldManager managerOverride = null)
            => Activate(managerOverride);

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered || destinationWorld == null) return;
            if (!other.CompareTag(playerTag)) return;
            _triggered = true;
            StartCoroutine(Activate(managerOverride: null));
        }

        private IEnumerator Activate(IWorldManager managerOverride)
        {
            if (activationDelay > 0f)
                yield return new WaitForSeconds(activationDelay);

            var manager = managerOverride ?? ServiceLocator.Get<IWorldManager>();
            if (manager == null)
            {
                Debug.LogError("[WorldPortal] No IWorldManager registered in ServiceLocator — " +
                               "did GameplaySceneSetup.EnsureWorldManager run?");
                _triggered = false; // allow retry once the manager wires up
                yield break;
            }

            if (destinationWorld == null)
            {
                Debug.LogError("[WorldPortal] destinationWorld is not set — cannot activate.");
                yield break;
            }

            var task = manager.LoadAndActivateAsync(destinationWorld);
            // Non-blocking wait: yield until completion so the coroutine
            // surface stays cooperative with the rest of gameplay.
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                Debug.LogError($"[WorldPortal] LoadAndActivateAsync failed: {task.Exception?.GetBaseException().Message}");
                _triggered = false;
                yield break;
            }

            Debug.Log($"[WorldPortal] Active world is now '{manager.Active?.WorldId}'. " +
                      $"Spawn tile: {ResolveSpawnTile()}.");
        }
    }
}
