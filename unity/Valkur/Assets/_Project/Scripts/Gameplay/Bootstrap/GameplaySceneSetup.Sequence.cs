using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Boot;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The gameplay boot sequence, as a list.
    ///
    /// This replaced a 250-line coroutine of <c>EnsureX(); Report("…");
    /// yield return null;</c> triplets, and the shape change is what fixes four
    /// separate defects at once rather than four patches:
    ///
    /// <list type="bullet">
    ///   <item>the step total is <c>list.Count</c>, so <c>SetupStepTotal = 53</c>
    ///   — which had drifted against a real 70 and pinned the bar at 100 % for the
    ///   last quarter of the boot — has no place to exist any more;</item>
    ///   <item>the runner owns one exception boundary, so all seventy steps are
    ///   guarded instead of five;</item>
    ///   <item>ORDER is a value a test can walk, and this sequence carries at least
    ///   six documented, load-bearing ordering constraints that previously lived
    ///   only in comments;</item>
    ///   <item>the seventeen authoring editors are one <c>if</c> away from a release
    ///   build instead of being welded into it.</item>
    /// </list>
    ///
    /// <b>Weights are first-boot estimates only.</b> <see cref="BootTimeline"/>
    /// replaces each one with the previous boot's measured milliseconds, so getting
    /// one wrong here costs exactly one uncalibrated launch on a fresh install.
    /// </summary>
    public partial class GameplaySceneSetup
    {
        /// <summary>
        /// Builds the ordered sequence. Pure composition — nothing here runs; the
        /// runner in <c>Start</c> executes it. That separation is what lets an
        /// EditMode test assert the order without booting a world.
        /// </summary>
        private List<BootStep> BuildBootSequence()
        {
            bool editors = RuntimeEditorPolicy.AuthoringEditorsAvailable;
            var s = new List<BootStep>(80);

            // ── The two installs that must precede every editor ──────────────
            // Every runtime editor's OnEnable does `if (GameEditorManager.HasInstance)
            // …Register(this)` with NO retry — a handful self-heal via EnsureInstance(),
            // most do not, and a skipped registration is permanent and silent: that
            // editor never joins the exclusivity group, so opening it leaves whatever
            // else is open stacked underneath, both canvases eating clicks. The old
            // code was protected only by the accident that EnsureTileEditor happened
            // to run first. Making it step 0 turns that accident into a contract.
            s.Add(BootStep.Silent(() => GameEditorManager.EnsureInstance()));

            // Immediately after, and before any panel: the workspace service installs
            // itself as DraggablePanel's state sink, and a panel built before that
            // install answers its "was I left closed?" question from the old
            // PlayerPrefs backend and is then recorded there too — two owners of one
            // bit, which is the failure this layer exists to remove.
            s.Add(BootStep.Silent(() =>
                Valkur.Gameplay.Editors.Workspace.EditorWorkspaceService.EnsureInstance()));

            // ── World ────────────────────────────────────────────────────────
            s.Add(BootStep.Of("Construyendo la rejilla del mundo", BuildWorldGrid, 3f));
            s.Add(BootStep.Of("Inicializando las zonas", EnsureZoneManager, 1f));
            s.Add(BootStep.Of("Inicializando el gestor de mundos", EnsureWorldManager, 1f));

            // TileEditorManager MUST exist before the world load runs its "Aplicando
            // ajustes de tiles" stage: that stage funnels collision-tag JSON into
            // TileEditorManager.Instance.CollisionTags, and with the singleton absent
            // the sink is null and every tag is silently dropped. The M2 baker then
            // reads every cell as Wildcard and stamps them into the WorldAll
            // sub-tilemap, blocking the player on every visual layer regardless of the
            // painted tag. It is therefore NOT gated with the authoring editors: it is
            // a runtime component that happens to also have a UI.
            s.Add(BootStep.Of("Inicializando el editor de tiles", EnsureTileEditor, 2f));

            s.Add(BootStep.Coroutine("Cargando el mundo", LoadWorldProgressively, 260f, subStages: 5));

            // Installed unconditionally: even on the legacy single-overlay branch (no
            // GenerateDungeon call) the Map editor's slot loads still drive the regen.
            s.Add(BootStep.Of("Instalando el vigilante de mazmorras", EnsureDungeonSlotBootstrap, 1f, barrier: false));
            s.Add(BootStep.Of("Preparando los mundos en vivo", EnsureSeedWorldLiveStreamer, 1f, barrier: false));
            s.Add(BootStep.Of("Horneando las colisiones del terreno", RebakeTilemapColliders, 60f));

            // ── Light, weather, effects ──────────────────────────────────────
            s.Add(BootStep.Of("Inicializando la luz global", EnsureGlobalLight2D, 1f, barrier: false));
            s.Add(BootStep.Of("Arrancando el ciclo de dia y noche", EnsureDayNightCycle, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando los efectos visuales", () =>
            {
                EnsureVFXManager();
                EnsureCameraFeelDirector();
            }, 2f));
            s.Add(BootStep.Of("Cargando las particulas del mundo", EnsureParticleInstancesLoader, 25f));

            // Owns ClearAllSpawnedWorldContent / ReloadAllWorldContent, which every
            // zone portal and every building door goes through. Runtime, not
            // authoring — never gated.
            s.Add(BootStep.Of("Inicializando el editor de mapas", EnsureMapEditor, 2f));

            // ── Save, navigation, services ───────────────────────────────────
            // Before the save service and before the chat: the restore path asks this
            // for the catalogue, and the service listens for conversations.
            s.Add(BootStep.Of("Inicializando las misiones", EnsureQuestService, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando el sistema de guardado", EnsureSaveService, 3f));
            s.Add(BootStep.Of("Inicializando el guardado rapido", EnsureSaveLoadInput, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando la separacion de NPCs", EnsureNPCSeparation, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando el pathfinding", EnsurePathFinder, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando las tiendas", EnsureVendorShopUI, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando la economia", EnsureVendorEconomyService, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando las conversaciones", EnsureChatSystem, 3f));
            s.Add(BootStep.Of("Cargando las luces del mundo", EnsureWorldLightLoader, 20f));
            s.Add(BootStep.Of("Cargando las colisiones de los edificios", EnsureBuildingCollisionLoader, 15f));

            // ── Runtime wiring the authoring editors used to smuggle in ──────
            // Each of these was a side effect inside an Ensure*Editor method. Gating
            // the editors without lifting them out would have taken the item catalog,
            // the drop pipeline and two picker catalogs down with the toolbox, in a
            // build nobody runs in the Editor — the silent kind of regression this
            // whole subsystem specialises in.
            s.Add(BootStep.Of("Registrando el catalogo de objetos", () =>
            {
                RegisterItemCatalogForRuntime();
                EnsureItemDropService();
            }, 4f));
            s.Add(BootStep.Silent(RegisterSpawnerTemplateCatalogFallback));
            s.Add(BootStep.Silent(RegisterMonsterCatalogFallback));

            // ── The seventeen authoring editors ──────────────────────────────
            // Roughly a third of the old sequence. `barrier: false` throughout: each
            // is a self-contained manager on its own GameObject that registers itself
            // in OnEnable (which AddComponent runs synchronously), and none of them
            // reads another one, so none needs a frame to have passed. They collapse
            // to a handful of frames on the runner's time budget.
            if (editors)
            {
                s.Add(BootStep.Of("Preparando el editor de generadores", EnsureSpawnerEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de edificios", EnsureBuildingsRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de FSM", EnsureFSMRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de objetos", EnsureItemsRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de hechizos", EnsureSpellsRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de entidades", EnsureEntitiesRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de jefes", EnsureBossEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de inventario", EnsureInventoryRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de particulas", EnsureParticlesRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor de iluminacion", EnsureLightingRuntimeEditor, 1f, barrier: false));
                s.Add(BootStep.Of("Preparando el editor general", EnsureGeneralEditor, 1f, barrier: false));
            }

            // ── Atmosphere and weather ───────────────────────────────────────
            s.Add(BootStep.Of("Inicializando la atmosfera", EnsureDayNightAtmosphere, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando el clima", EnsureWeatherManager, 3f));
            // The sky reads the weather it was created after, and every building and entity
            // spawned later reads the sun the sky evaluates — so it sits exactly here.
            s.Add(BootStep.Of("Levantando el cielo", EnsureSkyLayer, 1f, barrier: false));

            if (editors)
            {
                s.Add(BootStep.Of("Preparando el editor de tiempo y clima", () =>
                {
                    EnsureTimeWeatherEditor();
                    EnsureCameraEditor();
                    EnsureControlsEditor();
                    EnsureSkillsEditor();
                    EnsureEconomyEditor();
                    EnsureDeathEditor();
                    EnsureQuestsEditor();
                    EnsureSeedWorldEditor();
                    EnsureSelectionEditor();
                }, 5f, barrier: false));
            }

            // NOT an editor: the reader that makes the Pause binding do anything.
            // It sat inside the editor block and would have been gated out with it.
            s.Add(BootStep.Silent(EnsurePauseHotkeyReader));

            s.Add(BootStep.Of("Inicializando la consola", EnsureDevConsole, 2f, barrier: false));

            // ── Death, progression, feedback ─────────────────────────────────
            s.Add(BootStep.Of("Inicializando el botin de muerte", EnsureDeathDropSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando el ciclo de muerte y resurreccion", EnsureDeathSequenceFlow, 2f, barrier: false));
            s.Add(BootStep.Of("Inicializando la restauracion al subir de nivel", EnsureLevelUpRestoreSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando la muerte permanente", EnsurePermadeathSaveCleanupSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando el aviso de experiencia", EnsureXpFeedbackSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando la penalizacion por morir", EnsureXpLossOnDeathSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando la telemetria de progreso", EnsureProfileTelemetrySystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando el respawn de NPCs", EnsureNPCRespawnSystem, 1f, barrier: false));
            s.Add(BootStep.Of("Inicializando los avisos", EnsureToastSystem, 1f, barrier: false));

            // ── The player ───────────────────────────────────────────────────
            // Pre-apply the saved class BEFORE the spawn, so visuals and stats are
            // built for the right character. The full restore (position, HP) lands
            // later through SaveService.Load.
            s.Add(BootStep.Silent(() =>
            {
                if (Save.PendingSaveLoad.HasPending &&
                    !string.IsNullOrWhiteSpace(Save.PendingSaveLoad.PlayerClass))
                    PlayerSelectionState.SetSelectedPlayer(Save.PendingSaveLoad.PlayerClass);
            }));

            s.Add(BootStep.Coroutine("Creando el personaje", SpawnPlayerProgressively, 200f, subStages: 7));

            s.Add(BootStep.Of("Inicializando el streaming procedural", EnsureProceduralChunkStreamer, 2f, barrier: false));
            s.Add(BootStep.Of("Poblando el mundo", SpawnTestMonsters, 5f));
            s.Add(BootStep.Of("Inicializando los generadores de monstruos", EnsureMonsterSpawner, 3f));

            // Before the building loader, never after: every building asks the
            // ServiceLocator for this as it spawns, so a service registered later is
            // found by nothing and every felled tree quietly comes back whole.
            s.Add(BootStep.Of("Restaurando los danos del mundo", EnsureWorldDamageService, 3f));

            s.Add(BootStep.Coroutine("Levantando los edificios", EnsureBuildingLoaderProgressively, 150f, subStages: 3));
            s.Add(BootStep.Of("Cargando los generadores colocados", EnsureSpawnerInstanceLoader, 5f));
            // After the MonsterSpawner (so placements are tracked like any monster) and the
            // zones, and OUTSIDE the editor block: this used to be loaded by the Entities
            // editor, which a release build does not create, so no hand-placed monster ever
            // stood in a shipped game. Before RestoreSessionState, which takes down the ones
            // the loaded save says are already dead.
            s.Add(BootStep.Of("Colocando las entidades del mapa", EnsurePlacedEntityService, 5f));

            // ── Audio ────────────────────────────────────────────────────────
            s.Add(BootStep.Of("Inicializando el audio", EnsureAudioManager, 20f));
            s.Add(BootStep.Of("Inicializando el audio de combate", EnsureCombatAudioSystem, 3f, barrier: false));
            s.Add(BootStep.Of("Arrancando la musica", EnterGameAudio, 3f, barrier: false));

            // ── Shaders ──────────────────────────────────────────────────────
            // The one thing a loading screen exists for that this one never did.
            // Valkur ships custom shaders (ValkurSnow, SpriteHDRTintLit,
            // SpriteAdditive, the ScreenGrade blit) whose variants compile on FIRST
            // USE — the first spell, the first snowfall, the first nightfall — each
            // one a hitch in play rather than a millisecond behind a progress bar.
            // Skipped in the Editor, where shaders are compiled on demand anyway and
            // warming them all costs far more than it saves.
            s.Add(BootStep.Of("Precalentando los shaders", WarmupShaders, 40f));

            // ── Session restore ──────────────────────────────────────────────
            s.Add(BootStep.Of("Restaurando la partida", RestoreSessionState, 25f));

            return s;
        }

        /// <summary>
        /// Compile every shader variant the build ships, so the first cast does not
        /// pay for its own material. A no-op outside a player build.
        /// </summary>
        private void WarmupShaders()
        {
            if (Application.isEditor) return;
            Shader.WarmupAllShaders();
        }

        /// <summary>
        /// The tail of the old <c>Start</c>: apply the pending save (or mint a fresh
        /// run), settle the telemetry identity against whatever run id that produced,
        /// and drop the crash-safe position checkpoint.
        ///
        /// Telemetry is started HERE rather than inside EnsureProfileTelemetrySystem
        /// because SaveService.RunId is only settled once the load or the
        /// BeginNewRun above has run; wiring it earlier is what used to mint a
        /// phantom RunRecord on every boot whether or not the player was resuming.
        /// </summary>
        private void RestoreSessionState()
        {
            if (Save.PendingSaveLoad.HasPending)
            {
                string savePath = Save.PendingSaveLoad.Consume();
                if (SaveService.Instance != null)
                {
                    SaveService.Instance.Load(savePath);
                    ApplyPositionCheckpointIfNewer(SaveService.Instance.LastLoadedTimestamp);
                    Debug.Log($"[GameplaySceneSetup] Loaded pending save: {savePath}");
                }
            }
            else
            {
                SaveService.Instance?.BeginNewRun();
                ApplyPositionCheckpointIfNewer(null);
            }

            StartTelemetryRunForCurrentSession();
            Save.SaveFileManager.DeletePositionCheckpoint();
        }

        /// <summary>
        /// Report a sub-stage from inside one of the three progressive steps. The
        /// label changes and the bar walks the parent step's own share of the total,
        /// so a five-second world load is five moving stages rather than one frozen
        /// one. Sub-stages never advance past their parent's share.
        /// </summary>
        private void ReportSubStage(string message)
        {
            Valkur.Core.LoadingReporter.ReportStage(message, BootTimeline.NextSubStageFraction(message));
        }

        /// <summary>
        /// Pumps a progressive step, converting any exception thrown during MoveNext
        /// into one recorded failure so the rest of the boot keeps running.
        /// <c>yield return</c> cannot live inside a <c>try/catch</c> in C#, so this
        /// helper inverts the structure to give the same safety the synchronous
        /// steps get from the runner.
        /// </summary>
        private IEnumerator RunGuarded(IEnumerator iter, string label)
        {
            if (iter == null) yield break;
            while (true)
            {
                bool hasMore;
                try
                {
                    hasMore = iter.MoveNext();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GameplaySceneSetup] '{label}' fallo: {ex.Message}\n{ex.StackTrace}");
                    BootTimeline.RecordFailure(label, ex);
                    yield break;
                }
                if (!hasMore) yield break;
                yield return iter.Current;
            }
        }
    }
}
