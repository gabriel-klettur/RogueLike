using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Sets up the MainGameplay scene at runtime.
    /// Spawns the player, camera, HUD, and test monsters.
    /// Temporary bootstrap until full spawner system is ported.
    /// </summary>
    public class GameplaySceneSetup : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerDefinition defaultPlayerDef;
        [SerializeField] private GameObject playerPrefab;

        [Header("Monsters (test)")]
        [SerializeField] private MonsterDefinition testMonsterDef;
        [SerializeField] private GameObject monsterPrefab;
        [SerializeField] private int testMonsterCount = 3;
        [SerializeField] private float spawnRadius = 5f;

        private void Start()
        {
            SpawnPlayer();
            SpawnTestMonsters();
        }

        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player prefab assigned.");
                return;
            }

            var playerGo = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            playerGo.tag = "Player";

            if (defaultPlayerDef != null)
                EntitySetup.ConfigurePlayer(playerGo, defaultPlayerDef);

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
