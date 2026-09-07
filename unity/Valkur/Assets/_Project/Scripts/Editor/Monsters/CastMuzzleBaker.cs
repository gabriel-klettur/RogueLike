#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Monsters
{
    /// <summary>
    /// Measures where each drawn frame's head is and writes it into
    /// <see cref="EntityAssetConfig.castMuzzleFrames"/>, so a creature whose mouth is not
    /// above its feet can emit a spell from the mouth.
    ///
    /// <para><b>Opt-in by authoring <c>castMuzzle</c>.</b> The measurement is "the leading
    /// edge of the silhouette at the head's own height", which is the head on anything drawn
    /// in profile and is a raised axe on the barbarian. There is no way to tell those apart
    /// from pixels, so the entity-wide pair is the declaration that this creature HAS a
    /// muzzle worth finding, and the bake only refines it per frame. Running this over the
    /// whole catalogue would otherwise quietly move every humanoid's casts into their
    /// weapon.</para>
    ///
    /// <para><b>Re-runnable: it re-measures everything and KEEPS the hand-tuned rows.</b>
    /// Derived numbers are always rewritten, or a tool that cannot propagate a re-import is
    /// not a tool — a stale row for art that moved is the failure the per-frame table exists
    /// to avoid. But the measurement is a silhouette heuristic and a few poses defeat it: on
    /// the dragon's <c>cast_e6</c> the foreleg reaches further forward than the snout, so the
    /// automatic answer follows the leg. <c>CastMuzzleFrame.handTuned</c> is the escape
    /// hatch, and it is the same split the crafting importer already uses — the generator
    /// owns what it can measure, the human owns the exceptions. Like every other bulk asset
    /// tool here it uses <c>EditorUtility.SetDirty</c> alone and never
    /// <c>Undo.RecordObject</c> — see the building-template incident in CLAUDE.md.</para>
    /// </summary>
    public static class CastMuzzleBaker
    {
        /// <summary>Alpha above which a pixel counts as part of the creature.</summary>
        private const byte OPAQUE = 32;

        /// <summary>
        /// Slice of the sprite's width, measured back from the leading edge, that the head
        /// is followed through. Wide enough to contain a snout at any of its drawn heights
        /// and narrow enough to stop before the shoulder behind it.
        /// </summary>
        private const float HEAD_WINDOW = 0.07f;

        /// <summary>
        /// How far the ink may jump vertically from one column to the next and still count
        /// as the same body part, as a fraction of height.
        ///
        /// <para>Without it the window's raw vertical span is taken, and on a REARED frame
        /// that is two body parts at once: measured on <c>idle_e2</c>, the leading 45 px
        /// holds the head at rows 3-56 and a hind leg at row 274, so the midpoint of the
        /// span lands at row 138 — the empty air between them. Following the ink back from
        /// the tip instead keeps the head and never reaches the leg.</para>
        /// </summary>
        private const float CONTINUITY_TOLERANCE = 0.08f;

        /// <summary>
        /// How far back off the extreme point the muzzle sits, as a fraction of width. The
        /// extreme point is the tip of the snout (or of a horn on a reared frame); the mouth
        /// is just behind it, and without this the origin reads as fire starting a hair in
        /// front of a closed jaw.
        /// </summary>
        private const float TIP_SETBACK = 0.025f;

        [MenuItem("Valkur/Monsters/Bake Cast Muzzles (Dry Run)")]
        public static void DryRun() => Run(apply: false);

        [MenuItem("Valkur/Monsters/Bake Cast Muzzles")]
        public static void Apply() => Run(apply: true);

        private static void Run(bool apply)
        {
            var report = new StringBuilder();
            int entities = 0, rows = 0, skipped = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:MonsterDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def == null || def.assetConfig == null || !def.assetConfig.HasCastMuzzle) continue;

                var measured = MeasureAll(def, out int unresolved);
                if (measured.Count == 0)
                {
                    report.AppendLine($"{def.monsterKey}: no measurable frames (skipped)");
                    continue;
                }

                entities++;
                rows += measured.Count;
                skipped += unresolved;
                report.AppendLine(
                    $"{def.monsterKey}: {measured.Count} frames, x {measured.Min(m => m.offset.x):0.00}" +
                    $"-{measured.Max(m => m.offset.x):0.00}, y {measured.Min(m => m.offset.y):0.00}" +
                    $"-{measured.Max(m => m.offset.y):0.00}" +
                    (unresolved > 0 ? $" ({unresolved} frames had no readable source)" : ""));

                if (!apply) continue;

                def.assetConfig.castMuzzleFrames = KeepHandTuned(def.assetConfig.castMuzzleFrames, measured);
                EditorUtility.SetDirty(def);
            }

            if (apply)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[CastMuzzleBaker] {(apply ? "Applied" : "Dry run")}: {entities} entities, " +
                      $"{rows} frames, {skipped} unresolved.\n{report}");
        }

        /// <summary>
        /// Every distinct sprite the definition references, measured.
        ///
        /// <para>The sprites are gathered by walking the SERIALIZED tree rather than by
        /// naming the slots. <see cref="EntityAssetConfig"/> has grown a recover slot, attack
        /// variants, cast variants, state variants and loadouts, none of them with this tool
        /// in mind, and a hand-written field list is a second list of them that keeps
        /// compiling and quietly stops covering whatever was added last.</para>
        /// </summary>
        /// <summary>
        /// The fresh measurements, with any row a human placed carried across UNCHANGED.
        ///
        /// <para>A hand-tuned row for a frame the art no longer contains is dropped with
        /// everything else, so the exception cannot outlive the frame it was an exception
        /// for — which is the way a preserved-value rule usually rots.</para>
        /// </summary>
        private static List<CastMuzzleFrame> KeepHandTuned(List<CastMuzzleFrame> existing,
                                                           List<CastMuzzleFrame> measured)
        {
            if (existing == null || existing.Count == 0) return measured;

            var authored = new Dictionary<string, CastMuzzleFrame>();
            foreach (var row in existing)
                if (row != null && row.handTuned && !string.IsNullOrEmpty(row.frame))
                    authored[row.frame] = row;

            if (authored.Count == 0) return measured;

            for (int i = 0; i < measured.Count; i++)
                if (authored.TryGetValue(measured[i].frame, out CastMuzzleFrame kept))
                    measured[i] = kept;

            return measured;
        }

        private static List<CastMuzzleFrame> MeasureAll(MonsterDefinition def, out int unresolved)
        {
            unresolved = 0;
            var seen = new HashSet<string>();
            var result = new List<CastMuzzleFrame>();

            var so = new SerializedObject(def);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(it.objectReferenceValue is Sprite sprite)) continue;
                if (!seen.Add(sprite.name)) continue;

                // Only the profile pipelines, whose frames say which half they are. An entity
                // with real per-direction art has its head drawn where it is looking and does
                // not need — and could not be described by — a mirrored forward fraction.
                if (!TryReadHalf(sprite.name, out bool facesEast)) continue;

                if (!TryLoadSource(sprite, out Texture2D tex))
                {
                    unresolved++;
                    continue;
                }

                if (TryMeasure(tex, facesEast, out Vector2 offset))
                    result.Add(new CastMuzzleFrame { frame = sprite.name, offset = offset });
                else
                    unresolved++;

                Object.DestroyImmediate(tex);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.frame, b.frame));
            return result;
        }

        /// <summary>
        /// Loads the frame's PNG straight off disk into a throwaway texture.
        ///
        /// <para>Two reasons it cannot go through the imported asset.
        /// <c>AssetDatabase.GetAssetPath</c> returns EMPTY for an atlas-packed sprite, and
        /// <c>Art/NPC</c> is packed whole by <c>npc.spriteatlas</c> — so the sprite cannot
        /// name its own file. And an imported texture is not readable unless its importer
        /// says so, which is not a setting a measuring tool should be flipping on the
        /// shipped art. Matching by NAME against the project is the pattern CLAUDE.md
        /// already prescribes for packed sprites.</para>
        /// </summary>
        private static bool TryLoadSource(Sprite sprite, out Texture2D tex)
        {
            tex = null;
            string path = null;

            foreach (string guid in AssetDatabase.FindAssets($"{sprite.name} t:Texture2D"))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(candidate) != sprite.name) continue;
                path = candidate;
                break;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (tex.LoadImage(File.ReadAllBytes(path))) return true;

            Object.DestroyImmediate(tex);
            tex = null;
            return false;
        }

        /// <summary>
        /// The muzzle of one frame, as a FORWARD-positive fraction of that frame's own
        /// half-extents. Mirrors share the number: the sign is supplied at runtime by which
        /// half is being rendered, so <c>_e3</c> and <c>_w3</c> both measure positive here.
        /// </summary>
        internal static bool TryMeasure(Texture2D tex, bool facesEast, out Vector2 offset)
        {
            offset = Vector2.zero;
            int w = tex.width, h = tex.height;
            if (w < 4 || h < 4) return false;

            // GetPixels32 is bottom-up: index 0 is the BOTTOM-left pixel. The sprites import
            // with pivot (0.5, 0), so the bottom row is the ground line and the pivot column
            // is w/2 — which is what makes these fractions mean anything.
            Color32[] px = tex.GetPixels32();

            int lead = -1;
            for (int step = 0; step < w; step++)
            {
                int x = facesEast ? w - 1 - step : step;
                if (ColumnHasInk(px, w, h, x)) { lead = x; break; }
            }
            if (lead < 0) return false;

            // Walk back from the tip, one column at a time, keeping only ink that is
            // vertically CONTINUOUS with what the previous column held. See
            // CONTINUITY_TOLERANCE: taking the window's raw span instead merges the head
            // with whatever else happens to reach the leading edge.
            if (!SpanAt(px, w, h, lead, int.MinValue, int.MaxValue, out int low, out int high))
                return false;

            int window = Mathf.Max(1, Mathf.RoundToInt(w * HEAD_WINDOW));
            int tol = Mathf.Max(1, Mathf.RoundToInt(h * CONTINUITY_TOLERANCE));
            int back = facesEast ? -1 : 1;
            for (int k = 1; k < window; k++)
            {
                int x = lead + back * k;
                if (x < 0 || x >= w) break;
                if (!SpanAt(px, w, h, x, low - tol, high + tol, out int lo, out int hi)) break;
                low = Mathf.Min(low, lo);
                high = Mathf.Max(high, hi);
            }

            float setback = w * TIP_SETBACK;
            float muzzleX = facesEast ? lead - setback : lead + setback;
            float muzzleY = (low + high) * 0.5f;

            float halfW = w * 0.5f, halfH = h * 0.5f;
            offset = new Vector2(
                Mathf.Abs(muzzleX - halfW) / halfW,
                (muzzleY - halfH) / halfH);
            return true;
        }

        /// <summary>
        /// The vertical extent of the ink in one column, restricted to rows between
        /// <paramref name="minRow"/> and <paramref name="maxRow"/>. False when that column
        /// holds nothing in range, which is how the walk knows the body part has ended.
        /// </summary>
        private static bool SpanAt(Color32[] px, int w, int h, int x, int minRow, int maxRow,
                                   out int low, out int high)
        {
            low = int.MaxValue;
            high = int.MinValue;
            int from = Mathf.Max(0, minRow), to = Mathf.Min(h - 1, maxRow);
            for (int y = from; y <= to; y++)
            {
                if (px[y * w + x].a <= OPAQUE) continue;
                if (y < low) low = y;
                if (y > high) high = y;
            }
            return low <= high;
        }

        private static bool ColumnHasInk(Color32[] px, int w, int h, int x)
        {
            for (int y = 0; y < h; y++)
                if (px[y * w + x].a > OPAQUE) return true;
            return false;
        }

        /// <summary>
        /// Which half a frame belongs to, from its <c>_e&lt;n&gt;</c> / <c>_w&lt;n&gt;</c>
        /// suffix. The runtime twin is <c>CastMuzzle.TryReadSuffix</c>; both read the name
        /// because the bucket-to-half mapping is a per-pipeline fact — the player pipeline
        /// puts S and N on the east half and the wave13 monster pipeline puts them on the
        /// west — so a table would be right for one and silently wrong for the other.
        /// </summary>
        internal static bool TryReadHalf(string spriteName, out bool facesEast)
        {
            facesEast = true;
            if (string.IsNullOrEmpty(spriteName)) return false;

            int i = spriteName.Length - 1;
            if (!char.IsDigit(spriteName[i])) return false;
            while (i >= 0 && char.IsDigit(spriteName[i])) i--;
            if (i <= 0 || spriteName[i - 1] != '_') return false;

            char half = char.ToLowerInvariant(spriteName[i]);
            if (half == 'e') { facesEast = true; return true; }
            if (half == 'w') { facesEast = false; return true; }
            return false;
        }
    }
}
#endif
