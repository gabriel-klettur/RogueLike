using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Tags the building with <see cref="targetInstanceId"/> as the resurrection
    /// altar by attaching a <see cref="ResurrectionZone"/> component to it.
    ///
    /// Polls <c>BuildingLoader.SpawnedBuildings</c> until the target instance is
    /// available (the loader spawns buildings asynchronously after Start), then
    /// adds the component once and stops polling.
    ///
    /// Configurable from the inspector if the altar building changes; defaults
    /// to instance 91 (template 249) per the design doc.
    /// </summary>
    public class ResurrectionZoneAutoBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("Building instance id whose footprint revives the player.")]
        private int targetInstanceId = 91;

        [SerializeField, Tooltip("Stop searching after this many seconds. 0 = forever.")]
        private float searchTimeout = 30f;

        [SerializeField, Tooltip("Seconds between scans of the BuildingLoader.")]
        private float scanInterval = 0.25f;

        private float _scanTimer;
        private float _elapsed;
        private bool  _bound;

        private void Update()
        {
            if (_bound) { enabled = false; return; }

            _elapsed   += Time.unscaledDeltaTime;
            _scanTimer += Time.unscaledDeltaTime;
            if (searchTimeout > 0f && _elapsed > searchTimeout)
            {
                Debug.LogWarning($"[ResurrectionZoneAutoBinder] Building #{targetInstanceId} never spawned within {searchTimeout:0.0}s; auto-bind aborted.");
                enabled = false;
                return;
            }
            if (_scanTimer < scanInterval) return;
            _scanTimer = 0f;

            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null) return;

            for (int i = 0; i < loader.SpawnedBuildings.Count; i++)
            {
                var b = loader.SpawnedBuildings[i];
                if (b == null) continue;
                if (b.InstanceId != targetInstanceId) continue;

                if (b.GetComponent<ResurrectionZone>() == null)
                    b.gameObject.AddComponent<ResurrectionZone>();

                _bound = true;
                Debug.Log($"[ResurrectionZoneAutoBinder] Bound resurrection zone to building #{targetInstanceId} (template {b.Template?.templateId}).");
                enabled = false;
                return;
            }
        }
    }
}
