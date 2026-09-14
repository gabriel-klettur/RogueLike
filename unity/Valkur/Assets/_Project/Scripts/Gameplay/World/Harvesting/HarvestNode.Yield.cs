using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Skills;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What work PAYS and TEACHES. The single place a blow on a node turns into goods on the
    /// ground and into skill, whichever route the blow took.
    ///
    /// <para><b>PAID BY WORK, NOT BY BLOW.</b> A Destroy node banks the durability each blow
    /// actually removed and pays one yield per <see cref="DestructionProfile.workPerYield"/>. So
    /// the wood in a tree is a property of the TREE: an axe gets it out in four blows, bare hands
    /// in forty, and both come home with the same logs — the axe is faster, never poorer. The old
    /// rule (one yield per blow, whatever it dealt) made bare hands the richest tool in the game.</para>
    ///
    /// <para><b>ONLY A PLAYER'S PHYSICAL WORK PAYS.</b> A fireball that burns a tree down leaves
    /// ash, and a monster's slash that clips a trunk must not scatter logs across the map. Casts
    /// and non-player blows still damage and fell — that is durability's business — they simply
    /// produce nothing and teach nothing.</para>
    ///
    /// <para><b>STRAIGHT INTO THE BAG.</b> A yield is added to the worker's inventory the moment
    /// it is extracted; only when the bag cannot take it does it fall at their feet. A pile of
    /// logs to walk over after every tree is a chore between the player and the next swing, and
    /// the extraction itself is what the feedback celebrates.</para>
    ///
    /// <para><b>SKILL CHOOSES QUALITY, THE NODE CHOOSES KIND.</b> A yield is drawn from the skill's
    /// <see cref="GatheringYieldTable"/> at the worker's skill against the node's tags, so a master
    /// finds better wood and only a volcanic tree gives ember wood.</para>
    /// </summary>
    public partial class HarvestNode
    {
        private int _workTowardYield;
        private readonly System.Random _yieldRng = new System.Random();

        /// <summary>Durability banked toward the next yield. A test seam.</summary>
        public int WorkTowardYield => _workTowardYield;

        /// <summary>The item the most recent yield produced. What the feedback names.</summary>
        public ItemDefinition LastYieldItem { get; private set; }

        /// <summary>A Destroy node was finished; the int is how many bonus yields it paid.</summary>
        public event Action<int> Felled;

        /// <summary>
        /// One skill yield was extracted, naming the item and whether it went into the worker's
        /// bag (true) or fell to the ground because the bag was full (false). Raised for per-work
        /// yields AND for the fell bonus, so a feedback layer can show every log.
        /// </summary>
        public event Action<ItemDefinition, bool> ItemYielded;

        /// <summary>
        /// Every blow a Destroy-mode node takes, from any source: the interact session, a sword,
        /// a slash spell, a projectile.
        /// </summary>
        private void OnDurabilityWorked(HarvestWork work)
        {
            if (_profile == null) return;
            NoteWorked(work.Attacker);

            // BEFORE the yields: the feedback names each item through ItemYielded, and on the very
            // first blow a node receives it is not attached yet — the first log would go unnamed.
            EnsurePresentation();

            if (IsPlayer(work.Attacker) && work.Blow.Physical)
                HarvestWorkMark.For(work.Attacker)?.Strike(this);

            int yields = 0;
            if (work.Dealt > 0 && !work.Blow.Immune && work.Blow.Physical && IsPlayer(work.Attacker))
            {
                TrainSkill(work.Attacker, work.Blow);

                _workTowardYield += work.Dealt;
                int per = Mathf.Max(1, _profile.workPerYield);
                Vector3 drop = WorkerDropPoint(work.Attacker);

                while (_workTowardYield >= per)
                {
                    _workTowardYield -= per;
                    yields += RollYield(drop, work.Attacker, work.Blow);
                }
            }

            RaiseBlowLanded(work.Blow, yields);
        }

        /// <summary>
        /// The bonus for finishing a tree, paid to the player whose physical blow felled it:
        /// the profile's flat bonus plus one per <c>bonusYieldEverySkill</c> points of skill, and
        /// whatever work was banked toward a yield that never completed, rounded up — so the last
        /// few points of durability are never simply lost.
        /// </summary>
        private void PayFellBonus()
        {
            if (_durability == null) return;

            var attacker = _durability.LastAttacker;
            var blow = _durability.LastBlow;
            if (!IsPlayer(attacker) || !blow.Physical || blow.Immune) return;

            int count = _profile.fellBonusYields;
            if (_profile.gatheringSkill != null)
                count += _profile.gatheringSkill.BonusYields(SkillTenthsOf(attacker));
            if (_workTowardYield > 0) count++;
            _workTowardYield = 0;

            Vector3 drop = WorkerDropPoint(attacker);
            int paid = 0;
            for (int i = 0; i < count; i++) paid += RollYield(drop, attacker, blow);

            Felled?.Invoke(paid);
        }

        /// <summary>
        /// Draw one yield and put it in the worker's bag, or on the ground when the bag is full.
        /// Returns 1 when the item really went somewhere.
        /// A skilled node draws from its skill's table; a node without one falls back to the
        /// weighted pool or the independent table, which is what the mines still use.
        /// </summary>
        private int RollYield(Vector3 origin, GameObject worker, HarvestBlow blow)
        {
            if (_profile.UsesSkillYield)
            {
                float percent = SkillDefinition.ToPercent(SkillTenthsOf(worker));
                var item = _profile.gatheringSkill.yieldTable.Roll(_yieldRng, percent, _profile.yieldTags, out _);
                if (item == null) return 0;

                bool inBag = TryPutInBag(worker, item);
                if (!inBag)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.55f;
                    var position = new Vector3(origin.x + offset.x, origin.y + offset.y, origin.z);
                    if (DropSystem.SpawnDrop(item, 1, position) == null) return 0;
                }

                LastYieldItem = item;
                ItemYielded?.Invoke(item, inBag);
                GameEvents.FireResourceGathered(worker, _profile.gatheringSkill.skillKey, item.itemId, 1);
                return 1;
            }

            // The weighted pool wins when both are authored: it makes the lines COMPETE.
            if (_profile.yieldPool != null)
                return HarvestDropResolver.SpawnFromPool(_profile.yieldPool, origin);

            if (_profile.yieldPerBlow == null) return 0;
            return HarvestDropResolver.SpawnDrops(_profile.yieldPerBlow, origin);
        }

        /// <summary>
        /// Add one of <paramref name="item"/> to the worker's bag. False when there is no bag or no
        /// room — never a partial add, since a quantity of one either fits or it does not.
        /// </summary>
        private static bool TryPutInBag(GameObject worker, ItemDefinition item)
        {
            if (worker == null || item == null) return false;
            var bag = worker.GetComponentInParent<Valkur.Gameplay.Inventory.Inventory>();
            if (bag == null) return false;
            if (bag.AddItem(item, 1) > 0) return false;

            GameEvents.FireItemPickedUp(worker, item.displayName, 1);
            return true;
        }

        /// <summary>Roll for a skill gain against this node. Players only, physical blows only.</summary>
        private void TrainSkill(GameObject worker, HarvestBlow blow)
        {
            if (_profile == null || _profile.gatheringSkill == null) return;
            if (!blow.Physical || blow.Immune) return;

            var skills = PlayerSkills.For(worker);
            if (skills == null) return;

            // A weak tapped cut teaches in proportion to its worth. Gains are rolled per blow and
            // tapping multiplies blows, so without this a player could tap Awful at a master's
            // tempo and out-learn a careful one.
            float worth = _profile.gatheringSkill.CutWorth(_landingCut);
            if (worth < 1f && _yieldRng.NextDouble() >= worth) return;

            skills.TryGain(_profile.gatheringSkill, _profile.skillDifficulty, blow.WrongTool);
        }

        private static bool IsPlayer(GameObject go)
        {
            if (go == null) return false;
            return go.CompareTag("Player") || go.transform.root.CompareTag("Player");
        }
    }
}
