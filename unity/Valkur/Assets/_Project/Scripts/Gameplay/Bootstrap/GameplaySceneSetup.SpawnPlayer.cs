using UnityEngine;
using Valkur.Data;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player prefab assigned.");
                return;
            }

            var playerGo = Instantiate(playerPrefab, new Vector3(25f, 25f, 0f), Quaternion.identity);
            playerGo.tag = "Player";

            var selectedDef = ResolveSelectedPlayerDefinition();
            if (selectedDef != null)
            {
                if (!string.IsNullOrWhiteSpace(selectedDef.playerKey))
                    PlayerSelectionState.SetSelectedPlayer(selectedDef.playerKey);
                EntitySetup.ConfigurePlayer(playerGo, selectedDef);
            }
            else if (defaultPlayerDef != null)
            {
                EntitySetup.ConfigurePlayer(playerGo, defaultPlayerDef);
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] No player definition available for spawned player.");
            }
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

            for (int i = 0; i < testMonsterCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnRadius;
                Vector3 pos = new Vector3(offset.x, offset.y, 0f);
                var monsterGo = Instantiate(monsterPrefab, pos, Quaternion.identity);
                EntitySetup.ConfigureMonster(monsterGo, testMonsterDef);
            }
        }
    }
}
