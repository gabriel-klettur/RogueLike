using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The swung half of a Deplete seam: worked by holding the attack button exactly the way a
    /// tree is, and the charge arithmetic both the swing and the interact session share.
    ///
    /// <para>Reached through <see cref="HarvestSwingRegistry"/> rather than by implementing
    /// <c>IDestructibleObstacle</c>; that file records why, and it is the difference between a
    /// mine a player can mine and a mine a stray fireball can delete.</para>
    /// </summary>
    public partial class HarvestNode
    {
        /// <summary>
        /// Work banked toward the next charge. It CARRIES between blows, so a tool that scores
        /// just under the threshold still gets there.
        /// </summary>
        private int _chargeProgress;

        private bool _swingRegistered;

        /// <summary>Work banked toward the next charge. A test and diagnosis seam.</summary>
        public int ChargeProgress => _chargeProgress;

        /// <summary>How much work frees one charge.</summary>
        public int WorkPerCharge => _profile != null ? Mathf.Max(1, _profile.blowDamage) : 1;

        /// <summary>Whether a swing landing here would do anything.</summary>
        public bool AcceptsSwing =>
            _profile != null
            && _profile.harvestable
            && _profile.harvestMode == HarvestMode.Deplete
            && !_spent
            && _chargesRemaining > 0;

        /// <summary>
        /// A swing landed on the seam. Judged through the SAME resolver the interact session uses.
        /// </summary>
        public void ApplySwing(int amount, GameObject attacker, Vector2 contactPoint,
            SpellElement? element)
        {
            if (!AcceptsSwing || amount <= 0) return;

            var blow = HarvestBlowResolver.Resolve(_profile, attacker, element);
            NoteWorked(attacker);

            // A zero multiplier is a deliberate immunity. The blow still reports, so the feedback
            // layer can bounce it and say why.
            if (blow.Immune)
            {
                RaiseBlowLanded(blow, 0);
                return;
            }

            HarvestWorkMark.For(attacker)?.Strike(this);
            int yields = ApplyWork(amount, blow, attacker);
            RaiseBlowLanded(blow, yields);
        }

        /// <summary>
        /// Spend one blow's worth of work on the seam, and return how many stacks it produced.
        /// THE SINGLE OWNER of what a blow is worth to a Deplete node, shared by the interact
        /// session and by a swing — the same arithmetic <see cref="BuildingDurability"/> applies
        /// to a tree: the multiplier (matrix, tool gate, skill) scales the blow's DAMAGE and the
        /// clock is left alone.
        /// </summary>
        private int ApplyWork(int amount, HarvestBlow blow, GameObject worker)
        {
            int dealt = HarvestBlowResolver.Scale(amount, blow.Multiplier);
            if (dealt <= 0) return 0;

            TrainSkill(worker, blow);

            _chargeProgress += dealt;

            int freed = 0;
            while (_chargeProgress >= WorkPerCharge && _chargesRemaining > 0)
            {
                _chargeProgress -= WorkPerCharge;
                _chargesRemaining--;
                freed++;
            }

            Vector3 dropOrigin = WorkerDropPoint(worker);
            int yields = 0;
            for (int i = 0; i < freed; i++) yields += RollYield(dropOrigin, worker, blow);

            if (_chargesRemaining > 0) return yields;

            _chargeProgress = 0;
            EnterSpentState();

            // A shift and a swing can be running at once. Whichever empties the seam ends both.
            CancelInteraction();
            return yields;
        }

        /// <summary>
        /// Where a yield lands: at the WORKER'S FEET, not at the node's centre, which is inside
        /// the node's own collision cells. The worker's transform IS their feet: the character
        /// importer forces a (0.5, 0) pivot. Falls back to the node when there is no worker.
        /// </summary>
        private Vector3 WorkerDropPoint(GameObject worker)
        {
            if (worker != null) return worker.transform.position;
            return _worker != null ? _worker.transform.position : (Vector3)WorkableBounds.center;
        }

        /// <summary>
        /// Join and leave the swing registry. Only Deplete nodes do: a Destroy node already takes
        /// swings through its own <see cref="BuildingDurability"/>.
        /// </summary>
        private void RefreshSwingRegistration()
        {
            bool wanted = _profile != null
                          && _profile.harvestable
                          && _profile.harvestMode == HarvestMode.Deplete;

            if (wanted == _swingRegistered) return;

            if (wanted) HarvestSwingRegistry.Register(this);
            else HarvestSwingRegistry.Unregister(this);

            _swingRegistered = wanted;
        }
    }
}
