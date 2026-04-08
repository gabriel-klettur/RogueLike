using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Zone portal trigger. Place as a trigger collider on a building/door tile.
    /// When the Player enters the trigger the portal fires and loads the destination.
    ///
    /// Mirrors Python BuildingPortalSystem: cross-world portals load a new overlay JSON
    /// and reset particle/entity state; same-map portals just teleport the player.
    ///
    /// Inspector fields:
    ///   DestinationZone   — overlay filename in StreamingAssets/Maps/  (e.g. "dungeon.overlay.json")
    ///   TeleportPosition  — in-world tile coords where the player lands (0,0 = use center of map)
    ///   IsSceneTransition — if true, loads a full new Unity scene instead of an overlay swap
    ///   DestinationScene  — scene name to load (only if IsSceneTransition == true)
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ZonePortal : MonoBehaviour
    {
        [Header("Destination")]
        [Tooltip("Overlay filename in StreamingAssets/Maps/ for same-scene zone swap. E.g. 'dungeon.overlay.json'")]
        [SerializeField] private string destinationOverlay = "";

        [Tooltip("Where the player lands after transition (world-unit coords). Zero = map center.")]
        [SerializeField] private Vector2 teleportPosition = Vector2.zero;

        [Tooltip("If true, load a full Unity scene instead of swapping the overlay.")]
        [SerializeField] private bool isSceneTransition;

        [Tooltip("Scene name to load when IsSceneTransition is true.")]
        [SerializeField] private string destinationScene = "";

        [Header("Visual")]
        [Tooltip("VFX shown when player steps on the portal.")]
        [SerializeField] private Color portalColor = new Color(0.4f, 0.2f, 1f, 0.8f);
        [SerializeField] private float activationRadius = 0.6f;

        private bool _triggered;

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered) return;
            if (!other.CompareTag("Player")) return;

            _triggered = true;
            StartCoroutine(ActivatePortal(other.gameObject));
        }

        private System.Collections.IEnumerator ActivatePortal(GameObject player)
        {
            // Brief flash at portal position
            SpawnPortalVFX(transform.position);
            yield return new WaitForSeconds(0.35f);

            if (isSceneTransition && !string.IsNullOrWhiteSpace(destinationScene))
            {
                SceneTransitionManager.LoadScene(destinationScene);
            }
            else if (!string.IsNullOrWhiteSpace(destinationOverlay))
            {
                var gridBuilder = Object.FindObjectOfType<WorldGridBuilder>();
                if (gridBuilder != null)
                {
                    // Clear existing world
                    gridBuilder.ClearWorld();

                    // Load new overlay
                    OverlayLoader.LoadOverlay(destinationOverlay, gridBuilder);

                    // Reposition player
                    Vector2 dest = teleportPosition.sqrMagnitude > 0.001f
                        ? teleportPosition
                        : new Vector2(25f, 25f); // default lobby center

                    player.transform.position = new Vector3(dest.x, dest.y, 0f);

                    // Notify zone system
                    var zm = Object.FindObjectOfType<ZoneManager>();
                    if (zm != null) zm.ForceZoneName(System.IO.Path.GetFileNameWithoutExtension(destinationOverlay));

                    // Notify audio
                    var audio = ServiceLocator.Get<IAudioService>();
                    audio?.OnZoneChanged(System.IO.Path.GetFileNameWithoutExtension(destinationOverlay));

                    Debug.Log($"[ZonePortal] Transitioned to overlay '{destinationOverlay}'. Player at {dest}");
                }
            }

            // Allow re-trigger after brief cooldown (e.g. portaling back)
            yield return new WaitForSeconds(1f);
            _triggered = false;
        }

        private void SpawnPortalVFX(Vector3 pos)
        {
            var vfx = Valkur.Gameplay.VFX.VFXManager.Instance;
            if (vfx != null)
                vfx.SpawnAreaIndicator(pos, portalColor, 1.2f, 0.6f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = portalColor;
            Gizmos.DrawWireSphere(transform.position, activationRadius);
        }
    }
}
