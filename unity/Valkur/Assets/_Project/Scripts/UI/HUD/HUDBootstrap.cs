using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Infrastructure;
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

            var player = EntityRegistry.Player;
            if (player == null) return;

            var health = player.GetComponent<Health>();
            if (health == null) return;

            var mana = player.GetComponent<Mana>();

            // Create HUDManager
            var hudGo = new GameObject("HUDManager");
            var hudManager = hudGo.AddComponent<HUDManager>();
            var uiContainer = GameObject.Find("[UI]");
            if (uiContainer != null) hudGo.transform.SetParent(uiContainer.transform, false);
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
            if (uiContainer != null) debugGo.transform.SetParent(uiContainer.transform, false);

            // Music beat clock (one per scene, lives next to HUD root)
            if (MusicBeatClock.Instance == null)
            {
                var clockGo = new GameObject("MusicBeatClock");
                clockGo.AddComponent<MusicBeatClock>();
                if (uiContainer != null) clockGo.transform.SetParent(uiContainer.transform, false);
            }

            // Now-playing widget (bottom-right, always-on; replaces python ToastRenderSystem)
            if (FindObjectOfType<MusicPlayerHUD>() == null)
            {
                // Create with RectTransform up-front and parent BEFORE adding the
                // MonoBehaviour so its Awake/BuildUI sees the correct Canvas hierarchy.
                var musicGo = new GameObject("MusicPlayerHUD", typeof(RectTransform));
                if (uiContainer != null) musicGo.transform.SetParent(uiContainer.transform, false);
                musicGo.AddComponent<MusicPlayerHUD>();
            }

            // Create DeathBanner overlay (replacement for the old DeathScreenUI red modal).
            if (FindObjectOfType<DeathBannerUI>() == null)
            {
                var deathGo = new GameObject("DeathBannerUI");
                deathGo.AddComponent<DeathBannerUI>();
                if (uiContainer != null) deathGo.transform.SetParent(uiContainer.transform, false);
            }

            // Always-visible day/night HUD pieces: just the sundial clock and
            // the screen-edge vignette tint. Every modifying control (phase
            // shortcuts, weather toggles, speed slider, phase-tuning sliders)
            // moved into the F2 TimeWeatherEditor, so the gameplay HUD stays
            // clean and only shows information, never controls.
            if (FindObjectOfType<DayNightClockHUD>() == null)
            {
                var clockGo = new GameObject("DayNightClockHUD");
                clockGo.AddComponent<DayNightClockHUD>();
                if (uiContainer != null) clockGo.transform.SetParent(uiContainer.transform, false);
            }
            if (FindObjectOfType<DayNightVignetteOverlay>() == null)
            {
                var vignetteGo = new GameObject("DayNightVignetteOverlay");
                vignetteGo.AddComponent<DayNightVignetteOverlay>();
                if (uiContainer != null) vignetteGo.transform.SetParent(uiContainer.transform, false);
            }

            // War / Peace chip, top-left, under the clock. Read-only report of
            // Valkur.Core.PlayerStance plus a click to flip it; the actual control is Tab,
            // read by PlayerStanceToggle on the player.
            if (FindObjectOfType<StanceHUD>() == null)
            {
                var stanceGo = new GameObject("StanceHUD");
                stanceGo.AddComponent<StanceHUD>();
                if (uiContainer != null) stanceGo.transform.SetParent(uiContainer.transform, false);
            }

            // Top-right minimap. Instantiates MinimapManager on the same
            // GameObject and wraps it in a runtime-built panel + heading arrow
            // + zone-name banner consistent with the other HUD widgets.
            if (FindObjectOfType<MinimapHUD>() == null)
            {
                var minimapGo = new GameObject("MinimapHUD");
                minimapGo.AddComponent<MinimapHUD>();
                if (uiContainer != null) minimapGo.transform.SetParent(uiContainer.transform, false);
            }

            // Hide the HUD whenever any runtime editor opens, restore on close.
            // Hosted on this same GameObject ([Systems]/HUDBootstrap) so it lives
            // outside [UI] and survives the SetActive(false) it applies.
            if (GetComponent<HUDVisibilityController>() == null)
                gameObject.AddComponent<HUDVisibilityController>();

            _initialized = true;
            Debug.Log("[HUDBootstrap] HUD system initialized (main + debug + death banner + day/night clock + vignette + mouse targeting).");
        }
    }
}
