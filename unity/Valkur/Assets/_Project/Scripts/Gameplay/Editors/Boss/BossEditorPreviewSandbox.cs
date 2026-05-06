using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Enemies;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Manages the transient boss instance used by the Boss Editor Live Preview.
    ///
    /// Instantiated via <see cref="BossEditorManager.LivePreview"/> when the
    /// user clicks "Live preview". Despawns the boss when <see cref="Teardown"/>
    /// is called (toggle off, editor close, or boss selection change).
    ///
    /// The sandbox creates a minimal boss at runtime by adding the required
    /// components in order rather than relying on a prefab, because boss
    /// prefabs carry colliders / AI graphs that would interact with live gameplay.
    /// Only the choreography pipeline components are required for the preview.
    ///
    /// Does NOT touch <see cref="BossCueDispatcher"/> (left untouched per spec).
    /// The dispatcher fires spells through <see cref="SpellCaster.TryCastByKey"/>
    /// automatically once it is wired.
    /// </summary>
    public sealed class BossEditorPreviewSandbox : MonoBehaviour
    {
        // ── State ──────────────────────────────────────────────────────────────

        private GameObject _previewBoss;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns (or respawns) a preview boss for <paramref name="def"/> 4 world
        /// units in front of the player. If no player is found the boss is placed
        /// at the world origin.
        /// </summary>
        public void Spawn(BossDefinition def, SpellCatalog spellCatalog)
        {
            Teardown();
            if (def == null) return;

            Vector3 spawnPos = ResolveSpawnPosition();

            _previewBoss = new GameObject($"[PreviewBoss] {def.name}");
            _previewBoss.transform.position = spawnPos;

            // Minimal component set for choreography preview.
            // Health is required by BossPhaseController.
            var health = _previewBoss.AddComponent<Health>();
            health.Initialize(1000);

            // Add components in dependency order so each Awake() finds its siblings.
            _previewBoss.AddComponent<SpellCaster>();
            _previewBoss.AddComponent<BossPhaseController>();
            _previewBoss.AddComponent<NPCAutoCast>();
            _previewBoss.AddComponent<BossBeatChoreographer>();
            // Dispatcher added before Configurator so Configurator.Awake finds it.
            _previewBoss.AddComponent<BossCueDispatcher>();
            var configurator = _previewBoss.AddComponent<BossConfigurator>();

            // Wire the definition so the configurator can populate phases/charts.
            configurator.SetDefinition(def, spellCatalog);
            configurator.ConfigurePhasesFromDefinition();

            // Prime phase 0 chart binding (mirrors BossConfigurator.Start behaviour
            // but Start hasn't run yet because we're still in this frame).
            if (def.phases != null && def.phases.Length > 0)
                configurator.ConfigureChart(def.phases[0]);

            Debug.Log($"[BossEditorPreview] Spawned preview boss '{def.name}' at {spawnPos}.");
        }

        /// <summary>Destroys the preview boss immediately.</summary>
        /// <remarks>
        /// Uses <see cref="Object.DestroyImmediate"/> so the GameObject is gone
        /// within the same frame — required because this sandbox runs inside the
        /// Editor (not at runtime) and deferred <c>Destroy</c> would leave a stale
        /// GO until the next frame's destruction pass.
        /// </remarks>
        public void Teardown()
        {
            if (_previewBoss != null)
            {
                DestroyImmediate(_previewBoss);
                _previewBoss = null;
                Debug.Log("[BossEditorPreview] Preview boss destroyed.");
            }
        }

        /// <summary>True while a preview boss is alive.</summary>
        public bool IsActive => _previewBoss != null;

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Vector3 ResolveSpawnPosition()
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) return Vector3.zero;

            // 4 world units in the player's facing direction; fall back to +Y.
            float angle = 0f;
            var anim = player.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                float h = anim.GetFloat("Horizontal");
                float v = anim.GetFloat("Vertical");
                if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
                    angle = Mathf.Atan2(v, h) * Mathf.Rad2Deg;
            }
            Vector3 dir = Quaternion.Euler(0, 0, angle) * Vector3.right;
            return player.transform.position + dir * 4.5f;
        }

        private void OnDestroy()
        {
            // Safety: clean up if the editor GameObject is destroyed directly.
            Teardown();
        }
    }
}
