using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Asserts the SHIPPED data that turns Gatita from four static poses into a shopkeeper
    /// who paces her stall and breathes while she waits.
    ///
    /// <para>Written against the composition rather than either half, which is what CLAUDE.md
    /// prescribes after the spawner coordinate drift: the art can be cut perfectly and the
    /// definition can be filled in perfectly while the two disagree about how many frames a
    /// direction bucket holds, and nothing fails until someone looks at the screen.</para>
    /// </summary>
    public class GatitaAnimationDataTests
    {
        private const string DefinitionPath =
            "Assets/_Project/Data/Catalogs/Monsters/vendor_cheff_gatita.asset";

        private const string ArtRoot =
            "Assets/_Project/Art/NPC/neutral/vendors/cheff/gatita_chanchita";

        private const int IdleFrames = 6;
        private const int WalkFrames = 8;
        private const int Directions = 8;

        /// <summary>The body height every frame is normalised to, matching her old art so
        /// this change moves her without resizing her. Mirrors TARGET_BODY_PX in
        /// <c>tools/atlas/wave6/build_gatita_frames.py</c>.</summary>
        private const int TargetBodyPx = 240;

        private const int NpcPixelsPerUnit = 64;

        private static MonsterDefinition Definition()
        {
            var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(DefinitionPath);
            Assert.IsNotNull(def, $"Gatita's definition is missing at {DefinitionPath}.");
            return def;
        }

        // ---- The frames reached the definition -------------------------------

        [Test]
        public void IdleAndWalk_FillEveryDirectionBucket()
        {
            var config = Definition().assetConfig;

            Assert.AreEqual(IdleFrames * Directions, config.idleSheets.Count,
                "CreateSetFromLinearFrames slices the list into eight CONTIGUOUS " +
                "per-direction buckets — it is not one animation. Her art is a single " +
                "front view and DirectionalAnimator never flips, so every bucket repeats it.");
            Assert.AreEqual(WalkFrames * Directions, config.walkSheets.Count);
        }

        [Test]
        public void EveryDirectionBucket_HoldsTheSameCycle()
        {
            var config = Definition().assetConfig;

            AssertBucketsIdentical(config.idleSheets, IdleFrames, "idle");
            AssertBucketsIdentical(config.walkSheets, WalkFrames, "walk");
        }

        private static void AssertBucketsIdentical(List<Sprite> frames, int perDirection, string state)
        {
            for (int dir = 1; dir < Directions; dir++)
            {
                for (int i = 0; i < perDirection; i++)
                {
                    Assert.AreSame(frames[i], frames[dir * perDirection + i],
                        $"{state} bucket {dir} frame {i} must be the same sprite as bucket 0's. " +
                        "A bucket that drifts means one facing animates differently from the " +
                        "rest, which reads as the animation breaking when the player walks past.");
                }
            }
        }

        [Test]
        public void DirectionLayout_IsExplicitlyEightDirectional()
        {
            Assert.AreEqual(EntitySheetDirectionLayout.EightDirectional,
                Definition().assetConfig.directionLayout,
                "48 and 64 are exactly the frame counts Auto's heuristic is documented as " +
                "being ambiguous about, so the layout is stated rather than inferred.");
        }

        [Test]
        public void StaticPoses_AreCleared()
        {
            var config = Definition().assetConfig;

            Assert.IsNull(config.idle.south,
                "BuildSet prefers `directional` over `sheets`, so a leftover static pose " +
                "would silently win and the animation would never render a frame.");
            Assert.IsNull(config.walk.south);
        }

        // ---- The art on disk agrees with the definition ----------------------

        [Test]
        public void EveryFrame_ExistsOnDiskAndIsGroundAnchored()
        {
            AssertFramesImported("idle", IdleFrames);
            AssertFramesImported("walk", WalkFrames);
        }

        private static void AssertFramesImported(string state, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string path = $"{ArtRoot}/{state}/gatita_{state}_{i}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsNotNull(sprite, $"Missing frame {path}.");

                Assert.AreEqual(NpcPixelsPerUnit, sprite.pixelsPerUnit, 0.001f,
                    "ValkurAssetPostprocessor gives everything under /NPC/ 64 PPU. A frame " +
                    "that missed it renders at a different size from its neighbours.");

                Assert.AreEqual(0f, sprite.pivot.y, 0.5f,
                    $"{path} must pivot on its bottom edge: the cutter anchors every frame " +
                    "on the ground line, and a centred pivot would make her float.");
                Assert.AreEqual(sprite.rect.width / 2f, sprite.pivot.x, 1f,
                    $"{path} must pivot on its horizontal centre, where the cutter put her feet.");
            }
        }

        [Test]
        public void FrameHeight_KeepsHerTheSizeSheAlreadyWas()
        {
            // 240 px of body at 64 PPU is 3.75 world units, and her scaleIdle of 0.3 puts her
            // at 1.125 — the same as the 256x256 static she is replacing. This animation is
            // meant to change how she MOVES and nothing about how big she is, so a re-cut at
            // a different target would silently resize her relative to her own stall.
            float bodyUnits = (float)TargetBodyPx / NpcPixelsPerUnit;
            float scale = Definition().assetConfig.scaleConfig.scaleIdle;

            Assert.AreEqual(1.125f, bodyUnits * scale, 0.001f,
                "Her drawn height must stay where it was before she was animated.");
        }

        // ---- Pacing ----------------------------------------------------------

        [Test]
        public void Idle_IsPacedSlowerThanWalk()
        {
            var config = Definition().assetConfig;
            float idle = config.StateSpeedMultiplier("idle");
            float walk = config.StateSpeedMultiplier("walk");

            Assert.Less(idle, walk,
                "The whole point of the per-state dial here: she breathes slowly and walks " +
                "at a normal stride. Equal values mean the pacing was lost in a re-import.");
            Assert.AreEqual(1f, walk, 0.0001f,
                "Her walk cycle is tuned against her 0.8 u/s speed at the default frame rate; " +
                "pacing it would desync her stride from her travel.");
        }

        [Test]
        public void IdleCycle_ReadsAsABreathRatherThanAFidget()
        {
            var config = Definition().assetConfig;
            const float authoredFrameInterval = 0.15f;

            float cycle = IdleFrames * authoredFrameInterval / config.StateSpeedMultiplier("idle");

            Assert.That(cycle, Is.InRange(1.8f, 3.0f),
                "A six-frame idle at the default rate is a 0.9 s loop, which reads as panting. " +
                "Slower than about three seconds and the steps between frames start to show.");
        }

        // ---- She can actually walk -------------------------------------------

        [Test]
        public void PatrolType_IsTheShortCentredStroll()
        {
            Assert.AreEqual("stroll", Definition().patrolType,
                "The waypoints are no longer a PATH — StrollState picks its own bearing for " +
                "every bout — but they are still the only thing that remembers where she " +
                "was SPAWNED by the time the state runs, and it takes their midpoint as the " +
                "centre of her wander. Empty leaves FSMMonsterBrain skipping waypoint " +
                "generation entirely, and the leash then centres on wherever she happened " +
                "to be standing when the state was first entered.");
        }

        [Test]
        public void Stroll_StaysCentredOnHerStall()
        {
            var origin = new Vector2(12.5f, -7.25f);
            var points = PatrolWaypointGenerator.Generate(origin, "stroll");

            Assert.AreEqual(2, points.Length);

            var mid = (points[0] + points[1]) * 0.5f;
            Assert.AreEqual(origin.x, mid.x, 0.0001f,
                "A vendor patrolling the 5-unit `line` spends the session an average of " +
                "2.5 units off their own stall, and the player finds empty ground.");
            Assert.AreEqual(origin.y, mid.y, 0.0001f);

            Assert.AreEqual(points[0].y, points[1].y, 0.0001f,
                "Horizontal, and it no longer decides how she WALKS — StrollState picks its " +
                "own bearings and biases them away from north itself, because her art has no " +
                "back. What the symmetry still buys is that averaging the points lands on " +
                "the spawn, which is what ResolveHome does with them.");

            float span = Vector2.Distance(points[0], points[1]);
            Assert.That(span, Is.InRange(1.5f, 3.5f),
                "A short pace rather than a route. It sized the old five-second patrol " +
                "window; it now only has to stay small enough that a midpoint taken from it " +
                "means 'her stall' rather than 'somewhere on her beat'.");
        }

        // ---- The FSM lets her, and lets nobody else -------------------------

        [Test]
        public void ShippedFsm_GivesGatitaAWanderingSetOfHerOwn()
        {
            var sets = ReadFsm("sets.json");
            var stroller = FindSet(sets, "NPC_Stroller");

            var states = StateIds(stroller);
            CollectionAssert.Contains(states, "StrollState",
                "StrollState owns the whole idle-walk-idle rhythm internally. A set that " +
                "does not whitelist it cannot enter it at all.");
            Assert.AreEqual("StrollState", stroller["initial"] as string,
                "It is also the set's INITIAL state, and that is not decoration: nothing " +
                "else in the set can reach it, because no state class calls " +
                "ChangeState(new StrollState()).");

            CollectionAssert.DoesNotContain(states, "ChaseState",
                "The whitelist is the ONLY thing that makes a faction peaceful — no state " +
                "class reads stats.faction. A vendor who could enter ChaseState would hunt " +
                "the player the moment anyone raised her aggroRange. (StrollState never " +
                "acquires a target either, so here the whitelist is belt AND braces.)");

            var transitions = stroller["transitions"] as List<object>;
            Assert.AreEqual(0, transitions.Count,
                "This set USED to author Idle-to-Patrol at 240 frames and back at 300, and " +
                "that pair is exactly what made her read as a guard walking a beat: a " +
                "transition's cooldown_frames is ONE constant, so there is nowhere in the " +
                "authored FSM to say 'hold idle for between one and five breaths'. The " +
                "rhythm moved into the state, which can. Re-adding an authored edge here " +
                "would interrupt it mid-bout.");
        }

        [Test]
        public void ShippedFsm_AssignsHerByArchetypeAndLeavesTheOtherVendorsAlone()
        {
            var assignments = ReadFsm("assignments.json");
            var byArchetype = assignments["by_archetype"] as Dictionary<string, object>;

            Assert.AreEqual("NPC_Stroller", byArchetype["vendor_cheff_gatita"] as string,
                "by_archetype is keyed by monsterKey, so this reaches every Gatita and " +
                "nobody else.");

            // by_eid beats by_archetype and looks like the more surgical place for a
            // one-entity override — but it is keyed by an F5 PLACEMENT id, and Gatita is
            // produced by a spawner with no placement id at all. Authored there the entry
            // is silently unreachable: measured live, she kept NPC_Passive's four-state
            // whitelist and zero transitions, and simply stood still.
            var byEid = assignments["by_eid"] as Dictionary<string, object>;
            CollectionAssert.DoesNotContain(byEid.Keys, "vendor_cheff_gatita",
                "A monsterKey under by_eid is not an override, it is a no-op.");

            var passive = StateIds(FindSet(ReadFsm("sets.json"), "NPC_Passive"));
            CollectionAssert.DoesNotContain(passive, "PatrolState",
                "The other six passive NPCs share NPC_Passive and have no walk art. " +
                "Teaching that set to patrol would slide them around on a static pose.");
            CollectionAssert.DoesNotContain(passive, "StrollState",
                "Same reason, and it has to be said separately now: the pacing state was " +
                "renamed, and a check that only names the old one stops covering anything " +
                "the day the new one is added to the wrong set.");

            foreach (var pair in byArchetype)
            {
                if (pair.Key == "vendor_cheff_gatita") continue;
                Assert.AreNotEqual("NPC_Stroller", pair.Value as string,
                    $"'{pair.Key}' was moved onto the pacing set. Only Gatita has walk art.");
            }
        }

        private static Dictionary<string, object> ReadFsm(string file)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "FSM", file);
            Assert.IsTrue(File.Exists(path), $"Missing {path}.");

            var root = Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(File.ReadAllText(path))
                       as Dictionary<string, object>;
            Assert.IsNotNull(root, $"{file} did not parse as an object.");
            return root;
        }

        private static Dictionary<string, object> FindSet(Dictionary<string, object> root, string id)
        {
            var sets = root["sets"] as List<object>;
            foreach (var entry in sets)
            {
                var set = entry as Dictionary<string, object>;
                if (set != null && (set["id"] as string) == id)
                    return set;
            }

            Assert.Fail($"sets.json has no set '{id}'.");
            return null;
        }

        private static List<string> StateIds(Dictionary<string, object> set)
        {
            var ids = new List<string>();
            foreach (var entry in set["states"] as List<object>)
                ids.Add((entry as Dictionary<string, object>)["id"] as string);
            return ids;
        }
    }
}
