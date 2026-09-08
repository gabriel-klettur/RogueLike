using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Tags every building whose template is listed in <see cref="DeathTuning.altarTemplateIds"/>
    /// as a resurrection altar, by attaching a <see cref="ResurrectionZone"/>.
    ///
    /// <para>Polls <c>BuildingLoader.SpawnedBuildings</c> — the loader streams buildings in over
    /// several frames, so a single scan at boot finds a partial world. It keeps scanning for
    /// <see cref="DeathTuning.altarSearchTimeout"/> seconds and then stops.</para>
    ///
    /// <para><b>Giving up is now an EVENT, not a log line.</b> The shipped version wrote one
    /// <c>LogWarning</c> after sixty seconds and disabled itself; the audit of 2026-09-07 found
    /// zero altars in a 301-building world and nothing anywhere said so — the player was told
    /// "encuentra el altar para revivir" about a thing that did not exist.
    /// <see cref="SearchExhaustedWithNoAltar"/> is what <see cref="DeathSequenceController"/>'s
    /// rescue and the death banner both read, so the failure reaches the screen.</para>
    ///
    /// <para>It also re-scans after a WORLD SWAP. <c>WorldTransitionService</c> tears down and
    /// rebuilds every building, so the altars of the previous world are destroyed and the new
    /// world's have never been seen — a binder that had already finished would leave an interior
    /// with no altar and no explanation. <see cref="Rearm"/> is the seam; the count of spawned
    /// buildings dropping is the trigger, which needs no cooperation from the transition layer.</para>
    /// </summary>
    public class ResurrectionZoneAutoBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds between scans of the BuildingLoader.")]
        private float scanInterval = 0.25f;

        private float _scanTimer;
        private float _elapsed;
        private int _boundCount;
        private bool _searching = true;
        private int _lastSpawnedCount = -1;

        /// <summary>How many altars this binder has attached since the last re-arm.</summary>
        public int BoundCount => _boundCount;

        /// <summary>
        /// The altar templates being looked for. Read from the tuning rather than stored, so the
        /// Death Editor changing the list is visible here without a second copy that can drift —
        /// which is exactly what the two hard-coded 249s were.
        /// </summary>
        public int[] TargetTemplateIds => DeathTuning.Active.altarTemplateIds;

        /// <summary>
        /// True once the search window has closed with nothing found. The one fact the rest of the
        /// death flow needs, and the one the shipped build had no way to ask for.
        /// </summary>
        public bool SearchExhaustedWithNoAltar { get; private set; }

        /// <summary>True while the binder is still scanning for new buildings.</summary>
        public bool IsSearching => _searching;

        /// <summary>
        /// Start looking again — after a world swap, or after the Death Editor edits the template
        /// list. Clears the exhausted flag, because "we found nothing" is a statement about a
        /// finished search and there is a new one now.
        /// </summary>
        public void Rearm()
        {
            _searching = true;
            _elapsed = 0f;
            _scanTimer = 0f;
            _boundCount = 0;
            SearchExhaustedWithNoAltar = false;
        }

        private void Update()
        {
            _scanTimer += Time.unscaledDeltaTime;
            if (_scanTimer < scanInterval) return;
            _scanTimer = 0f;

            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null) return;

            // A world swap destroys every building and rebuilds them. The count FALLING is the
            // signal, and it needs nothing from the transition layer: an editor deleting one
            // building also trips it, which costs one extra scan pass and is harmless.
            int spawned = loader.SpawnedBuildings.Count;
            if (_lastSpawnedCount >= 0 && spawned < _lastSpawnedCount) Rearm();
            _lastSpawnedCount = spawned;

            if (!_searching) return;

            _boundCount += ResurrectionAltarRegistry.BindAll(loader);

            _elapsed += scanInterval;
            float timeout = Mathf.Max(1f, DeathTuning.Active.altarSearchTimeout);
            if (_elapsed < timeout) return;

            _searching = false;
            if (ResurrectionAltarRegistry.Count == 0)
            {
                SearchExhaustedWithNoAltar = true;
                Debug.LogWarning(
                    $"[ResurrectionZoneAutoBinder] No altar building found within {timeout:0.0}s " +
                    $"(templates: {FormatTemplates()}). The rescue in DeathSequenceController is " +
                    "now the only way back from a death — open ESC > Muerte > Altares to place one.");
            }
        }

        private static string FormatTemplates()
        {
            var ids = DeathTuning.Active.altarTemplateIds;
            if (ids == null || ids.Length == 0) return "(ninguna)";
            return string.Join(", ", ids);
        }
    }
}
