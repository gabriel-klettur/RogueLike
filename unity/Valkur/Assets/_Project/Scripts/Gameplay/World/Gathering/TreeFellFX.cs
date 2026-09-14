using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A felled tree FALLS. The crown tips away from the axe, accelerates, hits the ground with a
    /// bounce, throws up dust and leaves, and fades — while the stump is already standing where
    /// the trunk was.
    ///
    /// <para><b>WHAT IT REPLACED.</b> <see cref="DestructionKind.Fell"/> was authored on the tree
    /// profile and read by nothing, so the tree swapped to its stump in a single frame: the one
    /// moment the whole activity builds up to was the least eventful frame of it.</para>
    ///
    /// <para><b>THE CROWN IS THE CANOPY HALF, NOT A COPY OF THE WHOLE SPRITE.</b> The building is
    /// already split at its split ratio into a footprint (the trunk base, under the player) and a
    /// canopy (everything above). The footprint becomes the stump; the canopy is exactly the part
    /// that should fall, hinged at the line where the two meet — so the trunk reads as snapping
    /// above the stump, with no crop and no <c>Sprite.Create</c> (which on an atlas page is the
    /// 20 ms Tight-mesh trap this project has already measured).</para>
    ///
    /// <para><b>A FALL IS GRAVITY, NOT A LERP.</b> The angle goes as t² — slow to start, fast at
    /// the end — which is the whole difference between a tree coming down and a sign being
    /// rotated. The bounce is small and one-sided: a trunk does not rebound past the ground.</para>
    ///
    /// <para><b>SELF-CONTAINED AND SELF-DESTRUCTING.</b> It copies the sprite, material and
    /// sorting it needs at spawn and holds no reference to the building afterwards, so a regrow,
    /// a zone unload or a second felling cannot reach into it mid-fall.</para>
    /// </summary>
    public sealed class TreeFellFX : MonoBehaviour
    {
        private const float BOUNCE_SECONDS = 0.22f;
        private const float BOUNCE_DEGREES = 7f;
        private const float REST_SECONDS = 0.35f;
        private const float FADE_SECONDS = 0.55f;
        private const float FALL_DEGREES = 88f;

        private Transform _hinge;
        private SpriteRenderer _crown;
        private float _fallSeconds;
        private float _sign;
        private float _t;
        private bool _landed;
        private Color _baseColor;
        private Color _leafColor;
        private float _crownHeight;

        /// <summary>Whether the crown has touched the ground. A test seam.</summary>
        public bool Landed => _landed;

        /// <summary>
        /// Start a fall for this building, or return null when there is no canopy to drop (a
        /// building assembled with no top half, or already hidden).
        /// </summary>
        public static TreeFellFX Spawn(BuildingObject building, DestructionProfile profile, float directionSign)
        {
            if (building == null) return null;
            var canopy = building.CanopyRenderer;
            if (canopy == null || canopy.sprite == null || !canopy.gameObject.activeInHierarchy || !canopy.enabled)
                return null;

            Bounds b = canopy.bounds;
            var root = new GameObject("TreeFall");
            root.transform.position = new Vector3(b.center.x, b.min.y, 0f);

            var fx = root.AddComponent<TreeFellFX>();
            fx.Build(canopy, profile, directionSign);
            return fx;
        }

        private void Build(SpriteRenderer canopy, DestructionProfile profile, float directionSign)
        {
            _hinge = new GameObject("Hinge").transform;
            _hinge.SetParent(transform, false);

            var crownGo = new GameObject("Crown");
            crownGo.transform.SetParent(_hinge, worldPositionStays: false);
            crownGo.transform.position = canopy.transform.position;
            crownGo.transform.localScale = canopy.transform.lossyScale;

            _crown = crownGo.AddComponent<SpriteRenderer>();
            _crown.sprite = canopy.sprite;
            _crown.flipX = canopy.flipX;
            _crown.sharedMaterial = canopy.sharedMaterial;
            _crown.sortingLayerID = canopy.sortingLayerID;
            _crown.sortingOrder = canopy.sortingOrder;
            _crown.color = canopy.color;
            _baseColor = canopy.color;

            _sign = directionSign >= 0f ? 1f : -1f;
            _fallSeconds = profile != null ? Mathf.Max(0.2f, profile.fallSeconds) : 0.9f;
            _leafColor = profile != null ? profile.leafColor : new Color(0.36f, 0.58f, 0.25f, 1f);
            _crownHeight = canopy.bounds.size.y;

            // A creak of leaves from the crown the moment it lets go.
            HarvestFx.Leaves(canopy.bounds.center, _leafColor, 10, canopy.bounds.extents.x * 0.8f);
        }

        private void Update()
        {
            _t += Time.deltaTime;

            if (!_landed)
            {
                float k = Mathf.Clamp01(_t / _fallSeconds);
                SetAngle(FALL_DEGREES * k * k);
                if (k >= 1f) Land();
                return;
            }

            float after = _t - _fallSeconds;
            if (after < BOUNCE_SECONDS)
            {
                float b = after / BOUNCE_SECONDS;
                SetAngle(FALL_DEGREES - Mathf.Sin(b * Mathf.PI) * BOUNCE_DEGREES);
                return;
            }

            SetAngle(FALL_DEGREES);

            float fade = (after - BOUNCE_SECONDS - REST_SECONDS) / FADE_SECONDS;
            if (fade <= 0f) return;

            var c = _baseColor;
            c.a = _baseColor.a * (1f - Mathf.Clamp01(fade));
            _crown.color = c;

            if (fade >= 1f) Destroy(gameObject);
        }

        private void SetAngle(float degrees)
        {
            // Positive sign falls toward +X, which in Unity is a NEGATIVE rotation about Z.
            _hinge.localRotation = Quaternion.Euler(0f, 0f, -_sign * degrees);
        }

        private void Land()
        {
            _landed = true;
            SetAngle(FALL_DEGREES);

            Vector3 impact = transform.position + new Vector3(_sign * _crownHeight * 0.6f, -0.05f, 0f);
            var dust = new Color(0.62f, 0.55f, 0.45f, 0.55f);

            HarvestFx.Dust(impact, dust, 14, _crownHeight * 0.45f);
            HarvestFx.Leaves(impact + Vector3.up * 0.2f, _leafColor, 26, _crownHeight * 0.4f);
            CameraFeel.Cue(CameraFeelCue.ImpactMedium, new Vector2(_sign, 0f));
        }
    }
}
