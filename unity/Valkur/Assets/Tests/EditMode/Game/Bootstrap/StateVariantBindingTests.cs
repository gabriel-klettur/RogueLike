using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Bootstrap;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Guards how <see cref="EntityAnimationBinder"/> installs the per-state alternatives —
    /// the second walk cycle, the third death — and how a LOADOUT scopes them.
    ///
    /// The loadout half is the part that is easy to get subtly wrong, and it is wrong in a way
    /// no still frame shows: the mague's base walk carries three bare-handed cycles and his
    /// `armed` loadout four staff ones, so a rotation that stayed global would put the staff
    /// back in his empty hands one step in four, and take it out of them one step in three.
    /// A loadout therefore REPLACES rather than extends, at every level — state sets, state
    /// variants, swings and casts.
    /// </summary>
    public class StateVariantBindingTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int FramesPerDirection = 2;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ---- Helpers --------------------------------------------------------

        private List<Sprite> Frames(string prefix)
        {
            int count = 8 * FramesPerDirection;
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                sprite.name = $"{prefix}_{i}";
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        private GameObject Entity()
        {
            var go = new GameObject("StateVariantEntity");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator).GetField("targetRenderer", Instance).SetValue(anim, renderer);
            return go;
        }

        /// <summary>A config with every base slot filled, so the binder never refuses.</summary>
        private EntityAssetConfig BaseConfig()
        {
            return new EntityAssetConfig
            {
                directionLayout = EntitySheetDirectionLayout.EightDirectional,
                idleSheets = Frames("base_idle"),
                walkSheets = Frames("base_walk"),
                chaseSheets = Frames("base_chase"),
                castSheets = Frames("base_cast"),
                attackSheets = Frames("base_attack"),
                damageSheets = Frames("base_damage"),
                deathSheets = Frames("base_death"),
            };
        }

        private StateVariant Variant(string key) => new StateVariant { key = key, sheets = Frames(key) };

        private static string RenderedName(DirectionalAnimator anim)
            => anim.GetComponent<SpriteRenderer>().sprite != null
                ? anim.GetComponent<SpriteRenderer>().sprite.name
                : null;

        // ---- Base install ---------------------------------------------------

        [Test]
        public void BaseStateVariants_ReachTheAnimator()
        {
            EntityAssetConfig config = BaseConfig();
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "walk",
                variants = new List<StateVariant> { Variant("walk_a"), Variant("walk_b") },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, null));

            var anim = go.GetComponent<DirectionalAnimator>();
            Assert.AreEqual(2, anim.VariantCount(DirectionalAnimator.AnimState.Walk));
            // Nothing else gained a rotation: a group names one state and only that one.
            Assert.AreEqual(0, anim.VariantCount(DirectionalAnimator.AnimState.Idle));
            Assert.AreEqual(0, anim.VariantCount(DirectionalAnimator.AnimState.Death));
        }

        [Test]
        public void AVariantWithNoFrames_IsDropped_NotInstalledEmpty()
        {
            EntityAssetConfig config = BaseConfig();
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "death",
                variants = new List<StateVariant>
                {
                    Variant("death_a"),
                    new StateVariant { key = "death_broken", sheets = new List<Sprite>() },
                    Variant("death_b"),
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, null));

            // An empty slot in the rotation renders the fallback pose one entry in N, which
            // reads as the state failing to animate rather than as missing art.
            Assert.AreEqual(2, go.GetComponent<DirectionalAnimator>()
                                 .VariantCount(DirectionalAnimator.AnimState.Death));
        }

        [Test]
        public void NamingAttackOrCast_IsRefused_RatherThanFightingTheirOwnLists()
        {
            EntityAssetConfig config = BaseConfig();
            config.attackVariants.Add(new AttackVariant { key = "swing", sheets = Frames("swing") });
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "attack",
                variants = new List<StateVariant> { Variant("bogus_a"), Variant("bogus_b") },
            });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("stateVariants declares 'attack'"));

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, null));

            // attackVariants stands; the stateVariants entry changed nothing. Silently losing
            // to whichever install ran last is what this refusal exists to prevent.
            Assert.AreEqual(1, go.GetComponent<DirectionalAnimator>()
                                 .VariantCount(DirectionalAnimator.AnimState.Attack));
        }

        // ---- Loadout scoping ------------------------------------------------

        [Test]
        public void ALoadoutsStateVariants_ReplaceTheBaseOnes()
        {
            EntityAssetConfig config = BaseConfig();
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "walk",
                variants = new List<StateVariant> { Variant("bare_1"), Variant("bare_2"), Variant("bare_3") },
            });
            config.loadouts.Add(new Loadout
            {
                key = "armed",
                states = new List<LoadoutStateSheets>
                {
                    new LoadoutStateSheets
                    {
                        state = "walk",
                        sheets = Frames("armed_walk"),
                        variants = new List<StateVariant> { Variant("armed_1"), Variant("armed_2") },
                    },
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, "armed"));

            var anim = go.GetComponent<DirectionalAnimator>();
            Assert.AreEqual(2, anim.VariantCount(DirectionalAnimator.AnimState.Walk));

            var names = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
                names.Add(RenderedName(anim));
                anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            }

            foreach (string n in names)
            {
                Assert.IsNotNull(n);
                StringAssert.StartsWith("armed_", n,
                    "a loadout's rotation must never step into the base character's art");
            }
        }

        [Test]
        public void SwappingBackToBase_RestoresTheBaseRotation()
        {
            EntityAssetConfig config = BaseConfig();
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "walk",
                variants = new List<StateVariant> { Variant("bare_1"), Variant("bare_2"), Variant("bare_3") },
            });
            config.loadouts.Add(new Loadout
            {
                key = "armed",
                states = new List<LoadoutStateSheets>
                {
                    new LoadoutStateSheets
                    {
                        state = "walk",
                        sheets = Frames("armed_walk"),
                        variants = new List<StateVariant> { Variant("armed_1") },
                    },
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, "armed"));
            Assert.AreEqual(1, go.GetComponent<DirectionalAnimator>()
                                 .VariantCount(DirectionalAnimator.AnimState.Walk));

            // Every state is pushed on a re-bind, not only the authored ones: a stale array
            // left from the loadout is what would keep the staff walking after it was stowed.
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, null));
            Assert.AreEqual(3, go.GetComponent<DirectionalAnimator>()
                                 .VariantCount(DirectionalAnimator.AnimState.Walk));
        }

        [Test]
        public void ALoadoutThatOverridesAStateWithoutVariants_ClearsTheBaseRotation()
        {
            EntityAssetConfig config = BaseConfig();
            config.stateVariants.Add(new StateVariantGroup
            {
                state = "walk",
                variants = new List<StateVariant> { Variant("bare_1"), Variant("bare_2") },
            });
            config.loadouts.Add(new Loadout
            {
                key = "armed",
                states = new List<LoadoutStateSheets>
                {
                    // No variants: this loadout draws the walk exactly once.
                    new LoadoutStateSheets { state = "walk", sheets = Frames("armed_walk") },
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, "armed"));

            var anim = go.GetComponent<DirectionalAnimator>();
            Assert.AreEqual(0, anim.VariantCount(DirectionalAnimator.AnimState.Walk));

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            StringAssert.StartsWith("armed_walk", RenderedName(anim));
        }

        [Test]
        public void ALoadoutsSwingsAndCasts_ReplaceTheBaseRotations()
        {
            EntityAssetConfig config = BaseConfig();
            config.attackVariants.Add(new AttackVariant { key = "punch", sheets = Frames("punch") });
            config.attackVariants.Add(new AttackVariant { key = "kick", sheets = Frames("kick") });
            config.castVariants.Add(new CastVariant { key = "bare_cast", sheets = Frames("bare_cast") });
            config.loadouts.Add(new Loadout
            {
                key = "armed",
                states = new List<LoadoutStateSheets>
                {
                    new LoadoutStateSheets { state = "attack", sheets = Frames("armed_attack") },
                },
                attackVariants = new List<AttackVariant>
                {
                    new AttackVariant { key = "staff_swing", sheets = Frames("staff_swing") },
                },
                castVariants = new List<CastVariant>
                {
                    new CastVariant { key = "staff_cast_1", sheets = Frames("staff_cast_1") },
                    new CastVariant { key = "staff_cast_2", sheets = Frames("staff_cast_2") },
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, "armed"));

            var anim = go.GetComponent<DirectionalAnimator>();
            Assert.AreEqual(1, anim.VariantCount(DirectionalAnimator.AnimState.Attack));
            Assert.AreEqual(2, anim.VariantCount(DirectionalAnimator.AnimState.Cast));
        }

        [Test]
        public void ALoadoutWithNoVariantsOfItsOwn_KeepsTheBaseRotations()
        {
            EntityAssetConfig config = BaseConfig();
            config.attackVariants.Add(new AttackVariant { key = "punch", sheets = Frames("punch") });
            config.attackVariants.Add(new AttackVariant { key = "kick", sheets = Frames("kick") });
            config.loadouts.Add(new Loadout
            {
                key = "hat",
                states = new List<LoadoutStateSheets>
                {
                    new LoadoutStateSheets { state = "idle", sheets = Frames("hat_idle") },
                },
            });

            GameObject go = Entity();
            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, "hat"));

            // An empty list means "this loadout does not change how I swing", not "I swing
            // once" — a loadout that only replaces a hat must not delete the melee rotation.
            Assert.AreEqual(2, go.GetComponent<DirectionalAnimator>()
                                 .VariantCount(DirectionalAnimator.AnimState.Attack));
        }
    }
}
