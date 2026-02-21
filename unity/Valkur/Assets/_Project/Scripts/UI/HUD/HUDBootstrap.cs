using UnityEngine;
using Valkur.Gameplay;

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

            // Create HUDManager
            var hudGo = new GameObject("HUDManager");
            var hudManager = hudGo.AddComponent<HUDManager>();
            hudManager.InitializeForPlayer(health);

            // Wire player MeleeCombat hits to TargetHUD
            var combat = player.GetComponent<MeleeCombat>();
            if (combat != null && hudManager.TargetHUD != null)
            {
                var targetHUD = hudManager.TargetHUD;
                combat.OnHitTarget += (hitGo, dmg) => targetHUD.ShowTarget(hitGo);
            }

            _initialized = true;
            Debug.Log("[HUDBootstrap] HUD system initialized.");
        }
    }
}
