#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Monsters
{
    /// <summary>
    /// Measures every drawn frame of every monster and playable character and writes the
    /// capsules of its hurtbox (<see cref="EntityCollisionProfile.hurtShapeFrames"/>) plus the
    /// footprint of its idle stance.
    ///
    /// <para><b>Re-runnable, and it keeps what a human shaped.</b> Derived rows are rewritten on
    /// every run — a tool that cannot follow a re-import is not a tool, and a hurtbox measured
    /// against art that has since moved is exactly the defect it exists to prevent. A row marked
    /// <c>handTuned</c>, and a footprint marked <c>footprintHandTuned</c>, are carried across
    /// untouched: the split the muzzle baker and the crafting importer already use. A hand-tuned
    /// row whose frame no longer exists is dropped with the rest, so an exception cannot outlive
    /// the frame it was for.</para>
    ///
    /// <para><b>Only frames that say which half they are.</b> The profile stores X forward along
    /// the drawn facing, and a frame named <c>_e3</c> / <c>_w3</c> is the only kind that states
    /// its facing. Real 8-direction art without the suffix keeps the automatic capsule, which is
    /// symmetric and therefore right for it whichever way it looks.</para>
    ///
    /// <para>Reads the PNGs straight off disk (the imported textures are not readable, and an
    /// atlas-packed sprite cannot name its file at runtime). <c>EditorUtility.SetDirty</c> plus
    /// <c>SaveAssetIfDirty</c> on what it touched — never <c>Undo.RecordObject</c> (see the
    /// building-template incident) and never <c>AssetDatabase.SaveAssets</c>, which would write
    /// every dirty asset in the project, another session's half-finished edit included.</para>
    /// </summary>
    public static class EntityCollisionBaker
    {
        [MenuItem("Valkur/Entities/Bake Collision Shapes (Dry Run)")]
        public static void DryRun() => Run(apply: false);

        [MenuItem("Valkur/Entities/Bake Collision Shapes")]
        public static void Apply() => Run(apply: true);

        /// <summary>Summary of one run, for the menu log and for a probe that wants the numbers.</summary>
        public struct Report
        {
            public int Entities, Frames, Unresolved, KeptHandTuned;
            public string Text;
        }

        public static Report Run(bool apply)
        {
            var report = new Report();
            var sb = new StringBuilder();
            var cache = new SpriteAlphaReader();

            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:MonsterDefinition"))
                {
                    var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (def == null || def.assetConfig == null) continue;
                    BakeOne(def, def.monsterKey, def.assetConfig, apply, cache, sb, ref report);
                }

                foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition"))
                {
                    var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (def == null || def.assetConfig == null) continue;
                    BakeOne(def, def.playerKey, def.assetConfig, apply, cache, sb, ref report);
                }
            }
            finally
            {
                cache.Clear();
            }

            report.Text = sb.ToString();
            Debug.Log($"[EntityCollisionBaker] {(apply ? "Applied" : "Dry run")}: {report.Entities} entities, " +
                      $"{report.Frames} frames, {report.KeptHandTuned} hand-tuned kept, " +
                      $"{report.Unresolved} unresolved.\n{report.Text}");
            return report;
        }

        private static void BakeOne(ScriptableObject owner, string key, EntityAssetConfig config, bool apply,
                                    SpriteAlphaReader cache, StringBuilder sb, ref Report report)
        {
            var profile = config.collision ?? new EntityCollisionProfile();
            var measured = new List<HurtShapeFrame>();
            int unresolved = 0;
            Sprite idle = FirstIdleSprite(config);
            Vector2 footprint = Vector2.zero;

            foreach (var sprite in CollectSprites(owner))
            {
                if (!CastMuzzleBaker.TryReadHalf(sprite.name, out bool facesEast)) continue;
                if (!cache.TryRead(sprite, out byte[] alpha, out int w, out int h))
                {
                    unresolved++;
                    continue;
                }

                if (!HurtShapeFitter.TryFit(alpha, w, h, facesEast, out var fit))
                {
                    unresolved++;
                    continue;
                }

                measured.Add(new HurtShapeFrame { frame = sprite.name, shapes = fit.Shapes });

                if (sprite == idle)
                {
                    float ppu = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
                    float worldW = sprite.rect.width / ppu, worldH = sprite.rect.height / ppu;
                    footprint = FootprintFrom(fit, worldW, worldH);
                }
            }

            if (measured.Count == 0 && unresolved == 0) return;

            measured.Sort((a, b) => string.CompareOrdinal(a.frame, b.frame));
            int kept = KeepHandTuned(profile.hurtShapeFrames, measured);

            report.Entities++;
            report.Frames += measured.Count;
            report.Unresolved += unresolved;
            report.KeptHandTuned += kept;

            int shapes = 0;
            foreach (var row in measured) shapes += row.shapes.Count;
            sb.Append(key).Append(": ").Append(measured.Count).Append(" frames, ")
              .Append(measured.Count > 0 ? (shapes / (float)measured.Count).ToString("0.0") : "0")
              .Append(" capsules/frame");
            if (footprint != Vector2.zero)
                sb.Append(", footprint ").Append(footprint.x.ToString("0.00")).Append(" x ").Append(footprint.y.ToString("0.00"));
            if (profile.footprintHandTuned) sb.Append(" (footprint hand-tuned, kept)");
            if (kept > 0) sb.Append(", ").Append(kept).Append(" hand-tuned kept");
            if (unresolved > 0) sb.Append(", ").Append(unresolved).Append(" unresolved");
            sb.AppendLine();

            if (!apply) return;

            config.collision = profile;
            profile.hurtShapeFrames = measured;
            if (!profile.footprintHandTuned && footprint != Vector2.zero)
            {
                profile.footprintSize = footprint;
                profile.footprintOffset = Vector2.zero;
            }
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
        }

        /// <summary>
        /// The footprint of a stance: the trimmed width of the ink at the feet, a little inside it
        /// (the footprint is what collides, and a shoulder-width footprint wedges a crowd in a
        /// doorway), with a depth under half of that — capped by the body's own height so a long
        /// quadruped's footprint does not become a deep slab.
        /// </summary>
        internal static Vector2 FootprintFrom(HurtShapeFitter.Fit fit, float frameWorldW, float frameWorldH)
        {
            float inkW = fit.InkWidthFraction * frameWorldW;
            float inkH = fit.InkHeightFraction * frameWorldH;
            float feet = fit.FootWidthFraction * frameWorldW * 0.8f;
            float auto = EntityCollisionProfile.AutoFootprintSize(inkW, inkH).x;
            // An UPRIGHT body stands on roughly its own core width: a robe hem, a planted staff or
            // a wide stance measure as feet and would wedge the creature in a doorway (measured:
            // the mague came out at 1.23 u against a 0.58 u automatic). A LONG body really does
            // stand on legs spread along its length, so the dragon keeps what its legs measure.
            bool longBody = inkW > inkH * 1.3f;
            float cap = longBody ? inkW * 0.5f : auto * 1.3f;
            float width = Mathf.Clamp(Mathf.Max(feet, auto * 0.75f), 0.25f, Mathf.Max(0.3f, cap));
            width = Mathf.Min(width, 4f);
            float depth = Mathf.Clamp(width * EntityCollisionProfile.AutoFootprintDepthRatio, 0.16f, Mathf.Max(0.2f, inkH * 0.3f));
            return new Vector2(width, depth);
        }

        private static int KeepHandTuned(List<HurtShapeFrame> existing, List<HurtShapeFrame> measured)
        {
            if (existing == null || existing.Count == 0) return 0;
            var authored = new Dictionary<string, HurtShapeFrame>();
            foreach (var row in existing)
                if (row != null && row.handTuned && !string.IsNullOrEmpty(row.frame))
                    authored[row.frame] = row;
            if (authored.Count == 0) return 0;

            int kept = 0;
            for (int i = 0; i < measured.Count; i++)
            {
                if (!authored.TryGetValue(measured[i].frame, out var row)) continue;
                measured[i] = row;
                kept++;
            }
            return kept;
        }

        private static Sprite FirstIdleSprite(EntityAssetConfig config)
        {
            if (config.idleSheets != null)
                foreach (var s in config.idleSheets)
                    if (s != null && CastMuzzleBaker.TryReadHalf(s.name, out _)) return s;
            return null;
        }

        /// <summary>
        /// Every distinct sprite the definition references, walked through the SERIALIZED tree so
        /// a slot added to <see cref="EntityAssetConfig"/> next month is covered without anybody
        /// remembering this list.
        /// </summary>
        private static List<Sprite> CollectSprites(ScriptableObject owner)
        {
            var result = new List<Sprite>();
            var seen = new HashSet<string>();
            var so = new SerializedObject(owner);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(it.objectReferenceValue is Sprite sprite)) continue;
                if (seen.Add(sprite.name)) result.Add(sprite);
            }
            return result;
        }
    }
}
#endif
