using System.Globalization;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Whether the player was dead when the game was saved, and where the body is.
    ///
    /// <para><b>Why this exists.</b> Death had no cost for anyone willing to reload. The collector
    /// refused to write a save while <c>hp &lt;= 0</c> and the restorer bumped any <c>hp == 0</c>
    /// back to full, so the last autosave was always a PRE-DEATH one: die, quit to menu, load, and
    /// the inventory, the coins and the XP were all back. Every dial in the tuning that makes
    /// dying expensive was opt-in for the player.</para>
    ///
    /// <para><b>It rides the save's METADATA bag</b>, exactly as the market's seed and day do, and
    /// for the same three reasons: it is world state rather than player stats, it is a flag and two
    /// floats, and the bag is already this project's answer for run-level facts. No schema bump,
    /// and a save written before this layer simply carries no keys — which reads as "was alive",
    /// which is the correct answer for every save that predates it.</para>
    ///
    /// <para><b>The corpse position is saved separately from the player position</b> and that is
    /// the whole point: a spirit halfway to the altar is nowhere near its own loot, and restoring
    /// one without the other would strand the items with no marker and no compass.</para>
    /// </summary>
    public static class DeathStateSave
    {
        public const string SpiritMetaKey = "death.spirit";
        public const string CorpseXMetaKey = "death.corpse_x";
        public const string CorpseYMetaKey = "death.corpse_y";

        /// <summary>
        /// True when the flow should be persisted at all: the player is in the death flow AND the
        /// tuning says to keep it. With <c>persistDeathState</c> off the save behaves exactly as it
        /// always did, which is what lets the setting be turned off without a second code path.
        /// </summary>
        public static bool ShouldPersist(DeathSequenceController controller)
        {
            if (!DeathTuning.Active.persistDeathState) return false;
            return controller != null && controller.IsDeathFlowActive;
        }

        /// <summary>
        /// Write the flag and the corpse position into <paramref name="data"/>.
        ///
        /// <para>Only the SPIRIT phase is persisted, never Dying or Reviving. Those two are
        /// mid-coroutine states measured in tenths of a second: a save landing inside one would
        /// restore into a phase whose coroutine no longer exists, which is a soft lock rather than
        /// a death. They are both restored as spirit, which is where each of them was heading.</para>
        /// </summary>
        public static void Collect(GameSaveData data, DeathSequenceController controller)
        {
            if (data == null) return;

            if (!ShouldPersist(controller))
            {
                // Written as an explicit 0 rather than left absent, so a save taken after a revive
                // OVERWRITES an older save's flag instead of inheriting it. The metadata bag is a
                // bag, not a snapshot — a key nobody clears is a key that survives forever.
                data.SetMeta(SpiritMetaKey, "0");
                return;
            }

            data.SetMeta(SpiritMetaKey, "1");
            Vector3 corpse = controller.LastDeathPosition;
            data.SetMeta(CorpseXMetaKey, corpse.x.ToString("R", CultureInfo.InvariantCulture));
            data.SetMeta(CorpseYMetaKey, corpse.y.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Read the flag back. Returns false for every save that predates the layer, and for a save
        /// taken while alive.
        /// </summary>
        public static bool TryRead(GameSaveData data, out Vector3 corpsePosition)
        {
            corpsePosition = Vector3.zero;
            if (data == null) return false;

            string flag = data.GetMeta(SpiritMetaKey);
            if (string.IsNullOrEmpty(flag) || flag == "0") return false;

            float.TryParse(data.GetMeta(CorpseXMetaKey), NumberStyles.Float,
                           CultureInfo.InvariantCulture, out float x);
            float.TryParse(data.GetMeta(CorpseYMetaKey), NumberStyles.Float,
                           CultureInfo.InvariantCulture, out float y);
            corpsePosition = new Vector3(x, y, 0f);
            return true;
        }
    }
}
