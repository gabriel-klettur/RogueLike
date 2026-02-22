using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Auto-discovers the player in the scene and initializes the HUD system.
    /// Attach to a GameObject in the MainGameplay scene or let GameDirector create it.
    /// Polls until the player is found (handles async spawning).
    /// </summary>
    public class HUDBootstrap : MonoBehaviour
    {
        private bool _initialized;

        private void Update()
        {
            if (_initialized) return;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            var health = player.GetComponent<Health>();
            if (health == null) return;

            var mana = player.GetComponent<Mana>();

            // Create HUDManager
            var hudGo = new GameObject("HUDManager");
            var hudManager = hudGo.AddComponent<HUDManager>();
            hudManager.InitializeForPlayer(health, mana);

            var targetHUD = hudManager.TargetHUD;

            // Wire player MeleeCombat hits to TargetHUD
            var combat = player.GetComponent<MeleeCombat>();
            if (combat != null && targetHUD != null)
                combat.OnHitTarget += (hitGo, dmg) => targetHUD.ShowTarget(hitGo);

            // Wire mouse hover detection to TargetHUD
            var mouseDetector = player.GetComponent<MouseTargetDetector>();
            if (mouseDetector == null)
                mouseDetector = player.AddComponent<MouseTargetDetector>();
            int npcLayer = LayerMask.GetMask("NPC");
            if (npcLayer == 0) npcLayer = 1 << LayerMask.NameToLayer("NPC");
            mouseDetector.SetDetectableLayers(npcLayer);
            if (targetHUD != null)
                mouseDetector.OnTargetChanged += (target) => targetHUD.SetHoverTarget(target);

            // Create DebugHUD overlay (F1 to toggle)
            var debugGo = new GameObject("DebugHUD");
            debugGo.AddComponent<DebugHUD>();

            // Create DeathScreen overlay
            if (FindObjectOfType<DeathScreenUI>() == null)
            {
                var deathGo = new GameObject("DeathScreenUI");
                deathGo.AddComponent<DeathScreenUI>();
            }

            _initialized = true;
            Debug.Log("[HUDBootstrap] HUD system initialized (main + debug + death screen + mouse targeting).");
        }
    }
}
