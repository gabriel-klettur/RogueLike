using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Gameplay.Player
{
    /// <summary>
    /// Where a spell leaves a creature that is not shaped like a person.
    ///
    /// <para><b>The shared anchor cannot answer for the red dragon, and the reason is an
    /// axis.</b> <see cref="SpellCastAnchor"/> is a signed fraction of the caster's
    /// half-HEIGHT — Feet, Center, Hands, Head — plus a clearance along the aim. The dragon
    /// is <b>8.23 x 4.55 world units</b> and its mouth is three and a half units in FRONT of
    /// its pivot, so the error is horizontal and every value of a vertical enum is equally
    /// wrong: measured before this existed, its breath was born at (+0.50, 3.29) against a
    /// mouth at (±3.5, 2.4) — out of the middle of its own back.</para>
    ///
    /// <para><b>Three things here would each look like a working implementation on their own
    /// and are the ones that can silently stop being true.</b> That the muzzle only claims
    /// Hands and Head, because <c>Center</c> is an EXPLICIT request for the body that
    /// <c>thunderclap</c> depends on. That the two mirrored halves carry the SAME number,
    /// because the sign comes from which half is drawn and a table that stored signed values
    /// would be right until somebody re-baked. And that a monster which never opted in gains
    /// no component at all, because the measurement ("the leading edge of the silhouette at
    /// the head's own height") is a raised axe on a barbarian.</para>
    /// </summary>
    public class CastMuzzleTests
    {
        private const string DragonPath = "Assets/_Project/Data/Catalogs/Monsters/red_dragon.asset";
        private const string BarbolPath = "Assets/_Project/Data/Catalogs/Monsters/barbol.asset";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static MonsterDefinition Load(string path)
        {
            var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
            Assert.That(def, Is.Not.Null, $"{path} is missing.");
            return def;
        }

        /// <summary>
        /// Builds the entity the way the game does — through the binder — rather than adding
        /// a <see cref="CastMuzzle"/> by hand. Installing the component is half of what is
        /// under test: a correct component nobody attaches is the shape this project has
        /// shipped more than once.
        /// </summary>
        private GameObject Bind(MonsterDefinition def)
        {
            var go = new GameObject(def.monsterKey + "_probe");
            go.transform.position = new Vector3(10f, 5f, 0f);
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<DirectionalAnimator>();
            _spawned.Add(go);

            Assert.That(EntityAnimationBinder.ApplyMonsterVisuals(go, def), Is.True,
                $"{def.monsterKey} failed to bind — the rest of this fixture would measure nothing.");
            return go;
        }

        /// <summary>
        /// Puts one named frame on the renderer, which is what <see cref="CastMuzzle"/>
        /// actually reads.
        ///
        /// <para><b>Set directly rather than through <c>DirectionalAnimator.SetState</c>,
        /// and the reason is an EditMode limit rather than a shortcut.</b> That method does
        /// apply its frame immediately — it calls <c>AdvanceFrame</c> on a state change on
        /// purpose, so a new state is visible without frame-interval lag — but the animator
        /// caches its <c>SpriteRenderer</c> in <c>Awake</c>, and Unity never calls Awake on
        /// a component added in Edit Mode. So the write lands on a null reference and the
        /// renderer keeps whatever the binder seeded, which is the idle set's FIRST bucket:
        /// South, and on this pipeline South is drawn from the WEST half. That is not a
        /// hypothetical — this fixture first shipped driving the animator and measured
        /// -4.67 for a caster it had just faced east, which is exactly
        /// <c>idle_w0</c>'s 0.938 times the idle frame's 4.984 half-width, i.e. a real
        /// number about the wrong frame.</para>
        ///
        /// <para>Setting the sprite is also the more honest test: the production contract is
        /// "the muzzle belongs to the frame being DRAWN", so naming the frame is stating the
        /// thing under test instead of hoping an animator reaches it.</para>
        /// </summary>
        private static Sprite Show(GameObject go, EntityAssetConfig cfg, string frameName)
        {
            Sprite found = cfg.castSheets?.FirstOrDefault(s => s != null && s.name == frameName);
            Assert.That(found, Is.Not.Null,
                $"{frameName} is not among the dragon's cast sheets — the fixture is naming a " +
                "frame the art no longer contains.");

            go.GetComponent<SpriteRenderer>().sprite = found;
            return found;
        }

        [Test]
        public void Dragon_DeclaresAMuzzle_AndItIsBakedPerFrame()
        {
            var cfg = Load(DragonPath).assetConfig;

            Assert.That(cfg.HasCastMuzzle, Is.True,
                "red_dragon must declare castMuzzle: it is BOTH the fallback for a frame the " +
                "bake did not cover AND the opt-in that lets CastMuzzleBaker measure this " +
                "creature at all.");

            Assert.That(cfg.castMuzzleFrames, Is.Not.Null.And.Count.GreaterThan(0),
                "The per-frame table is empty. A single pair lands on the mouth in four of " +
                "the eight cast frames and in open air in the other four, because rearing " +
                "moves the mouth 1.3 units forward and 2.7 up.");

            // The states a cast can actually play. Idle and walk are measured too and cost
            // nothing, but these are the ones a spell is emitted during.
            foreach (string state in new[] { "cast", "attack" })
            {
                int rows = cfg.castMuzzleFrames.Count(f => f.frame.Contains("_" + state + "_"));
                Assert.That(rows, Is.GreaterThan(0),
                    $"No baked muzzle for any '{state}' frame — that state would silently fall " +
                    "back to the entity-wide pair, which is the case this table exists to replace.");
            }
        }

        [Test]
        public void EveryBakedOffset_IsForwardPositive_AndInsideTheSprite()
        {
            var cfg = Load(DragonPath).assetConfig;

            foreach (var f in cfg.castMuzzleFrames)
            {
                // Forward-POSITIVE is the storage contract: the sign is supplied at runtime
                // by which mirrored half is being rendered, so a negative here would be a
                // muzzle at the back of the creature on one facing and inside it on the other.
                Assert.That(f.offset.x, Is.GreaterThan(0f),
                    $"{f.frame} stores a non-positive forward fraction.");

                // A fraction of the frame's own half-extents. Anything past 1 is outside the
                // drawn sprite, i.e. a mouth in empty space.
                Assert.That(f.offset.x, Is.LessThanOrEqualTo(1f), $"{f.frame} x is outside the sprite.");
                Assert.That(Mathf.Abs(f.offset.y), Is.LessThanOrEqualTo(1f),
                    $"{f.frame} y is outside the sprite.");
            }
        }

        [Test]
        public void MirroredHalves_ShareOneMeasurement()
        {
            var cfg = Load(DragonPath).assetConfig;
            var byName = cfg.castMuzzleFrames.ToDictionary(f => f.frame, f => f.offset);

            int compared = 0;
            foreach (var kv in byName)
            {
                if (!kv.Key.Contains("_e")) continue;
                string west = ReplaceLastHalf(kv.Key, 'w');
                if (!byName.TryGetValue(west, out Vector2 other)) continue;

                compared++;
                Assert.That(other.x, Is.EqualTo(kv.Value.x).Within(0.02f),
                    $"{kv.Key} and {west} disagree on the forward fraction. They are the same " +
                    "drawing mirrored, so one number serves both — a signed table would be " +
                    "correct until the next bake and wrong after it.");
                Assert.That(other.y, Is.EqualTo(kv.Value.y).Within(0.02f),
                    $"{kv.Key} and {west} disagree on height.");
            }

            Assert.That(compared, Is.GreaterThan(0),
                "No east/west pair was compared — this test would pass on an empty table.");
        }

        private static string ReplaceLastHalf(string name, char half)
        {
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            return i < 0 ? name : name.Substring(0, i) + half + name.Substring(i + 1);
        }

        [Test]
        public void Muzzle_MovesHandsAndHead_AndLeavesFeetAndCenterAlone()
        {
            var def = Load(DragonPath);
            var go = Bind(def);
            Show(go, def.assetConfig, "red_dragon_cast_e3");

            Vector3 body = go.transform.position;
            var sr = go.GetComponent<SpriteRenderer>();

            Vector3 hands = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Hands);
            Vector3 head = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Head);
            Vector3 feet = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Feet);
            Vector3 center = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Center);

            Assert.That(hands.x - body.x, Is.GreaterThan(2f),
                "Hands must resolve to the muzzle, well forward of the pivot. Measured on the " +
                "shipped art it lands +3.65 units out; the old anchor path put it at +0.00 and " +
                "the clearance alone carried it to +0.50, which is the dragon's shoulder.");
            Assert.That(head, Is.EqualTo(hands),
                "Head means the same thing as Hands once a muzzle answers: both are 'out of " +
                "the upper body', which is the phrase a muzzle exists to say precisely.");

            Assert.That(feet.x, Is.EqualTo(body.x).Within(0.001f),
                "Feet is an explicit request for the body and must not be moved.");
            Assert.That(center.x, Is.EqualTo(sr.bounds.center.x).Within(0.001f),
                "Center is what thunderclap authors so its ring lands AROUND the caster. A " +
                "muzzle moving it would put the ring three units in front of the dragon and " +
                "out of the fight it is standing in.");
        }

        [Test]
        public void MuzzleFlips_WithTheDrawnHalf_NotWithTheAim()
        {
            var def = Load(DragonPath);
            var go = Bind(def);
            Vector3 body = go.transform.position;

            Show(go, def.assetConfig, "red_dragon_cast_e3");
            float east = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Hands).x - body.x;

            Show(go, def.assetConfig, "red_dragon_cast_w3");
            float west = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Hands).x - body.x;

            // A pixel at PPU 64. The two halves are the same drawing mirrored, so the muzzle
            // must be the same distance out on both and only the sign may differ — but they
            // are two SEPARATE baked PNGs, each trimmed to its own alpha, so their widths can
            // disagree by a pixel and the measured fractions with them. Measured on cast_e3
            // against cast_w3: 3.6457 and 3.6301, a gap of 0.0156 units, which is one pixel
            // exactly. Tightening this below a pixel would fail a correct bake.
            const float OnePixel = 1f / 64f;
            Assert.That(Mathf.Abs(east), Is.EqualTo(Mathf.Abs(west)).Within(2f * OnePixel),
                "The two halves are the same drawing mirrored, so the muzzle must be the " +
                "same distance out on both — only the sign may differ.");

            Assert.That(east, Is.GreaterThan(0f), "Facing east, the mouth is to the +X side.");
            Assert.That(west, Is.LessThan(0f),
                "Facing west, the mouth must be on the other side. The animator never flips a " +
                "sprite — the mirrors are baked — so the sign comes from which half is drawn, " +
                "read off the frame's own name rather than from a direction table: the player " +
                "pipeline puts S and N on the east half and the wave13 monster pipeline puts " +
                "them on the west, so a table would be right for one and silent about the other.");
        }

        [Test]
        public void AMonsterThatNeverOptedIn_GainsNoMuzzleAndMovesNothing()
        {
            var go = Bind(Load(BarbolPath));
            var sr = go.GetComponent<SpriteRenderer>();

            Assert.That(go.GetComponent<CastMuzzle>(), Is.Null,
                "barbol declares no castMuzzle, so the binder must attach NOTHING — the " +
                "unauthored case is a null check rather than a present component that has to " +
                "be asked whether it means anything.");

            Vector3 hands = ProjectileExecutor.ResolveCastOrigin(go.transform, SpellCastAnchor.Hands);
            Assert.That(hands.x, Is.EqualTo(sr.bounds.center.x).Within(0.001f),
                "Every entity without a muzzle must resolve exactly where it always did.");
        }

        /// <summary>
        /// The dragon's idle read as DANCING, and the cause is arithmetic rather than taste:
        /// six frames at the shared 0.15 s interval is a 0.9 s loop, which on a 4.55-unit
        /// creature is a twitch. Pinned as a RELATION — the idle is paced slower than the
        /// walk — rather than as the literal 0.35, so retuning the number stays a data edit
        /// and only losing the distinction is a red test. It is the same dial and the same
        /// argument as Gatita's breath: the entity-wide multiplier can only move every state
        /// together, and a slow idle on this creature must not become a wading walk.
        /// </summary>
        [Test]
        public void DragonIdle_IsPacedSlowerThanItsWalk()
        {
            var cfg = Load(DragonPath).assetConfig;

            float idle = cfg.StateSpeedMultiplier("idle");
            float walk = cfg.StateSpeedMultiplier("walk");

            Assert.That(idle, Is.LessThan(walk),
                $"idle {idle:0.00} is not slower than walk {walk:0.00}. Six idle frames at the " +
                "default rate is a 0.9 s loop and reads as the dragon fidgeting.");
            Assert.That(idle, Is.GreaterThan(0.15f),
                $"idle {idle:0.00} is slow enough to read as a frozen sprite rather than a breath.");
        }
    }
}
