using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// An entity's two physical collision layers, kept apart: the FOOTPRINT that stands on the
    /// ground, and the HURTBOX that follows the drawn body frame by frame.
    ///
    /// <para><b>Why two.</b> The footprint collides with walls and other bodies, so it must not
    /// change with the animation — a creature whose collider grew on every swing would be shoved
    /// out of the wall it stands next to, every swing. The hurtbox is what can be HIT, so it must
    /// change with the animation — a dragon that rears has lifted its head two units, and a blow
    /// aimed at the head it can see has to land. One collider cannot be both.</para>
    ///
    /// <para><b>The hurtbox touches NOTHING.</b> Its capsules live on a child of the entity, on
    /// the entity's own layer, with <c>Collider2D.excludeLayers</c> set to every layer: they
    /// generate no contacts at all (no wall, no portal trigger, no pickup sees them) while every
    /// layer-mask QUERY still returns them — and queries are how every damage path in this
    /// project finds its victims. So no damage path had to learn a new layer, and all 32 physics
    /// layers are already spent anyway. They are children, never components on the root, because
    /// <c>GetComponent&lt;Collider2D&gt;()</c> on the root is how movement, doors and the death
    /// flow find the body, and that must keep answering "the feet".</para>
    ///
    /// <para><b>One entity can now come back from a query as several colliders.</b>
    /// <see cref="EntityHitFilter"/> folds them to one per entity inside <c>SpellProbe</c>, and
    /// the handful of damage paths that query directly go through it too — a fireball must not
    /// hit a dragon once per capsule.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityColliderRig : MonoBehaviour
    {
        public const string HurtboxChildName = "Hurtbox";

        // Collider instance id -> owning rig. Membership is what lets a query result be folded
        // back to its entity without a GetComponentInParent per hit.
        private static readonly Dictionary<int, EntityColliderRig> ByCollider = new Dictionary<int, EntityColliderRig>(512);
        private static readonly List<HurtShape> ShapeScratch = new List<HurtShape>(EntityCollisionProfile.MaxShapesPerFrame);

        private EntityCollisionProfile _profile;
        private SpriteRenderer _renderer;
        private DirectionalAnimator _animator;
        private Collider2D _footprint;
        private GameObject _hurtRoot;
        private readonly List<CapsuleCollider2D> _hurtboxes = new List<CapsuleCollider2D>(EntityCollisionProfile.MaxShapesPerFrame);
        private int _activeHurtboxes;

        private Sprite _appliedSprite;
        private float _appliedSign;
        private Vector3 _appliedRendererScale;
        private Vector3 _appliedRendererLocalPos;
        private float _appliedHurtScale;
        private int _appliedProfileVersion = -1;
        private bool _appliedSuppressed;
        private static int _profileVersion;

        /// <summary>The collider that stands on the ground. Never a hurtbox.</summary>
        public Collider2D Footprint => _footprint;

        /// <summary>Hurtbox capsules active for the frame on screen.</summary>
        public int HurtboxCount => _activeHurtboxes;

        public Collider2D GetHurtbox(int index)
            => index >= 0 && index < _activeHurtboxes ? _hurtboxes[index] : null;

        /// <summary>The profile this rig reads. Held by reference so the Entities editor's edits reach live entities.</summary>
        public EntityCollisionProfile Profile => _profile;

        /// <summary>
        /// Install or re-state the rig. Idempotent: a loadout swap, a respawn through the same
        /// prefab and the Entities editor's reconfigure all come back through here.
        /// </summary>
        public void Configure(EntityCollisionProfile profile, SpriteRenderer renderer,
                              DirectionalAnimator animator, Collider2D footprint)
        {
            _profile = profile ?? new EntityCollisionProfile();
            _renderer = renderer;
            _animator = animator;

            if (_footprint != null && _footprint != footprint) Unregister(_footprint);
            _footprint = footprint;
            if (_footprint != null) ByCollider[_footprint.GetInstanceID()] = this;

            EnsureHurtRoot();
            _appliedSprite = null;
            Refresh(force: true);
        }

        /// <summary>
        /// Tell every live rig that authored shapes changed. The Entities editor calls this after
        /// an edit; a rig otherwise re-places its capsules only when the FRAME changes, and a
        /// paused entity would keep the old shape until it moved.
        /// </summary>
        public static void NotifyProfilesChanged() => _profileVersion++;

        private void LateUpdate() => Refresh(force: false);

        /// <summary>
        /// Place the hurtbox for the frame currently drawn. Cheap when nothing changed: a sprite
        /// reference, a sign and two vectors are compared and nothing is written. When the frame
        /// changes the capsules are rewritten — a few fixture updates at the animation's own
        /// rate (6-8 per second), not per physics step.
        /// </summary>
        public void Refresh(bool force)
        {
            if (_renderer == null || _hurtRoot == null) return;

            Sprite sprite = _renderer.sprite;
            float sign = HurtShapeSpace.FacingSign(_renderer, _animator);
            Transform rt = _renderer.transform;
            float hurtScale = _profile != null ? Mathf.Clamp(_profile.hurtScale, 0.1f, 2f) : 1f;

            // A corpse, an unconscious body and a summon still rising out of the ground all
            // switch the entity's colliders OFF from outside (UnconsciousState, the death
            // cleanup, SummonRiseFX). They reach the footprint; the hurtbox follows it, or the
            // next frame change would switch the capsules back on and a corpse could be hit.
            bool suppressed = _footprint != null && !_footprint.enabled;

            if (!force && sprite == _appliedSprite && Mathf.Approximately(sign, _appliedSign) &&
                rt.lossyScale == _appliedRendererScale && rt.localPosition == _appliedRendererLocalPos &&
                Mathf.Approximately(hurtScale, _appliedHurtScale) && _appliedProfileVersion == _profileVersion &&
                suppressed == _appliedSuppressed)
                return;

            _appliedSuppressed = suppressed;
            if (suppressed)
            {
                _appliedSprite = sprite;
                SetActiveCount(0);
                return;
            }

            _appliedSprite = sprite;
            _appliedSign = sign;
            _appliedRendererScale = rt.lossyScale;
            _appliedRendererLocalPos = rt.localPosition;
            _appliedHurtScale = hurtScale;
            _appliedProfileVersion = _profileVersion;

            if (sprite == null)
            {
                SetActiveCount(0);
                return;
            }

            _profile.ResolveShapes(sprite.name, ShapeScratch);
            int count = Mathf.Min(ShapeScratch.Count, EntityCollisionProfile.MaxShapesPerFrame);
            EnsurePool(count);

            Transform hurt = _hurtRoot.transform;
            for (int i = 0; i < count; i++)
            {
                HurtShapeSpace.ToWorld(_renderer, sign, ShapeScratch[i], hurtScale,
                                       out Vector2 worldCenter, out Vector2 worldSize);
                var capsule = _hurtboxes[i];
                Vector3 lossy = hurt.lossyScale;
                float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
                float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
                Vector2 localSize = new Vector2(worldSize.x / sx, worldSize.y / sy);
                capsule.offset = hurt.InverseTransformPoint(worldCenter);
                capsule.size = new Vector2(Mathf.Max(0.01f, localSize.x), Mathf.Max(0.01f, localSize.y));
                capsule.direction = localSize.y >= localSize.x
                    ? CapsuleDirection2D.Vertical
                    : CapsuleDirection2D.Horizontal;
            }
            SetActiveCount(count);
        }

        // -- Queries --------------------------------------------------------------

        /// <summary>The rig that owns a collider, whether its footprint or one of its capsules.</summary>
        public static bool TryGetOwner(Collider2D collider, out EntityColliderRig rig)
        {
            rig = null;
            if (collider == null) return false;
            return ByCollider.TryGetValue(collider.GetInstanceID(), out rig) && rig != null;
        }

        /// <summary>True for a hurtbox capsule of any rig.</summary>
        public static bool IsHurtbox(Collider2D collider)
            => TryGetOwner(collider, out var rig) && collider != rig._footprint;

        /// <summary>
        /// The point of this body nearest to <paramref name="from"/>: across every hurtbox
        /// capsule, or the footprint when the frame carries none.
        /// </summary>
        public Vector2 ClosestPoint(Vector2 from)
        {
            float best = float.PositiveInfinity;
            Vector2 result = transform.position;
            for (int i = 0; i < _activeHurtboxes; i++)
            {
                Vector2 p = _hurtboxes[i].ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; result = p; }
            }
            if (float.IsPositiveInfinity(best) && _footprint != null && _footprint.enabled)
                result = _footprint.ClosestPoint(from);
            return result;
        }

        /// <summary>
        /// Points that sample this body for a SHAPED test (a sword's arc, a breath's cone): for
        /// each capsule its point nearest the attacker, its centre and both ends of its spine.
        /// A sector test on one point per body is what made a large creature immune whenever its
        /// centre sat a degree outside an arc that visibly covered half of it.
        /// </summary>
        public void CollectProbePoints(Vector2 from, List<Vector2> points)
        {
            for (int i = 0; i < _activeHurtboxes; i++)
            {
                var c = _hurtboxes[i];
                Bounds b = c.bounds;
                points.Add(c.ClosestPoint(from));
                points.Add(b.center);
                if (c.direction == CapsuleDirection2D.Vertical)
                {
                    float reach = Mathf.Max(0f, b.extents.y - b.extents.x);
                    points.Add(new Vector2(b.center.x, b.center.y + reach));
                    points.Add(new Vector2(b.center.x, b.center.y - reach));
                }
                else
                {
                    float reach = Mathf.Max(0f, b.extents.x - b.extents.y);
                    points.Add(new Vector2(b.center.x + reach, b.center.y));
                    points.Add(new Vector2(b.center.x - reach, b.center.y));
                }
            }
            if (_activeHurtboxes == 0 && _footprint != null)
            {
                points.Add(_footprint.ClosestPoint(from));
                points.Add(_footprint.bounds.center);
            }
        }

        /// <summary>World bounds of the hurtbox on screen, or of the footprint when it has none.</summary>
        public Bounds HurtBounds
        {
            get
            {
                if (_activeHurtboxes == 0)
                    return _footprint != null ? _footprint.bounds : new Bounds(transform.position, Vector3.zero);
                Bounds b = _hurtboxes[0].bounds;
                for (int i = 1; i < _activeHurtboxes; i++) b.Encapsulate(_hurtboxes[i].bounds);
                return b;
            }
        }

        /// <summary>Every collider of this rig — footprint and active capsules — appended to <paramref name="result"/>.</summary>
        public void CollectColliders(ICollection<Collider2D> result)
        {
            if (_footprint != null) result.Add(_footprint);
            for (int i = 0; i < _hurtboxes.Count; i++)
                if (_hurtboxes[i] != null) result.Add(_hurtboxes[i]);
        }

        // -- Internals --------------------------------------------------------------

        private void EnsureHurtRoot()
        {
            if (_hurtRoot != null) return;

            Transform existing = transform.Find(HurtboxChildName);
            _hurtRoot = existing != null ? existing.gameObject : new GameObject(HurtboxChildName);
            _hurtRoot.transform.SetParent(transform, false);
            _hurtRoot.transform.localPosition = Vector3.zero;
            _hurtRoot.transform.localRotation = Quaternion.identity;
            _hurtRoot.transform.localScale = Vector3.one;
            _hurtRoot.layer = gameObject.layer;

            _hurtboxes.Clear();
            _hurtRoot.GetComponents(_hurtboxes);
            for (int i = 0; i < _hurtboxes.Count; i++) Prepare(_hurtboxes[i]);
        }

        private void EnsurePool(int count)
        {
            // The layer is re-stated every time: EntitySetup assigns the entity's layer after
            // some paths already built the rig, and a capsule left on Default is one no target
            // mask contains — an entity nothing can hit, silently.
            _hurtRoot.layer = gameObject.layer;
            while (_hurtboxes.Count < count)
            {
                var capsule = _hurtRoot.AddComponent<CapsuleCollider2D>();
                Prepare(capsule);
                _hurtboxes.Add(capsule);
            }
        }

        private void Prepare(CapsuleCollider2D capsule)
        {
            // usedByComposite and density are left alone on purpose: a capsule cannot be
            // composited and density only exists under auto-mass, and assigning either logs a
            // warning per entity even when the value is the default.
            capsule.isTrigger = false;
            // Every layer excluded: a hurtbox produces no contact with anything, so it can
            // never push, block, trip a portal or collect a pickup. Queries ignore this mask,
            // which is the property the whole design rests on.
            capsule.excludeLayers = ~0;
            capsule.includeLayers = 0;
            ByCollider[capsule.GetInstanceID()] = this;
        }

        private void SetActiveCount(int count)
        {
            _activeHurtboxes = count;
            for (int i = 0; i < _hurtboxes.Count; i++)
            {
                bool on = i < count;
                if (_hurtboxes[i] != null && _hurtboxes[i].enabled != on) _hurtboxes[i].enabled = on;
            }
        }

        private static void Unregister(Collider2D collider)
        {
            if (collider != null) ByCollider.Remove(collider.GetInstanceID());
        }

        private void OnDestroy()
        {
            Unregister(_footprint);
            for (int i = 0; i < _hurtboxes.Count; i++) Unregister(_hurtboxes[i]);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByCollider.Clear();
            ShapeScratch.Clear();
            _profileVersion = 0;
        }
    }
}
