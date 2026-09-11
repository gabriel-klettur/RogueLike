using System.Collections;
using UnityEngine;
using Valkur.Data;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        // Progressive player spawn — each step yields a frame so the loading
        // screen repaints and reports a sub-stage label. The previous
        // synchronous SpawnPlayer() did all of this in one ~8 s blocking call,
        // which surfaced as a frozen "Spawning player" stage with no progress
        // feedback. The work itself is identical; only the scheduling changed.
        private IEnumerator SpawnPlayerProgressively()
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player prefab assigned.");
                yield break;
            }

            if (_spellCatalog != null)
                EntitySetup.SetSpellCatalog(_spellCatalog);

            // Spawn at Lobby center by default. With full world, Lobby offset
            // is (50,50) + center (25,25) = (75,75); with single overlay
            // (Lobby at 0,0) the centre is (25,25).
            Vector3 spawnPos = new Vector3(25f, 25f, 0f);
            var zm = FindObjectOfType<World.ZoneManager>();
            if (zm != null && zm.TryGetZone("Lobby", out var lobbyDef))
                spawnPos = new Vector3(lobbyDef.gridOffset.x + 25f, lobbyDef.gridOffset.y + 25f, 0f);

            // If a position checkpoint exists from a previous session (last
            // autosave / quit-time write), respawn there instead. New Game
            // already deletes the checkpoint via SaveFileManager.DeletePositionCheckpoint
            // in MainMenuUI.StartNewGame, so this branch only triggers for
            // Continue / hot-reload scenarios.
            //
            // SaveService.Load also calls ApplyPositionCheckpointIfNewer
            // later in the bootstrap, but that fires AFTER the player
            // instantiates — meaning Cinemachine and the camera setup briefly
            // see the lobby-centre pose. Reading the checkpoint now skips the
            // visible "snap from lobby" frame.
            var checkpoint = Valkur.Gameplay.Save.SaveFileManager.ReadPositionCheckpoint();
            if (checkpoint != null && !string.IsNullOrEmpty(checkpoint.timestamp))
            {
                // A checkpoint written inside an interior names a grid this world does not
                // have, and spawning on its coordinates puts the player off the map. Falling
                // back to the lobby is visibly wrong for one boot; landing in the void is a
                // save the player cannot recover from without a console.
                if (IsSpawnableCheckpoint(zm, checkpoint.zone, out string refusal))
                {
                    spawnPos = new Vector3(checkpoint.x, checkpoint.y, 0f);
                    Debug.Log($"[GameplaySceneSetup] Restoring player position from checkpoint: " +
                              $"({checkpoint.x:F1}, {checkpoint.y:F1}) zone='{checkpoint.zone}'.");
                }
                else
                {
                    Debug.LogWarning($"[GameplaySceneSetup] Ignoring the position checkpoint " +
                                     $"({checkpoint.x:F1}, {checkpoint.y:F1}): {refusal} " +
                                     $"Spawning at {spawnPos.x:F0},{spawnPos.y:F0} instead.");
                }
            }

            // ── 1. Resolve player class (Resources.LoadAll scan) ────────────
            ReportSubStage("Resolviendo la clase"); yield return null;
            var resolvedDef = ResolveSelectedPlayerDefinition() ?? defaultPlayerDef;
            if (resolvedDef == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player definition available for spawned player.");
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(resolvedDef.playerKey))
                PlayerSelectionState.SetSelectedPlayer(resolvedDef.playerKey);

            // ── 2. Instantiate prefab ───────────────────────────────────────
            ReportSubStage("Creando la entidad"); yield return null;
            var playerGo = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            playerGo.tag = "Player";
            playerGo.transform.SetParent(GetSceneContainer("[Entities]"), true);

            // ── 3. Animation rebind (heaviest single chunk: 7 directional sets) ──
            ReportSubStage("Montando las animaciones"); yield return null;
            EntitySetup.ConfigurePlayerVisuals(playerGo, resolvedDef);

            // ── 4. Health / movement / combat / dash ────────────────────────
            ReportSubStage("Conectando el combate"); yield return null;
            EntitySetup.ConfigurePlayerCombat(playerGo, resolvedDef);

            // ── 5. Spell catalog scan + per-spell registration ──────────────
            ReportSubStage("Cargando el grimorio"); yield return null;
            EntitySetup.ConfigurePlayerSpells(playerGo);

            // ── 6. Mana, XP, inventory, currency, death flow, class marker ──
            ReportSubStage("Calculando las estadisticas"); yield return null;
            EntitySetup.ConfigurePlayerStats(playerGo, resolvedDef);

            // ── 7. HUD singletons (inventory, spell bar, icons, range) ──────
            ReportSubStage("Construyendo la interfaz"); yield return null;
            EntitySetup.ConfigurePlayerHUD();

            Debug.Log($"[GameplaySceneSetup] Player ready: key={resolvedDef.playerKey}, " +
                      $"HP={resolvedDef.maxStrength}, MP={resolvedDef.maxIntelligence}, " +
                      $"ATK={resolvedDef.basicAttack}, SPD={resolvedDef.basicSpeed}");
        }

        private PlayerDefinition ResolveSelectedPlayerDefinition()
        {
            if (!PlayerSelectionState.HasExplicitSelection)
                return defaultPlayerDef;

            string selectedKey = PlayerSelectionState.SelectedPlayerKey;

            var selectedAssetDef = TryResolveCatalogDefinition(selectedKey);
            if (selectedAssetDef != null)
                return selectedAssetDef;

            var selectedRuntimeDef = PlayerClassCatalog.CreateRuntimeDefinition(selectedKey);
            if (selectedRuntimeDef == null)
            {
                Debug.LogWarning($"[GameplaySceneSetup] Selected player class '{selectedKey}' not found in runtime catalog.");
                return defaultPlayerDef;
            }

            return selectedRuntimeDef;
        }

        /// <summary>
        /// Optional drop-in folder for PlayerDefinitions shipped inside Resources.
        /// The authored definitions live in Data/Catalogs/Players and are resolved
        /// through the AssetDatabase branch below in the Editor; this folder exists
        /// so a built player can still override them without a code change.
        /// </summary>
        private const string PlayerDefinitionResourceFolder = "Players";

        private PlayerDefinition TryResolveCatalogDefinition(string selectedKey)
        {
            if (string.IsNullOrWhiteSpace(selectedKey))
                return null;

            if (defaultPlayerDef != null &&
                string.Equals(defaultPlayerDef.playerKey, selectedKey, System.StringComparison.OrdinalIgnoreCase))
            {
                return defaultPlayerDef;
            }

            // Scoped to Resources/Players, never the whole tree: LoadAll with an empty
            // path deserializes every one of the ~7 400 assets under Resources/ just to
            // filter for one type, and any asset in there whose script no longer
            // resolves logs a "referenced script (Unknown)" error on each call.
            var resourceDefs = Resources.LoadAll<PlayerDefinition>(PlayerDefinitionResourceFolder);
            for (int i = 0; i < resourceDefs.Length; i++)
            {
                var def = resourceDefs[i];
                if (def != null && string.Equals(def.playerKey, selectedKey, System.StringComparison.OrdinalIgnoreCase))
                    return def;
            }

#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets("t:PlayerDefinition", new[] { "Assets/_Project/Data/Catalogs/Players" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(path);
                if (def != null && string.Equals(def.playerKey, selectedKey, System.StringComparison.OrdinalIgnoreCase))
                    return def;
            }
#endif

            return null;
        }

        private void SpawnTestMonsters()
        {
            if (monsterPrefab == null || testMonsterDef == null) return;

            var entitiesContainer = GetSceneContainer("[Entities]");
            for (int i = 0; i < testMonsterCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnRadius;
                Vector3 pos = new Vector3(offset.x, offset.y, 0f);
                var monsterGo = Instantiate(monsterPrefab, pos, Quaternion.identity);
                monsterGo.transform.SetParent(entitiesContainer, true);
                EntitySetup.ConfigureMonster(monsterGo, testMonsterDef);
            }
        }
    }
}
