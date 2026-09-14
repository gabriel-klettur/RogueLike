using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// One blow's worth of work that actually reached a building: who threw it, what it amounted
    /// to after the matrix, the tool gate and the worker's skill, and where it landed.
    ///
    /// <para>It exists so that EVERY way of hitting a tree — the interact key, a sword swing, a
    /// slash spell, a thrown boomerang — reports through ONE event raised by
    /// <see cref="BuildingDurability"/>. Before it, only the interact session rolled yields and
    /// drew feedback, so the same tree paid and looked differently depending on which button felled
    /// it.</para>
    /// </summary>
    public readonly struct HarvestWork
    {
        public readonly GameObject Attacker;

        /// <summary>Durability actually removed. 0 for an immune blow, which is still reported.</summary>
        public readonly int Dealt;

        public readonly HarvestBlow Blow;
        public readonly Vector2 Contact;

        /// <summary>True when this blow is the one that finishes the building.</summary>
        public readonly bool Finishing;

        public HarvestWork(GameObject attacker, int dealt, HarvestBlow blow, Vector2 contact, bool finishing)
        {
            Attacker = attacker;
            Dealt = dealt;
            Blow = blow;
            Contact = contact;
            Finishing = finishing;
        }
    }
}
