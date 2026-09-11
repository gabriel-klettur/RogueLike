using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The player's aim, drawn ON THE GROUND at their feet.
    ///
    /// <para><b>It answers one question — which way am I pointing — and it is allowed to answer
    /// nothing else.</b> That is a constraint on the component, not a description of how much
    /// has been built yet. Everything else about it follows from that one rule. The radius is
    /// FIXED, so it cannot be read as reach. It is WHITE with one shade per layer, because
    /// taking the primary spell's palette made a fireball draw a red marker and turned the aim
    /// indicator into a readout of a loadout choice. Per-slot cooldowns are absent — and
    /// <c>SpellBarHUD</c> and the player panel's mouse slots and dash pip (<c>PlayerHUD</c>)
    /// already say that twice over. So is the charge ramp, which <c>ChargeBuildFX</c> owns.</para>
    ///
    /// <para><b>Three second jobs were built here and then removed, and each was individually
    /// defensible.</b> A sweep that dimmed while the primary recovered; a tip that contracted
    /// during a cast wind-up; a ring instead of a point in the Peace stance. Together they made
    /// the one shape under the character mean four things at once, and no glance could separate
    /// "my spell is recovering" from "I am unarmed" from "I turned". A fourth was rejected
    /// before it was written: leaning the tip toward whatever <c>MouseTargetDetector</c> found
    /// breaks the promise outright, since the rig would then point somewhere the player is not.
    /// <c>FacingIndicatorSingleJobTests</c> reads this source and fails on a reference to the
    /// stance, the caster or a cooldown — every one of them arrives looking small.</para>
    ///
    /// <para><b>Structure.</b> An unrotated, unscaled root that FOLLOWS the player rather than
    /// being parented to them — parenting inherits the entity scale, and every rig in this
    /// project that wanted a world size and took a parent's scale ended up rendering at some
    /// other size than the one it authored. Under it, ONE ground plane carrying the vertical
    /// squash, and the aim rotation as a CHILD of that squash. Squashing each piece separately
    /// foreshortens its length without turning its direction, and it then slides across the
    /// floor instead of lying on it.</para>
    ///
    /// <para><b>Depth.</b> The rig draws on the BODY's own sorting layer, rebased on the feet
    /// every frame, and it goes behind the body when the aim points north and in front when it
    /// points south — the same "sort by the sign of the depth against the caster" rule
    /// <c>ShieldSphereFX</c> records. It shipped on Overhead first, above every painted layer,
    /// and the first thing the player reported was the tip drawn over their own legs when
    /// aiming up.</para>
    ///
    /// <para><b>It is two layers: a chevron and the glow around it.</b> It had four — a light
    /// pool at the feet and a 60-degree wedge sweeping the ground between the pool and the tip
    /// — and those two read as a cone of particles growing out of the character's boots, which
    /// is what they were cut for. The pool's actual JOB is the thing to remember before anyone
    /// adds it back: it anchored the marker to the body, and without it the first live capture
    /// read as an arrowhead floating a unit away with nothing connecting the two. The aura
    /// carries that now by being large enough to belong to the chevron.</para>
    /// </summary>
    public partial class FacingIndicator : MonoBehaviour
    {
        private PlayerController _player;
        private Health _health;
        /// <summary>The body sprite on the entity root — the renderer <c>YSortEntity</c> writes,
        /// and the one whose LAYER the rig follows (it moves to EntitiesOverhead when the player
        /// stands on the overhead visual layer).</summary>
        private SpriteRenderer _bodySr;

        private Transform _root;
        private Transform _aim;
        private SpriteRenderer _auraSr;
        private SpriteRenderer _tipSr;

        private void Start()
        {
            _player = GetComponent<PlayerController>();
            _health = GetComponent<Health>();
            _bodySr = GetComponent<SpriteRenderer>();
            BuildRig();
        }

        private void OnDestroy()
        {
            // The root is not a child of the player, so nothing else would take it down.
            if (_root != null) Destroy(_root.gameObject);
        }

        /// <summary>
        /// Build the two-layer ground rig. Internal so an EditMode test can assemble one
        /// without Play Mode: <c>Awake</c>/<c>Start</c> never run on a component added outside
        /// Play Mode, and a test that adds this component and then measures the rig would be
        /// measuring an object that was never built.
        /// </summary>
        internal void BuildRig()
        {
            if (_root != null) return;

            if (_bodySr == null) _bodySr = GetComponent<SpriteRenderer>();
            FacingIndicatorSprites.EnsureBuilt();

            var rootGo = new GameObject("FacingIndicator");
            _root = rootGo.transform;
            var container = GameObject.Find("[VFX]");
            if (container != null) _root.SetParent(container.transform, false);
            _root.position = transform.position;
            _root.rotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            // One squash for the whole rig, and the aim turns UNDER it.
            var groundGo = new GameObject("GroundPlane");
            var ground = groundGo.transform;
            ground.SetParent(_root, false);
            ground.localPosition = Vector3.zero;
            ground.localRotation = Quaternion.identity;
            ground.localScale = new Vector3(1f, FacingIndicatorStyle.GroundSquash, 1f);

            var aimGo = new GameObject("Aim");
            _aim = aimGo.transform;
            _aim.SetParent(ground, false);
            _aim.localPosition = Vector3.zero;
            _aim.localScale = Vector3.one;

            // Aura first so the tip draws over it. Both hang off the AIM: they are one shape
            // and neither means anything without the other.
            _auraSr = MakeLayer("Aura", FacingIndicatorSprites.Aura, _aim);
            _tipSr = MakeLayer("Tip", FacingIndicatorSprites.Tip, _aim);

            PaintLayers(0f);
            ApplyState(Time.deltaTime, snapHeading: true);
        }

        private SpriteRenderer MakeLayer(string layerName, Sprite sprite, Transform parent)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // Layer and order are written by ApplyDepth every frame, from the body. This is only
            // the value they hold before the first ApplyState — and never SortingConfig.Z_SKY:
            // that is a Z DEPTH, and passing it as a sortingOrder is what buried the old chevron
            // under wall tops, decorations, projectiles and every VFX in the game.
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = 0;
            // Both layers are ADDITIVE. The rig used to carry one alpha layer — a dark rim
            // under the chevron — and it went with the redesign: a white aura cannot outline a
            // white shape, so there is nothing left here that wants to darken the ground.
            sr.sharedMaterial = Spells.ElementalSprites.SharedAdditiveMaterial;
            return sr;
        }
    }
}
