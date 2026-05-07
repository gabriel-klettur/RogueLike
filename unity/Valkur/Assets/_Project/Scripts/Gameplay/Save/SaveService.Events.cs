using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    public partial class SaveService
    {
        private void RebindGameEvents()
        {
            // Removing a non-subscribed handler is a safe no-op, so doing
            // unbind+bind unconditionally keeps subscriptions exactly-once
            // even when GameEvents.Clear() ran between calls.
            UnbindGameEvents();
            // Tier 2 dirty triggers — flip the flag, the timer / debounce
            // takes care of the actual write.
            GameEvents.OnPlayerDamaged += HandlePlayerDamaged;
            GameEvents.OnXpGained      += HandleXpGained;
            GameEvents.OnItemPickedUp  += HandleItemPickedUp;
            GameEvents.OnItemConsumed  += HandleItemConsumed;
            // Critical milestones — force an immediate save.
            GameEvents.OnLevelUp       += HandleLevelUp;
            GameEvents.OnZoneChanged   += HandleZoneChanged;
            GameEvents.OnPlayerDied    += HandlePlayerDied;
            GameEvents.OnEntityDied    += HandleEntityDied;
        }

        private void UnbindGameEvents()
        {
            GameEvents.OnPlayerDamaged -= HandlePlayerDamaged;
            GameEvents.OnXpGained      -= HandleXpGained;
            GameEvents.OnItemPickedUp  -= HandleItemPickedUp;
            GameEvents.OnItemConsumed  -= HandleItemConsumed;
            GameEvents.OnLevelUp       -= HandleLevelUp;
            GameEvents.OnZoneChanged   -= HandleZoneChanged;
            GameEvents.OnPlayerDied    -= HandlePlayerDied;
            GameEvents.OnEntityDied    -= HandleEntityDied;
        }

        private void HandlePlayerDamaged(int amount, int currentHp, int maxHp) =>
            MarkDirty($"player damaged ({amount} dmg)");

        private void HandleXpGained(GameObject entity, int amount)
        {
            if (entity != null && entity.CompareTag("Player"))
                MarkDirty($"player gained {amount} XP");
        }

        private void HandleLevelUp(GameObject entity, int newLevel)
        {
            if (entity == null || !entity.CompareTag("Player")) return;
            // Level-up is a milestone — force the save now instead of waiting
            // for the timer. A crash between level-up and next periodic save
            // would otherwise lose the new level + skill points.
            //
            // MarkDirty FIRST so that if SaveImmediately fails (e.g.
            // GameStateCollector returns null because the player got destroyed
            // mid-frame, or disk write throws) the autosave timer / debounce
            // still picks up the event on its next pass. On success
            // WriteAutosaveToDisk re-clears the flag, so production keeps the
            // exact same end state.
            MarkDirty($"player leveled up to {newLevel}");
            SaveImmediately($"player leveled up to {newLevel}");
        }

        private void HandleItemPickedUp(GameObject collector, string itemName, int quantity)
        {
            if (collector != null && collector.CompareTag("Player"))
                MarkDirty($"player picked up {itemName} x{quantity}");
        }

        private void HandleItemConsumed(GameObject consumer, string itemName)
        {
            if (consumer != null && consumer.CompareTag("Player"))
                MarkDirty($"player consumed {itemName}");
        }

        private void HandleZoneChanged(string oldZone, string newZone)
        {
            // Zone transitions are the canonical "checkpoint" in sandbox
            // games. Force-save so a crash on the new zone never sends the
            // player back to the old one.
            // See HandleLevelUp for rationale on the MarkDirty + SaveImmediately
            // ordering (defence-in-depth: if the immediate save fails, the
            // timer still picks the event up).
            string reason = $"zone {oldZone} → {newZone}";
            MarkDirty(reason);
            SaveImmediately(reason);
        }

        private void HandlePlayerDied()
        {
            // Player death is the most expensive thing to lose — restart-from-
            // checkpoint UX depends on the on-disk state being current. We
            // still gate on _hasKnownPlayerPos because OnApplicationQuit's
            // alive-only guard does not apply to death itself (we WANT the
            // dead state recorded so the run-end UI can read it).
            // MarkDirty + SaveImmediately: same defence-in-depth pattern as
            // HandleLevelUp / HandleZoneChanged.
            MarkDirty("player died");
            SaveImmediately("player died");
        }

        private void HandleEntityDied(GameObject victim, GameObject killer)
        {
            // GameEvents.FireEntityDied passes (victim, killer) in that order;
            // the same convention as OnEntityDamaged. Keep parameter names in
            // sync with the source signature so the boss-detection branch
            // inspects the actual victim.
            if (victim == null) return;
            var boss = victim.GetComponent<BossPhaseController>();
            if (boss != null)
            {
                string reason = $"boss '{victim.name}' defeated";
                MarkDirty(reason);
                SaveImmediately(reason);
            }
        }
    }
}
