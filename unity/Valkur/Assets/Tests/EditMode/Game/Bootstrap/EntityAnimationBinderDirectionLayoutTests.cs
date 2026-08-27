using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Pins <see cref="EntityAssetConfig.directionLayout"/> — the explicit alternative to
    /// <c>EntityAnimationBinder</c>'s old frame-count-only heuristic
    /// (<c>count % 4 == 0 &amp;&amp; count / 8 &lt; 3 &amp;&amp; count / 4 &gt;= 3</c>), which cannot
    /// distinguish a genuine 16-frame 4x4 sheet from an 8x2 one and silently assumes an
    /// undocumented South, West, East, North strip order with no way to opt out.
    ///
    /// A 4-directional <c>DirectionalSpriteSet</c> is distinguishable from an 8-directional
    /// one without inspecting sprite names: <c>TryBuildFourDirectionalSet</c> assigns the
    /// SAME array reference to <c>east</c>/<c>southEast</c>/<c>northEast</c> (each
    /// intercardinal mirrors its nearest cardinal), while the 8-directional builder gives
    /// every bucket its own array. <c>ReferenceEquals(set.east, set.northEast)</c> is
    /// therefore a precise, name-independent probe for which layout actually ran.
    /// </summary>
    public class EntityAnimationBinderDirectionLayoutTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private List<Sprite> CreateFrames(int count)
        {
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        private DirectionalAnimator Bind(EntityAssetConfig config)
        {
            var go = new GameObject("BinderTarget");
            _created.Add(go);
            go.AddComponent<SpriteRenderer>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _created.Add(def);
            def.assetConfig = config;

            Assert.IsTrue(EntityAnimationBinder.ApplyMonsterVisuals(go, def),
                "ApplyMonsterVisuals refused a config with a non-empty idleSheets list.");

            return go.GetComponent<DirectionalAnimator>();
        }

        private static bool IsFourDirectionalLayout(DirectionalAnimator.DirectionalSpriteSet set)
            => ReferenceEquals(set.east, set.northEast) && ReferenceEquals(set.east, set.southEast);

        // ---- Auto reproduces the shipped heuristic, unchanged ------------------

        [Test]
        public void Auto_SixteenFrames_ResolvesFourDirectional()
        {
            // The shipped convention this heuristic exists to preserve: 16 frames
            // (4 dirs x 4 frames) is the canonical imported player sheet shape.
            var config = new EntityAssetConfig { idleSheets = CreateFrames(16) };
            // directionLayout defaults to Auto (0) via the field initializer.

            var animator = Bind(config);

            Assert.IsTrue(IsFourDirectionalLayout(animator.IdleSprites),
                "Auto must still resolve a 16-frame sheet to the 4-directional layout — " +
                "this is the exact convention every shipped player/monster sheet relies on.");
            Assert.AreEqual(4, animator.IdleSprites.south.Length);
        }

        [Test]
        public void Auto_TwentyFourFrames_ResolvesEightDirectional()
        {
            // 24 / 8 = 3, which is NOT < 3, so the historical heuristic falls through to
            // the 8-directional path. Pinned here so a change to the heuristic's
            // thresholds is caught even though nothing calls it directly anymore.
            var config = new EntityAssetConfig { idleSheets = CreateFrames(24) };

            var animator = Bind(config);

            Assert.IsFalse(IsFourDirectionalLayout(animator.IdleSprites));
            Assert.AreEqual(3, animator.IdleSprites.south.Length);
        }

        // ---- Explicit layout overrides the heuristic ---------------------------

        [Test]
        public void ExplicitEightDirectional_OverridesTheHeuristic_OnAFrameCountAutoWouldTreatAsFourDir()
        {
            // 16 frames alone would Auto-resolve to 4-directional (see above). An explicit
            // override must win regardless.
            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames(16),
                directionLayout = EntitySheetDirectionLayout.EightDirectional,
            };

            var animator = Bind(config);

            Assert.IsFalse(IsFourDirectionalLayout(animator.IdleSprites),
                "An explicit EightDirectional layout must not be second-guessed by the " +
                "frame-count heuristic.");
            Assert.AreEqual(2, animator.IdleSprites.south.Length, "16 frames / 8 dirs = 2 each.");
        }

        [Test]
        public void ExplicitFourDirectional_OverridesTheHeuristic_OnAFrameCountAutoWouldTreatAsEightDir()
        {
            // 24 frames alone would Auto-resolve to 8-directional (see above). An explicit
            // override must win regardless — this is the exact "24-frame sheet that could
            // be a genuine 8x3 strip or a stretched 4-direction one" ambiguity the enum
            // exists to remove.
            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames(24),
                directionLayout = EntitySheetDirectionLayout.FourDirectional_S_W_E_N,
            };

            var animator = Bind(config);

            Assert.IsTrue(IsFourDirectionalLayout(animator.IdleSprites));
            Assert.AreEqual(6, animator.IdleSprites.south.Length, "24 frames / 4 dirs = 6 each.");
        }

        // ---- The layout is shared with attack variants -------------------------

        [Test]
        public void ExplicitLayout_AlsoAppliesToAttackVariants()
        {
            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames(8),
                attackSheets = CreateFrames(8),
                directionLayout = EntitySheetDirectionLayout.FourDirectional_S_W_E_N,
                attackVariants = new List<AttackVariant>
                {
                    new AttackVariant { key = "punch", sheets = CreateFrames(16) },
                },
            };

            var animator = Bind(config);

            Assert.AreEqual(1, animator.AttackVariantCount);
            Assert.IsTrue(IsFourDirectionalLayout(animator.AttackVariantSet(0)),
                "attackVariants must resolve sheets through the same directionLayout as " +
                "the seven base states — a variant is authored on the same sheet " +
                "convention as the rest of the entity.");
        }

        // ---- Directional-sprite mode is untouched by the field -----------------

        [Test]
        public void DirectionLayoutField_IsIgnored_WhenDirectionalSpritesAreWired()
        {
            var texture = new Texture2D(1, 1);
            _created.Add(texture);
            var idleSouth = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _created.Add(idleSouth);

            var config = new EntityAssetConfig
            {
                idle = new DirectionalSprites { south = idleSouth },
                directionLayout = EntitySheetDirectionLayout.EightDirectional,
            };

            var animator = Bind(config);

            // The "no-sets" directional mode takes precedence over sheets entirely
            // (HasDirectionalSprites short-circuits BuildSet) — directionLayout has
            // nothing to resolve here and must not throw or otherwise interfere.
            Assert.AreSame(idleSouth, animator.IdleSprites.south[0]);
        }
    }
}
