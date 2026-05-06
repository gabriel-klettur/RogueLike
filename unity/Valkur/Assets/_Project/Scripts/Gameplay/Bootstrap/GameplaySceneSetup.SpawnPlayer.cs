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

            // Spawn at Lobby center. With full world, Lobby offset is (50,50) + center (25,25) = (75,75).
            // With single overlay (Lobby at 0,0), center is (25,25).
            Vector3 spawnPos = new Vector3(25f, 25f, 0f);
            var zm = FindObjectOfType<World.ZoneManager>();
            if (zm != null && zm.TryGetZone("Lobby", out var lobbyDef))
                spawnPos = new Vector3(lobbyDef.gridOffset.x + 25f, lobbyDef.gridOffset.y + 25f, 0f);

            // ── 1. Resolve player class (Resources.LoadAll scan) ────────────
            Report("Loading player class"); yield return null;
            var resolvedDef = ResolveSelectedPlayerDefinition() ?? defaultPlayerDef;
            if (resolvedDef == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player definition available for spawned player.");
                yield break;
            }
            if (!string.IsNullOrWhiteSpace(resolvedDef.playerKey))
                PlayerSelectionState.SetSelectedPlayer(resolvedDef.playerKey);

            // ── 2. Instantiate prefab ───────────────────────────────────────
            Report("Spawning player entity"); yield return null;
            var playerGo = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            playerGo.tag = "Player";
            playerGo.transform.SetParent(GetSceneContainer("[Entities]"), true);

            // ── 3. Animation rebind (heaviest single chunk: 7 directional sets) ──
            Report("Building player visuals"); yield return null;
            EntitySetup.ConfigurePlayerVisuals(playerGo, resolvedDef);

            // ── 4. Health / movement / combat / dash ────────────────────────
            Report("Wiring player combat"); yield return null;
            EntitySetup.ConfigurePlayerCombat(playerGo, resolvedDef);

            // ── 5. Spell catalog scan + per-spell registration ──────────────
            Report("Loading spell book"); yield return null;
            EntitySetup.ConfigurePlayerSpells(playerGo);

            // ── 6. Mana, XP, inventory, currency, death flow, class marker ──
            Report("Initializing player stats"); yield return null;
            EntitySetup.ConfigurePlayerStats(playerGo, resolvedDef);

            // ── 7. HUD singletons (inventory, spell bar, icons, range) ──────
            Report("Building HUD"); yield return null;
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

        private PlayerDefinition TryResolveCatalogDefinition(string selectedKey)
        {
            if (string.IsNullOrWhiteSpace(selectedKey))
                return null;

            if (defaultPlayerDef != null &&
                string.Equals(defaultPlayerDef.playerKey, selectedKey, System.StringComparison.OrdinalIgnoreCase))
            {
                return defaultPlayerDef;
            }

            var resourceDefs = Resources.LoadAll<PlayerDefinition>(string.Empty);
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
