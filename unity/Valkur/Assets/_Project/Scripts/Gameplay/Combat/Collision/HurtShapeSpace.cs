using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The ONE conversion from a <see cref="HurtShape"/> (fractions of a frame) to the world,
    /// shared by the live hurtbox, the collision overlay and the Entities editor's stage.
    ///
    /// <para><b>Measured against the sprite's RECT and PIVOT, never against
    /// <c>SpriteRenderer.bounds</c>.</b> Bounds are the MESH's extents, and a sprite imported
    /// with a tight mesh reports a box smaller than the picture; the baker measures the whole
    /// rect out of the PNG, so the runtime has to place against that same rect or every capsule
    /// on a tight-mesh sprite lands shrunk towards its middle. Rect and pivot also survive atlas
    /// packing unchanged, which <c>textureRect</c> does not.</para>
    ///
    /// <para>The facing sign is <see cref="CastMuzzle.FacingSignFor"/>, the rule the muzzle
    /// already uses, so a hurtbox and the spell leaving the same mouth cannot disagree about
    /// which way the creature is drawn.</para>
    /// </summary>
    public static class HurtShapeSpace
    {
        /// <summary>The frame's rect in the renderer's LOCAL space (sprite units, pivot at the origin).</summary>
        public static Rect LocalRect(Sprite sprite)
        {
            if (sprite == null) return new Rect(-0.5f, 0f, 1f, 1f);
            float ppu = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
            Rect r = sprite.rect;
            return new Rect(-sprite.pivot.x / ppu, -sprite.pivot.y / ppu, r.width / ppu, r.height / ppu);
        }

        /// <summary>+1 when the frame is drawn facing east, -1 west, honouring a flipped renderer.</summary>
        public static float FacingSign(SpriteRenderer renderer, DirectionalAnimator animator)
        {
            if (renderer == null) return 1f;
            float sign = CastMuzzle.FacingSignFor(renderer.sprite,
                animator != null ? animator.CurrentDirection : DirectionalAnimator.Direction.East);
            return renderer.flipX ? -sign : sign;
        }

        /// <summary>
        /// A shape's centre and size in the renderer's local space. <paramref name="scale"/>
        /// multiplies the size only (the profile's forgiveness).
        /// </summary>
        public static void ToLocal(Sprite sprite, float facingSign, HurtShape shape, float scale,
                                   out Vector2 center, out Vector2 size)
        {
            Rect r = LocalRect(sprite);
            Vector2 half = r.size * 0.5f;
            center = new Vector2(r.center.x + facingSign * half.x * shape.center.x,
                                 r.center.y + half.y * shape.center.y);
            size = new Vector2(Mathf.Abs(shape.size.x) * r.width * scale,
                               Mathf.Abs(shape.size.y) * r.height * scale);
        }

        /// <summary>A shape's centre and size in WORLD space for a renderer on screen.</summary>
        public static void ToWorld(SpriteRenderer renderer, float facingSign, HurtShape shape, float scale,
                                   out Vector2 center, out Vector2 size)
        {
            ToLocal(renderer.sprite, facingSign, shape, scale, out Vector2 lc, out Vector2 ls);
            Transform t = renderer.transform;
            center = t.TransformPoint(lc);
            Vector3 lossy = t.lossyScale;
            size = new Vector2(ls.x * Mathf.Abs(lossy.x), ls.y * Mathf.Abs(lossy.y));
        }

        /// <summary>
        /// The inverse, for the editor: a WORLD point on the frame back to the shape's centre
        /// space. Size is converted separately by <see cref="WorldSizeToShape"/>.
        /// </summary>
        public static Vector2 WorldToShapeCenter(SpriteRenderer renderer, float facingSign, Vector2 world)
        {
            Rect r = LocalRect(renderer.sprite);
            Vector2 local = renderer.transform.InverseTransformPoint(world);
            Vector2 half = new Vector2(Mathf.Max(0.0001f, r.width * 0.5f), Mathf.Max(0.0001f, r.height * 0.5f));
            float s = Mathf.Approximately(facingSign, 0f) ? 1f : Mathf.Sign(facingSign);
            return new Vector2((local.x - r.center.x) / half.x * s, (local.y - r.center.y) / half.y);
        }

        /// <summary>A WORLD size on the frame back to the shape's fraction-of-rect space.</summary>
        public static Vector2 WorldSizeToShape(SpriteRenderer renderer, Vector2 worldSize)
        {
            Rect r = LocalRect(renderer.sprite);
            Vector3 lossy = renderer.transform.lossyScale;
            float w = Mathf.Max(0.0001f, r.width * Mathf.Abs(lossy.x));
            float h = Mathf.Max(0.0001f, r.height * Mathf.Abs(lossy.y));
            return new Vector2(Mathf.Abs(worldSize.x) / w, Mathf.Abs(worldSize.y) / h);
        }

        /// <summary>
        /// The capsule's own outline as a closed loop, for overlays that draw a shape with no
        /// collider behind it (the editor stage). Runs along the longer side, like
        /// <see cref="CapsuleCollider2D"/> does when its direction is chosen from its size.
        /// </summary>
        public static void AppendCapsuleLoop(Vector2 center, Vector2 size, System.Collections.Generic.List<Vector2> points,
                                             int segmentsPerCap = 10)
        {
            bool vertical = size.y >= size.x;
            float radius = (vertical ? size.x : size.y) * 0.5f;
            float halfLength = Mathf.Max(0f, (vertical ? size.y : size.x) * 0.5f - radius);
            Vector2 axis = vertical ? Vector2.up : Vector2.right;
            Vector2 side = vertical ? Vector2.right : Vector2.down;
            Vector2 a = center + axis * halfLength;
            Vector2 b = center - axis * halfLength;

            float baseAngle = Mathf.Atan2(side.y, side.x);
            for (int i = 0; i <= segmentsPerCap; i++)
            {
                float ang = baseAngle + Mathf.PI * i / segmentsPerCap;
                points.Add(a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
            }
            for (int i = 0; i <= segmentsPerCap; i++)
            {
                float ang = baseAngle + Mathf.PI + Mathf.PI * i / segmentsPerCap;
                points.Add(b + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
            }
        }
    }
}
