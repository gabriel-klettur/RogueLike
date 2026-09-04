using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The swung half of harvesting: a Deplete seam worked by holding the attack button,
    /// exactly the way a tree is already chopped.
    ///
    /// <para>Chopping has always had two ways in — hold the interact key and stand there, or
    /// just swing at the tree — because a tree is a <see cref="BuildingDurability"/> and every
    /// melee swing already reaches those. A mine had only the first, so two activities that
    /// look identical on screen answered the mouse differently, and the one that ignored it
    /// looked broken.</para>
    ///
    /// <para>Reached through <see cref="HarvestSwingRegistry"/> rather than by implementing
    /// <c>IDestructibleObstacle</c>; that file records why, and it is the difference between
    /// a mine a player can mine and a mine a stray fireball can delete.</para>
    ///
    /// <para>WHAT A SWING IS WORTH goes through <see cref="ApplyWork"/>, which the interact
    /// session also calls — the same arithmetic a tree gets, so the mouse, the key and a
    /// combat swing can never disagree about what a tool is worth.</para>
    /// </summary>
    public partial class HarvestNode
    {
        /// <summary>
        /// Work banked toward the next charge. It CARRIES between blows rather than resetting,
        /// so a tool that scores just under the threshold still gets there — resetting would
        /// make a whole band of damage values do literally nothing, and the player would have
        /// no way to tell that band from being immune.
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
        /// A swing landed on the seam. Judged through the SAME resolver the interact session
        /// uses, so the mouse and the key can never disagree about what a tool is worth.
        /// </summary>
        public void ApplySwing(int amount, GameObject attacker, Vector2 contactPoint,
            SpellElement? element)
        {
            if (!AcceptsSwing || amount <= 0) return;

            var blow = HarvestBlowResolver.Resolve(_profile, attacker, element);

            // A zero multiplier is a deliberate immunity. The blow still reports, so the
            // feedback layer can bounce it and say why — silence here is what makes a player
            // think the node is scenery.
            if (blow.Immune)
            {
                EnsurePresentation();
                BlowLanded?.Invoke(blow, 0);
                return;
            }

            int yields = ApplyWork(amount, blow, WorkerDropPoint(attacker));

            EnsurePresentation();
            BlowLanded?.Invoke(blow, yields);
        }

        /// <summary>
        /// Spend one blow's worth of work on the seam, and return how many stacks it produced.
        ///
        /// <para>THE SINGLE OWNER of what a blow is worth to a Deplete node, shared by the
        /// interact session and by a swing. It is deliberately the SAME arithmetic
        /// <see cref="BuildingDurability"/> applies to a tree: the resistance multiplier scales
        /// the blow's DAMAGE, <see cref="HarvestBlowResolver.Scale"/> keeps a real multiplier
        /// from rounding to nothing, and the clock is left alone.</para>
        ///
        /// <para>It did not used to be. Mining spent the multiplier on the CLOCK in the channel
        /// and on fractional progress in a swing, against chopping spending it on damage — one
        /// concept with three implementations and three separate wrong-tool floors
        /// (MIN_RATE_SCALE, MAX_SWINGS_PER_CHARGE, and Scale's integer floor). The result was
        /// two activities that look identical on screen and answer the same tool differently,
        /// and the difference between them lived in code rather than in data. What separates a
        /// seam from a tree now is only what the profile says: how much work it holds and
        /// whether running out destroys it.</para>
        /// </summary>
        private int ApplyWork(int amount, HarvestBlow blow, Vector3 dropOrigin)
        {
            int dealt = HarvestBlowResolver.Scale(amount, blow.Multiplier);
            if (dealt <= 0) return 0;

            _chargeProgress += dealt;

            int freed = 0;
            while (_chargeProgress >= WorkPerCharge && _chargesRemaining > 0)
            {
                _chargeProgress -= WorkPerCharge;
                _chargesRemaining--;
                freed++;
            }

            int yields = 0;
            for (int i = 0; i < freed; i++) yields += RollYield(dropOrigin);

            if (_chargesRemaining > 0) return yields;

            _chargeProgress = 0;
            EnterSpentState();

            // A shift and a swing can be running at once — the player can hold the interact
            // key and still attack. Whichever empties the seam ends both.
            CancelInteraction();
            return yields;
        }

        /// <summary>
        /// Where a yield lands: at the WORKER'S FEET, not at the node's centre.
        ///
        /// <para>Not cosmetic. A seam's centre is inside the rock — the building's own
        /// collision cells are painted over it — so a stack dropped there is spawned inside
        /// geometry the player cannot walk into, and the ore they just earned is either
        /// unreachable or has to be fished out from a corner. It also reads wrong: a pickaxe
        /// swing that makes ore appear four units away, on the far side of the thing being
        /// struck, does not look like the swing produced it.</para>
        ///
        /// <para>The worker's transform IS their feet: ValkurAssetPostprocessor forces a
        /// (0.5, 0) pivot on everything under Art/Characters, so the pivot is the bottom row
        /// of the sprite. No offset needed, and adding one would drift the day that changes.</para>
        ///
        /// <para>Falls back to the node when there is no worker — a blow can arrive from a
        /// caster-less source, and a drop at the rock is better than a drop at the origin.</para>
        /// </summary>
        private Vector3 WorkerDropPoint(GameObject worker)
        {
            if (worker != null) return worker.transform.position;
            return _worker != null ? _worker.transform.position : (Vector3)WorkableBounds.center;
        }

        /// <summary>
        /// Build the chips, the flash and the bar the first time this node is actually worked,
        /// by either route.
        ///
        /// <para>Lazily rather than at spawn: a populated world is hundreds of nodes and the
        /// great majority are never touched in a run, so building a particle system and a bar
        /// for each one up front would pay for every tree in the forest to render nothing.</para>
        /// </summary>
        private void EnsurePresentation()
        {
            HarvestFeedback.Attach(this);
            HarvestNodeBar.Attach(this);
        }

        /// <summary>
        /// Join and leave the swing registry. Only Deplete nodes do: a Destroy node already
        /// takes swings through its own <see cref="BuildingDurability"/>, and a second path to
        /// the same building would work it twice per swing.
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
