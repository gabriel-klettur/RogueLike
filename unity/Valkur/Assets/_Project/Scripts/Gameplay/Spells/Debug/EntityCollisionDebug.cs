using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>What a spell did to one entity, as far as the overlay can tell.</summary>
    public enum EntityContactState
    {
        /// <summary>A spell query's broad phase returned one of its colliders, and no damage followed.</summary>
        Touched = 0,
        /// <summary>Damage was actually applied (<c>GameEvents.OnHitDealt</c>).</summary>
        Hit = 1,
    }

    /// <summary>
    /// One entity's frozen picture at the moment a spell reached it: every collider it carried,
    /// in world space, plus the point the executor's narrow phase measured (when it said so).
    /// </summary>
    public sealed class EntityContact
    {
        public int EntityId;
        public string EntityName;
        public EntityContactState State;
        public int Damage;
        public int HitCount;
        /// <summary>World-space closed loops, one per collider (a polygon can add several).</summary>
        public readonly List<Vector2[]> Loops = new List<Vector2[]>();
        /// <summary>Parallel to <see cref="Loops"/>: true for the body collider the spells measure against.</summary>
        public readonly List<bool> LoopIsBody = new List<bool>();
        public bool HasTestPoint;
        public Vector2 TestPoint;
        public bool TestPointAccepted;
        public string TestPointLabel;
        /// <summary>Where to hang the entity's label: the top of its outline.</summary>
        public Vector2 LabelAnchor;
    }

    /// <summary>
    /// The record of which ENTITIES the last spell collision reached, and how.
    ///
    /// <para><b>This is the other half of <see cref="SpellDebugAreas"/>.</b> The areas overlay
    /// draws the shape a spell swept; it cannot say what that shape was compared against. Every
    /// "the area covers the monster and nothing happened" report is a disagreement between two
    /// geometries - the query and the target's collider - plus whatever point the executor
    /// narrows on, and only the first was visible. This freezes the second and third.</para>
    ///
    /// <para><b>It survives until the NEXT collision, not the next cast.</b> A cast that misses
    /// everything leaves the previous picture up, because the question an author is asking is
    /// almost always about the last thing that touched something. The record is replaced the
    /// first time a push arrives under a newer cast serial.</para>
    ///
    /// <para>Two sources, and both are needed. Contacts come from <see cref="SpellProbe"/>: every
    /// collider a spell query returned, whether or not the executor then accepted it - that is
    /// what makes a rejected target visible at all. Hits come from <c>GameEvents.OnHitDealt</c>,
    /// the event every damaging spell path raises once damage has landed, which also catches
    /// projectiles that find their victim through a physics callback rather than a probe. Melee
    /// swings raise the same event and are deliberately ignored (<see cref="BeginMeleeScope"/>):
    /// a monster hitting the player every second would otherwise wipe the picture of the spell
    /// being diagnosed.</para>
    ///
    /// <para>OFF by default and free when off: every push returns on its first line.</para>
    /// </summary>
    public static class EntityCollisionDebug
    {
        /// <summary>Cap on entities in one record - an area over a crowd must not grow without bound.</summary>
        public const int MaxContacts = 64;

        private static readonly List<EntityContact> Contacts = new List<EntityContact>(MaxContacts);
        private static readonly List<Collider2D> ColliderScratch = new List<Collider2D>(8);

        private static bool _enabled;
        private static int _version;
        private static int _castSerial;
        private static int _recordSerial = -1;
        private static int _meleeDepth;
        private static string _pendingSpellKey = string.Empty;
        private static string _pendingCasterName = string.Empty;
        private static Transform _pendingCaster;
        private static string _spellKey = string.Empty;
        private static string _casterName = string.Empty;

        /// <summary>
        /// Master switch, shared by the Entities editor button and the <c>colisiones</c> command.
        /// Turning it off clears the record, for the reason <see cref="SpellDebugAreas.Enabled"/>
        /// gives: a picture of an unknown moment in the past is worse than none.
        /// </summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!value) Clear();
                _version++;
            }
        }

        public static int Version { get { return _version; } }
        public static IReadOnlyList<EntityContact> Current { get { return Contacts; } }
        public static string SpellKey { get { return _spellKey; } }
        public static string CasterName { get { return _casterName; } }

        /// <summary>Called from <c>SpellCaster.ExecuteSpell</c>. Does NOT clear - see the class notes.</summary>
        public static void BeginCast(SpellDefinition spell, Transform caster)
        {
            if (!_enabled) return;
            _castSerial++;
            _pendingSpellKey = spell != null ? spell.spellKey : "(sin hechizo)";
            _pendingCasterName = caster != null ? caster.name : "(sin lanzador)";
            _pendingCaster = caster;
        }

        public static void Clear()
        {
            Contacts.Clear();
            _recordSerial = -1;
            _spellKey = string.Empty;
            _casterName = string.Empty;
            _version++;
        }

        /// <summary>
        /// Brackets a melee swing so its <c>OnHitDealt</c> does not replace the spell picture.
        /// Depth-counted rather than a bool so a nested swing cannot end the scope early.
        /// </summary>
        public static void BeginMeleeScope() { _meleeDepth++; }
        public static void EndMeleeScope() { if (_meleeDepth > 0) _meleeDepth--; }

        // -- Pushes ---------------------------------------------------------------

        /// <summary>A spell query returned this collider.</summary>
        public static void Touched(Collider2D collider)
        {
            if (!_enabled || collider == null) return;
            GameObject entity = ResolveEntity(collider);
            if (entity == null) return;
            if (_pendingCaster != null &&
                (entity.transform == _pendingCaster || _pendingCaster.IsChildOf(entity.transform))) return;

            EntityContact contact = GetOrCreate(entity);
            if (contact == null) return;
            _version++;
        }

        /// <summary>Damage from a spell landed on <paramref name="victim"/>.</summary>
        public static void Hit(GameObject victim, int damage)
        {
            if (!_enabled || victim == null || _meleeDepth > 0) return;
            GameObject entity = ResolveEntity(victim);
            if (entity == null) return;

            EntityContact contact = GetOrCreate(entity);
            if (contact == null) return;

            // Re-freeze at the moment of the blow: the collider may have moved since the broad
            // phase first saw it, and the hit is the moment the author wants on screen.
            Snapshot(entity, contact);
            contact.State = EntityContactState.Hit;
            contact.Damage += damage;
            contact.HitCount++;
            _version++;
        }

        /// <summary>
        /// The narrow-phase point an executor measured for this entity, and whether it passed.
        /// This is the diagnostic the broad-phase outline cannot give: a slash tests the body
        /// CENTRE against its sector, so a large creature whose trunk is inside the arc and
        /// whose centre is not is touched and never hit.
        /// </summary>
        public static void TestPoint(GameObject entity, Vector2 point, bool accepted, string label)
        {
            if (!_enabled || entity == null) return;
            GameObject root = ResolveEntity(entity);
            if (root == null) return;

            EntityContact contact = GetOrCreate(root);
            if (contact == null) return;

            // An accepted test is the more interesting one to keep: once any tick passed, the
            // earlier refusals of that same swing are history.
            if (contact.HasTestPoint && contact.TestPointAccepted && !accepted) return;
            contact.HasTestPoint = true;
            contact.TestPoint = point;
            contact.TestPointAccepted = accepted;
            contact.TestPointLabel = label;
            _version++;
        }

        // -- Internals ------------------------------------------------------------

        private static EntityContact GetOrCreate(GameObject entity)
        {
            if (_recordSerial != _castSerial)
            {
                Contacts.Clear();
                _recordSerial = _castSerial;
                _spellKey = _pendingSpellKey;
                _casterName = _pendingCasterName;
            }

            int id = entity.GetInstanceID();
            for (int i = 0; i < Contacts.Count; i++)
                if (Contacts[i].EntityId == id) return Contacts[i];

            if (Contacts.Count >= MaxContacts) return null;

            var contact = new EntityContact { EntityId = id, EntityName = entity.name };
            Snapshot(entity, contact);
            Contacts.Add(contact);
            return contact;
        }

        private static bool HasHurtbox(GameObject entity)
        {
            var rig = entity.GetComponent<Combat.EntityColliderRig>();
            return rig != null && rig.HurtboxCount > 0;
        }

        private static void Snapshot(GameObject entity, EntityContact contact)
        {
            contact.Loops.Clear();
            contact.LoopIsBody.Clear();

            Collider2D body = EntityColliderConfigurator.GetBodyCollider(entity);
            ColliderScratch.Clear();
            entity.GetComponentsInChildren(false, ColliderScratch);

            float top = float.NegativeInfinity;
            float cx = entity.transform.position.x;
            for (int i = 0; i < ColliderScratch.Count; i++)
            {
                Collider2D c = ColliderScratch[i];
                if (c == null || !c.enabled) continue;
                int before = contact.Loops.Count;
                ColliderOutline.AppendLoops(c, contact.Loops);
                // What a spell can land on is drawn bold: the hurtbox capsules, and the footprint
                // for an entity that has no hurtbox.
                bool isBody = Combat.EntityColliderRig.IsHurtbox(c) ||
                              (c == body && !HasHurtbox(entity));
                for (int k = before; k < contact.Loops.Count; k++)
                {
                    contact.LoopIsBody.Add(isBody);
                    if (!isBody) continue;
                    var loop = contact.Loops[k];
                    for (int p = 0; p < loop.Length; p++)
                        if (loop[p].y > top) { top = loop[p].y; cx = c.bounds.center.x; }
                }
            }

            if (float.IsNegativeInfinity(top))
            {
                Bounds b = ColliderScratch.Count > 0 && ColliderScratch[0] != null
                    ? ColliderScratch[0].bounds
                    : new Bounds(entity.transform.position, Vector3.one);
                top = b.max.y;
                cx = b.center.x;
            }
            contact.LabelAnchor = new Vector2(cx, top + 0.2f);
            ColliderScratch.Clear();
        }

        /// <summary>
        /// The entity a collider belongs to: the object carrying <see cref="Valkur.Gameplay.Health"/>.
        /// Colliders with no Health above them (walls, pickups) are not entities and are skipped.
        /// </summary>
        private static GameObject ResolveEntity(Collider2D collider)
        {
            var health = collider.GetComponentInParent<Valkur.Gameplay.Health>();
            return health != null ? health.gameObject : null;
        }

        private static GameObject ResolveEntity(GameObject go)
        {
            var health = go.GetComponentInParent<Valkur.Gameplay.Health>();
            return health != null ? health.gameObject : null;
        }

        /// <summary>Domain Reload is OFF; a record of last session's world positions must not survive.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Contacts.Clear();
            ColliderScratch.Clear();
            _enabled = false;
            _version = 0;
            _castSerial = 0;
            _recordSerial = -1;
            _meleeDepth = 0;
            _pendingSpellKey = string.Empty;
            _pendingCasterName = string.Empty;
            _pendingCaster = null;
            _spellKey = string.Empty;
            _casterName = string.Empty;
        }
    }
}
