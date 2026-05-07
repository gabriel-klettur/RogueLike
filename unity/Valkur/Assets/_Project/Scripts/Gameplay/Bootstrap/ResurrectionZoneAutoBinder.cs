using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Tags every building whose template id matches <see cref="targetTemplateId"/>
    /// as a resurrection altar by attaching a <see cref="ResurrectionZone"/>
    /// component. Multiple altar instances can exist in a world — the spirit is
    /// routed to whichever is closest by <see cref="Combat.Death.SpiritAltarPathHighlighter"/>.
    ///
    /// Polls <c>BuildingLoader.SpawnedBuildings</c> until at least one altar is
    /// found AND the loader stops adding new ones, then stops polling.
    /// </summary>
    public class ResurrectionZoneAutoBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("Template id used to mark a building as resurrection altar. " +
                                 "Every BuildingObject with Template.templateId == this value " +
                                 "gets a ResurrectionZone component.")]
        private int targetTemplateId = 249;

        [SerializeField, Tooltip("Stop searching after this many seconds. 0 = forever.")]
        private float searchTimeout = 60f;

        [SerializeField, Tooltip("Seconds between scans of the BuildingLoader.")]
        private float scanInterval = 0.25f;

        private float _scanTimer;
        private float _elapsed;
        private int _boundCount;

        public int BoundCount => _boundCount;
        public int TargetTemplateId => targetTemplateId;

        private void Update()
        {
            _elapsed   += Time.unscaledDeltaTime;
            _scanTimer += Time.unscaledDeltaTime;
            if (searchTimeout > 0f && _elapsed > searchTimeout)
            {
                if (_boundCount == 0)
                    Debug.LogWarning($"[ResurrectionZoneAutoBinder] No building with template id={targetTemplateId} spawned within {searchTimeout:0.0}s; auto-bind aborted.");
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
                if (b == null || b.Template == null) continue;
                if (b.Template.templateId != targetTemplateId) continue;
                if (b.GetComponent<ResurrectionZone>() != null) continue;

                b.gameObject.AddComponent<ResurrectionZone>();
                _boundCount++;
                Debug.Log($"[ResurrectionZoneAutoBinder] Bound resurrection zone to building #{b.InstanceId} (template {b.Template.templateId}).");
            }
        }
    }
}
