using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        private void EnsureDeathDropSystem()
        {
            if (FindObjectOfType<DeathDropSystem>() != null) return;
            var go = new GameObject("DeathDropSystem");
            go.AddComponent<DeathDropSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] DeathDropSystem created.");
        }

        // Death-sequence orchestrator + URP grayscale volume + altar binder.
        // All three must coexist for the spirit/altar revive flow to work, so
        // they're wired in a single Ensure method.
        private void EnsureDeathSequenceFlow()
        {
            if (FindObjectOfType<DeathSequenceController>() == null)
            {
                var grayscaleGo = new GameObject("DeathGrayscaleVolume");
                grayscaleGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                var grayscale = grayscaleGo.AddComponent<GrayscaleVolumeController>();

                var seqGo = new GameObject("DeathSequenceController");
                seqGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                var controller = seqGo.AddComponent<DeathSequenceController>();
                controller.BindGrayscaleController(grayscale);

                Debug.Log("[GameplaySceneSetup] DeathSequenceController + GrayscaleVolumeController created.");
            }

            if (FindObjectOfType<ResurrectionZoneAutoBinder>() == null)
            {
                var binderGo = new GameObject("ResurrectionZoneAutoBinder");
                binderGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                binderGo.AddComponent<ResurrectionZoneAutoBinder>();
                Debug.Log("[GameplaySceneSetup] ResurrectionZoneAutoBinder created.");
            }

            if (FindObjectOfType<SpiritAltarPathHighlighter>() == null)
            {
                var pathGo = new GameObject("SpiritAltarPathHighlighter");
                pathGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                pathGo.AddComponent<SpiritAltarPathHighlighter>();
                Debug.Log("[GameplaySceneSetup] SpiritAltarPathHighlighter created.");
            }

            if (FindObjectOfType<SpiritWorldGrayscale>() == null)
            {
                var grayGo = new GameObject("SpiritWorldGrayscale");
                grayGo.transform.SetParent(GetSceneContainer("[Systems]"), false);
                grayGo.AddComponent<SpiritWorldGrayscale>();
                Debug.Log("[GameplaySceneSetup] SpiritWorldGrayscale created.");
            }
        }

        // LevelUpRestoreSystem listens to GameEvents.OnLevelUp and refills
        // HP/MP. Idempotent: bails if a designer already wired one in the
        // scene. Created in [Systems] alongside DeathDropSystem so the
        // gameplay-loop helpers cluster in one place.
        private void EnsureLevelUpRestoreSystem()
        {
            if (FindObjectOfType<LevelUpRestoreSystem>() != null) return;
            var go = new GameObject("LevelUpRestoreSystem");
            go.AddComponent<LevelUpRestoreSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] LevelUpRestoreSystem created.");
        }

        // PermadeathSaveCleanupSystem deletes the active autosave when the
        // player dies AND GameSettings.permadeath is on. The component
        // itself reads the flag each death — adding it here is harmless
        // when permadeath is off (it just listens and skips).
        private void EnsurePermadeathSaveCleanupSystem()
        {
            if (FindObjectOfType<PermadeathSaveCleanupSystem>() != null) return;
            var go = new GameObject("PermadeathSaveCleanupSystem");
            go.AddComponent<PermadeathSaveCleanupSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] PermadeathSaveCleanupSystem created.");
        }

        // LevelUpSkillPointSystem grants skill points to the levelled
        // entity's LearnedSkills on each level-up. Sibling to
        // LevelUpRestoreSystem; both can safely coexist on the same event.
        // Skipped silently for NPCs without a LearnedSkills component.
        private void EnsureLevelUpSkillPointSystem()
        {
            if (FindObjectOfType<LevelUpSkillPointSystem>() != null) return;
            var go = new GameObject("LevelUpSkillPointSystem");
            go.AddComponent<LevelUpSkillPointSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] LevelUpSkillPointSystem created.");
        }

        // XpFeedbackSystem closes the visual juice loop: floating "+N XP"
        // above the player and "LEVEL UP!" toast on level-up. Audio is
        // already covered by CombatAudioSystem.OnLevelUp, so this only
        // adds the visual layer. Idempotent.
        private void EnsureXpFeedbackSystem()
        {
            if (FindObjectOfType<XpFeedbackSystem>() != null) return;
            var go = new GameObject("XpFeedbackSystem");
            go.AddComponent<XpFeedbackSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] XpFeedbackSystem created.");
        }

        // LevelUpStatScalingSystem permanently grows MaxHp/MaxMana on each
        // level-up via a LevelStatCurve. Spawned without a curve assigned
        // = silent no-op, so adding the bootstrap call is safe even before
        // designers wire the SO. Idempotent.
        private void EnsureLevelUpStatScalingSystem()
        {
            if (FindObjectOfType<LevelUpStatScalingSystem>() != null) return;
            var go = new GameObject("LevelUpStatScalingSystem");
            go.AddComponent<LevelUpStatScalingSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] LevelUpStatScalingSystem created (no curve assigned — system idle until set).");
        }

        // XpLossOnDeathSystem applies an XP penalty when the player revives
        // post-spirit. Uses the default 10% / no-delevel policy on
        // creation; designers can adjust on the spawned component or via
        // the runtime tuning HUDs. Idempotent.
        private void EnsureXpLossOnDeathSystem()
        {
            if (FindObjectOfType<XpLossOnDeathSystem>() != null) return;
            var go = new GameObject("XpLossOnDeathSystem");
            go.AddComponent<XpLossOnDeathSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] XpLossOnDeathSystem created (default 10% in-level penalty, no de-level).");
        }

        // Boots the meta-progression telemetry layer: creates a
        // JsonProfileDb at persistentDataPath/profile.json, registers
        // it in ServiceLocator, hydrates from disk, and starts the
        // first run row. Subsequent OnEntityDied / OnPlayerDied /
        // OnLevelUp / OnXpGained events flow into the DB through
        // ProfileTelemetrySystem.
        private void EnsureProfileTelemetrySystem()
        {
            if (FindObjectOfType<Save.ProfileTelemetrySystem>() != null) return;

            // Resolve or create the IProfileDb singleton.
            if (!ServiceLocator.TryGet<Valkur.Infrastructure.Persistence.Profile.IProfileDb>(out var db))
            {
                var json = new Valkur.Infrastructure.Persistence.Profile.JsonProfileDb();
                json.LoadAll();
                ServiceLocator.Register<Valkur.Infrastructure.Persistence.Profile.IProfileDb>(json);
                db = json;
                Debug.Log($"[GameplaySceneSetup] ProfileDb hydrated from {json.FilePath}.");
            }

            var go = new GameObject("ProfileTelemetrySystem");
            var sys = go.AddComponent<Save.ProfileTelemetrySystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            sys.BindDb(db);
            // StartRun is intentionally NOT called here. The bootstrap fires it
            // later via StartTelemetryRunForCurrentSession() — once we know
            // whether the user is loading an existing save (resume the saved
            // runId+ordinal) or starting a new game (mint fresh ones). Calling
            // StartRun unconditionally at bind time used to spawn a phantom
            // RunRecord on every scene load, including when the user was just
            // loading an existing save.
            Debug.Log("[GameplaySceneSetup] ProfileTelemetrySystem created (run start deferred).");
        }

        /// <summary>
        /// Kicks off the active run on the telemetry side once SaveService
        /// has settled on the canonical runId — either by adopting one from
        /// a loaded save or by minting a fresh GUID via BeginNewRun. Reuses
        /// the loaded ordinal when present so resumed sessions keep their
        /// "Run #N" identity; otherwise lets ProfileTelemetrySystem mint
        /// the next one off the per-profile counter and propagates it back
        /// to SaveService so subsequent saves embed it in meta.
        /// </summary>
        private void StartTelemetryRunForCurrentSession()
        {
            var sys = FindObjectOfType<Save.ProfileTelemetrySystem>();
            if (sys == null) return;
            string runId   = SaveService.Instance != null ? SaveService.Instance.RunId     : null;
            int    ordinal = SaveService.Instance != null ? SaveService.Instance.RunOrdinal : 0;
            sys.StartRun(
                permadeath:   GameSettings.Instance.permadeath,
                reuseRunId:   runId,
                reuseOrdinal: ordinal);
            // Propagate the freshly-minted ordinal back to SaveService so
            // every save written from here on includes meta.run_ordinal.
            // (When ordinal was reused, SetRunOrdinal is a no-op.)
            if (SaveService.Instance != null && sys.ActiveRunOrdinal > 0)
                SaveService.Instance.SetRunOrdinal(sys.ActiveRunOrdinal);
            Debug.Log($"[GameplaySceneSetup] Telemetry run started: " +
                      $"runId={runId} ordinal=#{sys.ActiveRunOrdinal}");
        }

        private void EnsureNPCRespawnSystem()
        {
            var existing = FindObjectOfType<NPCRespawnSystem>();
            if (existing != null)
            {
                if (monsterPrefab != null) existing.SetMonsterPrefab(monsterPrefab);
                return;
            }
            var go = new GameObject("NPCRespawnSystem");
            var sys = go.AddComponent<NPCRespawnSystem>();
            if (monsterPrefab != null) sys.SetMonsterPrefab(monsterPrefab);
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] NPCRespawnSystem created.");
        }

        private void EnsureToastSystem()
        {
            if (FindObjectOfType<Combat.ToastSystem>() != null) return;
            var go = new GameObject("ToastSystem");
            go.AddComponent<Combat.ToastSystem>();
            go.transform.SetParent(GetSceneContainer("[UI]"), false);
            Debug.Log("[GameplaySceneSetup] ToastSystem created.");
        }
    }
}
