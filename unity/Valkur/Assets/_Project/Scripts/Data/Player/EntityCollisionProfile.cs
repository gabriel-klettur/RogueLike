using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One capsule of an entity's HURTBOX: the part of its body a blow, a spell or an arrow
    /// can land on.
    ///
    /// <para>Expressed in the space <see cref="CastMuzzleFrame"/> already uses, for the same
    /// reasons: <b>X is FORWARD along the drawn facing</b> (the runtime supplies the sign from
    /// which half is rendered, so the two mirrored halves share one number) and <b>Y is height
    /// above the sprite's vertical centre</b>, both as fractions of the CURRENT frame's own
    /// half-extents. <see cref="size"/> is a fraction of the frame's FULL extents. A fraction
    /// survives a frame trimmed to its own alpha, a re-export at another resolution and
    /// <c>scaleConfig.scaleIdle</c>; a world distance survives none of them.</para>
    ///
    /// <para>A capsule and not a polygon: a hurtbox that follows every pixel changes shape on
    /// every frame, costs a fixture rebuild each time, and turns a blow that clips a strand of
    /// hair into a hit the player reads as unfair. Two to four capsules follow a silhouette
    /// closely enough to be believed and loosely enough to be fair.</para>
    /// </summary>
    [Serializable]
    public struct HurtShape
    {
        [Tooltip("Centre. X forward along the drawn facing, Y above the sprite's vertical " +
                 "centre, as fractions of the frame's half-extents.")]
        public Vector2 center;

        [Tooltip("Width and height as fractions of the frame's full extents. The capsule runs " +
                 "along whichever of the two is longer.")]
        public Vector2 size;

        public HurtShape(Vector2 center, Vector2 size)
        {
            this.center = center;
            this.size = size;
        }

        /// <summary>Degenerate shapes are authoring debris, never an instruction.</summary>
        public bool IsValid => size.x > 0.0001f && size.y > 0.0001f;
    }

    /// <summary>
    /// The hurtbox of ONE drawn frame, keyed by that frame's sprite name.
    ///
    /// <para>Per frame for the reason the muzzle is: bodies MOVE. A dragon that rears lifts its
    /// head two and a half units, a death animation lies down, a lunge throws the torso a body
    /// width forward. A single hurtbox for the whole creature is right in the idle and wrong in
    /// every pose the fight is actually about.</para>
    /// </summary>
    [Serializable]
    public class HurtShapeFrame
    {
        [Tooltip("Sprite name this row belongs to, e.g. red_dragon_cast_e3.")]
        public string frame;

        public List<HurtShape> shapes = new List<HurtShape>();

        [Tooltip("Set when a human shaped this row in the Entities editor. The baker keeps it " +
                 "and re-measures everything else.")]
        public bool handTuned;
    }

    /// <summary>
    /// Every collision layer an entity carries, as DATA.
    ///
    /// <para><b>An entity has three collision jobs and one collider cannot do all three.</b>
    /// The FOOTPRINT is what stands on the ground: it collides with walls and other bodies and
    /// must not change with the animation, or a creature grows into the wall it is standing
    /// next to on every swing. The HURTBOX is what can be hit: it follows the drawn body frame
    /// by frame. Selection (the mouse) reads the sprite itself. They used to be ONE centred
    /// square half the size of the sprite — so the red dragon, 8.2 units long, could be hit
    /// only inside a 2.3-unit box on its back, and every humanoid collided with walls at chest
    /// height.</para>
    ///
    /// <para>Everything defaults to AUTOMATIC. An entity nobody baked or authored still gets a
    /// footprint at its feet and one capsule over its body, derived from the frame on screen,
    /// which is already strictly better than the square it replaces.</para>
    /// </summary>
    [Serializable]
    public class EntityCollisionProfile
    {
        /// <summary>Automatic footprint width as a fraction of the body's "core" width.</summary>
        public const float AutoFootprintWidthFraction = 0.5f;

        /// <summary>Automatic footprint depth as a fraction of its own width.</summary>
        public const float AutoFootprintDepthRatio = 0.45f;

        /// <summary>
        /// A sprite's core width is its width capped at this fraction of its height. Legacy
        /// art ships in square padded cells and a lunge frame is wider than the body, so the
        /// raw width would size a humanoid's feet like a horse's.
        /// </summary>
        public const float CoreWidthOfHeight = 0.62f;

        /// <summary>Most capsules one frame may carry. Five covers a dragon: jaw, neck, body, haunch, tail.</summary>
        public const int MaxShapesPerFrame = 5;

        [Header("Footprint (feet)")]
        [Tooltip("Footprint size in WORLD units at scale 1 (x across, y depth). (0,0) = " +
                 "automatic, derived from the idle frame. A footprint is deliberately not per " +
                 "frame: walls would push a creature around every time it swung.")]
        public Vector2 footprintSize = Vector2.zero;

        [Tooltip("Footprint centre relative to the pivot (the feet), world units at scale 1.")]
        public Vector2 footprintOffset = Vector2.zero;

        [Tooltip("Set when a human sized the footprint. The baker measures the feet of the idle " +
                 "frame and rewrites the footprint unless this is set.")]
        public bool footprintHandTuned;

        [Header("Hurtbox (body)")]
        [Tooltip("Multiplier on every hurtbox capsule's size. Below 1 is FORGIVENESS: the " +
                 "player's own hurtbox a little smaller than the drawing is what makes a near " +
                 "miss feel like a dodge instead of a robbery.")]
        [Range(0.5f, 1.25f)]
        public float hurtScale = 1f;

        [Tooltip("Creature-wide capsules for any frame without a row of its own. Empty = one " +
                 "automatic capsule over the frame on screen.")]
        public List<HurtShape> hurtShapes = new List<HurtShape>();

        [Tooltip("Per-frame capsules, baked by Valkur > Entities > Bake Collision Shapes and " +
                 "retouched in the Entities editor. A frame named here uses exactly these.")]
        public List<HurtShapeFrame> hurtShapeFrames = new List<HurtShapeFrame>();

        public bool HasAuthoredFootprint => footprintSize.x > 0.0001f && footprintSize.y > 0.0001f;

        /// <summary>
        /// The row for one frame, or null. A linear scan: the runtime component builds its own
        /// dictionary once, and this is what the authoring panel and tests use.
        /// </summary>
        public HurtShapeFrame FindFrame(string frameName)
        {
            if (hurtShapeFrames == null || string.IsNullOrEmpty(frameName)) return null;
            for (int i = 0; i < hurtShapeFrames.Count; i++)
            {
                var row = hurtShapeFrames[i];
                if (row == null) continue;
                // Ordinal: an atlas-packed sprite is identified by its stored name and nothing else.
                if (string.Equals(row.frame, frameName, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        /// <summary>
        /// The capsules that answer for a frame, in resolution order: its own row, then the
        /// creature-wide list, then one automatic capsule. Never empty.
        /// </summary>
        public void ResolveShapes(string frameName, List<HurtShape> result)
        {
            result.Clear();
            var row = FindFrame(frameName);
            if (row != null && AppendValid(row.shapes, result)) return;
            if (AppendValid(hurtShapes, result)) return;
            result.Add(AutoHurtShape());
        }

        /// <summary>
        /// Where the shapes for a frame come from, for a readout that has to say it.
        /// </summary>
        public HurtShapeSource SourceFor(string frameName)
        {
            var row = FindFrame(frameName);
            if (row != null && CountValid(row.shapes) > 0)
                return row.handTuned ? HurtShapeSource.HandTuned : HurtShapeSource.Baked;
            if (CountValid(hurtShapes) > 0) return HurtShapeSource.Creature;
            return HurtShapeSource.Automatic;
        }

        /// <summary>
        /// Writes one frame's row, adding it when absent and REMOVING it when given nothing —
        /// an empty row would shadow the creature-wide shapes with no capsule at all, which is
        /// an entity nothing can hit.
        /// </summary>
        public HurtShapeFrame SetFrameShapes(string frameName, IReadOnlyList<HurtShape> shapes, bool handTuned)
        {
            if (string.IsNullOrEmpty(frameName)) return null;
            hurtShapeFrames ??= new List<HurtShapeFrame>();

            var row = FindFrame(frameName);
            int valid = 0;
            if (shapes != null)
                for (int i = 0; i < shapes.Count; i++) if (shapes[i].IsValid) valid++;

            if (valid == 0)
            {
                if (row != null) hurtShapeFrames.Remove(row);
                return null;
            }

            if (row == null)
            {
                row = new HurtShapeFrame { frame = frameName };
                hurtShapeFrames.Add(row);
            }

            row.shapes ??= new List<HurtShape>();
            row.shapes.Clear();
            for (int i = 0; i < shapes.Count && row.shapes.Count < MaxShapesPerFrame; i++)
                if (shapes[i].IsValid) row.shapes.Add(shapes[i]);
            row.handTuned = handTuned;
            return row;
        }

        /// <summary>
        /// One capsule over the frame's core: centred, most of its height, the core width.
        /// Expressed as fractions of the frame, so it needs no measurement to be right-sized.
        /// </summary>
        public static HurtShape AutoHurtShape() => new HurtShape(new Vector2(0f, -0.04f), new Vector2(0.5f, 0.9f));

        /// <summary>
        /// The automatic footprint for a body of the given WORLD size: the core width halved,
        /// and a depth under half of that. Measured against the shipped player prefab's
        /// hand-tuned 0.5 x 0.3 box: the dwarf (1.22 x 1.86) comes out 0.58 x 0.26.
        /// </summary>
        public static Vector2 AutoFootprintSize(float bodyWidth, float bodyHeight)
        {
            float core = Mathf.Min(Mathf.Max(0f, bodyWidth), Mathf.Max(0f, bodyHeight) * CoreWidthOfHeight);
            float width = Mathf.Clamp(core * AutoFootprintWidthFraction, 0.25f, 3.5f);
            float depth = Mathf.Max(0.16f, width * AutoFootprintDepthRatio);
            return new Vector2(width, depth);
        }

        private static bool AppendValid(List<HurtShape> source, List<HurtShape> result)
        {
            if (source == null) return false;
            int before = result.Count;
            for (int i = 0; i < source.Count && result.Count - before < MaxShapesPerFrame; i++)
                if (source[i].IsValid) result.Add(source[i]);
            return result.Count > before;
        }

        private static int CountValid(List<HurtShape> shapes)
        {
            if (shapes == null) return 0;
            int n = 0;
            for (int i = 0; i < shapes.Count; i++) if (shapes[i].IsValid) n++;
            return n;
        }
    }

    /// <summary>Which layer of the resolution answered for a frame.</summary>
    public enum HurtShapeSource
    {
        Automatic = 0,
        Creature = 1,
        Baked = 2,
        HandTuned = 3,
    }
}
