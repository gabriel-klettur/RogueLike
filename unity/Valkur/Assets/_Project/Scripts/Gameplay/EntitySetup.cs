using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Utility to configure entity GameObjects from ScriptableObject definitions.
    /// Used by spawners and scene setup to wire prefabs at runtime.
    /// </summary>
    public static class EntitySetup
    {
        public static void ConfigurePlayer(GameObject go, PlayerDefinition def)
        {
            var health = go.GetComponent<Health>();
            if (health != null)
                health.Initialize(def.initialStrength);

            var controller = go.GetComponent<PlayerController>();
            if (controller != null)
                controller.SetMoveSpeed(def.basicSpeed);

            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
                combat.Initialize(def.basicAttack, 0.5f, 1.5f);
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            var ai = go.GetComponent<MonsterAI>();
            if (ai != null)
                ai.InitializeFromDefinition(def);
        }
    }
}
