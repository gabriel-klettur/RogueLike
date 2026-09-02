using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Guards the loadout swap: a character can be re-drawn with a second set of sprites for
    /// SOME of its states, keeping the base art for the rest.
    ///
    /// The dwarf's armed loadout has art for four states — idle, walk, chase, attack — and
    /// will never have art for the other six. So the two things worth pinning are that an
    /// overridden state actually changes, and that a state the loadout says nothing about
    /// does NOT: an override list that quietly blanked the six unlisted states would put the
    /// character in the wrong POSE (through the binder's fallback chain) rather than merely
    /// the wrong hands, and it would do it only on the states nobody looks at first.
    ///
    /// The third is the toggle contract. An unknown key must be REFUSED, not treated as
    /// "unequip" — a typo in a spell asset would otherwise read as a working toggle that only
    /// ever undresses, which looks like the art failed to load rather than like a bad key.
    /// </summary>
    public class PlayerLoadoutControllerTests
    {
        private const int FramesPerDirection = 4;

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

        // ---- Helpers --------------------------------------------------------

        /// <summary>8 * FramesPerDirection sprites, each named after <paramref name="prefix"/>
        /// so a failure says which set was rendered rather than comparing references.</summary>
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

        private EntityAssetConfig ConfigWithArmedLoadout()
        {
            var config = new EntityAssetConfig
            {
                directionLayout = EntitySheetDirectionLayout.EightDirectional,
                idleSheets   = Frames("base_idle"),
                walkSheets   = Frames("base_walk"),
                chaseSheets  = Frames("base_chase"),
                castSheets   = Frames("base_cast"),
                attackSheets = Frames("base_attack"),
                damageSheets = Frames("base_damage"),
                deathSheets  = Frames("base_death"),
            };

            config.loadouts = new List<Loadout>
            {
                new Loadout
                {
                    key = "armed",
                    states = new List<LoadoutStateSheets>
                    {
                        new LoadoutStateSheets { state = "idle",   sheets = Frames("armed_idle") },
                        new LoadoutStateSheets { state = "walk",   sheets = Frames("armed_walk") },
                        new LoadoutStateSheets { state = "chase",  sheets = Frames("armed_chase") },
                        new LoadoutStateSheets { state = "attack", sheets = Frames("armed_attack") },
                    },
                },
            };
            return config;
        }

        private GameObject Character(EntityAssetConfig config, out PlayerLoadoutController loadouts)
        {
            var go = new GameObject("TestCharacter");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();

            Assert.IsTrue(EntityAnimationBinder.ApplyLoadout(go, config, null),
                "The base bind must succeed before a swap can be tested.");

            // Awake never runs in EditMode, so the animator's `targetRenderer` stays null and
            // ApplyFrame writes nowhere. Without this the sprite only ever changes when the
            // BINDER seeds it (renderer.sprite = PeekFirstFrame(idle)), so every read below
            // returns the idle frame of whichever set was last bound and each test passes or
            // fails for the wrong reason — the first assertion of each looked right and every
            // one after it read a stale sprite.
            typeof(DirectionalAnimator)
                .GetField("targetRenderer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(go.GetComponent<DirectionalAnimator>(), renderer);

            loadouts = go.AddComponent<PlayerLoadoutController>();
            loadouts.Initialize(config);
            return go;
        }

        /// <summary>The name of the first frame the animator resolves for a state, which
        /// identifies which SET is installed without reaching into private arrays.</summary>
        private static string FirstFrameName(GameObject go, DirectionalAnimator.AnimState state)
        {
            var anim = go.GetComponent<DirectionalAnimator>();
            anim.SetState(state, DirectionalAnimator.Direction.East);
            Sprite rendered = go.GetComponent<SpriteRenderer>().sprite;
            return rendered != null ? rendered.name : null;
        }

        // ---- Overriding -----------------------------------------------------

        [Test]
        public void WearingALoadout_ReplacesTheStatesItOverrides()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);

            StringAssert.StartsWith("base_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));

            Assert.IsTrue(loadouts.SetLoadout("armed"));
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
            StringAssert.StartsWith("armed_walk_", FirstFrameName(go, DirectionalAnimator.AnimState.Walk));
            StringAssert.StartsWith("armed_chase_", FirstFrameName(go, DirectionalAnimator.AnimState.Chase));
            StringAssert.StartsWith("armed_attack_", FirstFrameName(go, DirectionalAnimator.AnimState.Attack));
        }

        [Test]
        public void WearingALoadout_LeavesTheStatesItDoesNotMention()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.SetLoadout("armed"));

            StringAssert.StartsWith("base_cast_", FirstFrameName(go, DirectionalAnimator.AnimState.Cast),
                "The armed loadout has no cast art and never will — casting must keep the " +
                "character's one authored casting animation.");
            StringAssert.StartsWith("base_damage_", FirstFrameName(go, DirectionalAnimator.AnimState.Damage));
            StringAssert.StartsWith("base_death_", FirstFrameName(go, DirectionalAnimator.AnimState.Death));
        }

        [Test]
        public void RemovingALoadout_RestoresTheBaseArtOnEveryState()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);

            Assert.IsTrue(loadouts.SetLoadout("armed"));
            Assert.IsTrue(loadouts.SetLoadout(null));

            StringAssert.StartsWith("base_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
            StringAssert.StartsWith("base_attack_", FirstFrameName(go, DirectionalAnimator.AnimState.Attack));
            Assert.IsFalse(loadouts.HasLoadoutActive);
        }

        // ---- Toggle contract ------------------------------------------------

        [Test]
        public void Toggle_AlternatesBetweenTheLoadoutAndTheBaseArt()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey);
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            loadouts.TickPendingStow(5f);
            Assert.IsNull(loadouts.ActiveLoadoutKey);
            StringAssert.StartsWith("base_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        // ---- Deferred stow --------------------------------------------------
        //
        // Drawing and stowing are deliberately asymmetric. The sheathe animation SHOWS the
        // weapon for its whole length, so stripping it on the cast frame would play a second
        // of putting away a sword the character is no longer holding.

        [Test]
        public void Stowing_KeepsTheWeaponUntilTheAnimationFinishes()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.StowPending);
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey,
                "The art must not swap on the cast frame - the sheathe is drawn with the " +
                "weapon in hand for every one of its frames.");
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));

            loadouts.ScheduleStow(1.2f);
            loadouts.TickPendingStow(0.6f);
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey, "Still mid-sheathe.");

            loadouts.TickPendingStow(0.6f);
            Assert.IsFalse(loadouts.StowPending);
            Assert.IsNull(loadouts.ActiveLoadoutKey);
            StringAssert.StartsWith("base_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void Drawing_SwapsOnTheCastFrameWithNothingPending()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsFalse(loadouts.StowPending,
                "Only the stow is deferred. Deferring the draw too would play the whole " +
                "equip animation on a character with empty hands.");
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void StowingReportsItsDirectionOnTheCastFrame_NotWhenTheArtLands()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.SwappedThisFrame);
            Assert.IsTrue(loadouts.LastSwapStowed,
                "PlayerController.ShouldPlayCastReversed reads this in the SAME frame the " +
                "executor ran, to pick the playback direction. Waiting for the art to land " +
                "would leave the sheathe playing forwards, which reads as a second draw.");
        }

        [Test]
        public void PressingAgainMidSheathe_CancelsTheStowAndKeepsTheWeapon()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.StowPending);

            Assert.IsTrue(loadouts.ToggleLoadout("armed"),
                "A press mid-sheathe means the player changed their mind, so it must do " +
                "something rather than queue a second stow.");
            Assert.IsFalse(loadouts.StowPending);
            Assert.IsFalse(loadouts.LastSwapStowed,
                "Calling the stow off is a DRAW, so the equip animation plays forward.");

            loadouts.TickPendingStow(5f);
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey,
                "The cancelled stow must not land later and undress the character.");
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void SetLoadout_CancelsAPendingStow()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.StowPending);

            // What the animation probes do: park the character in a loadout directly. A stow
            // left armed would fire seconds later and undress what the probe just dressed.
            Assert.IsFalse(loadouts.SetLoadout("armed"), "Already worn - no change.");
            Assert.IsFalse(loadouts.StowPending);

            loadouts.TickPendingStow(5f);
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey);
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void AnUnscheduledStow_StillLandsOnTheFallbackDelay()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));

            // Nothing called ScheduleStow - a caster with no animator, or a path that never
            // reaches TriggerCastAnimation. The character must not hang armed forever.
            loadouts.TickPendingStow(0.36f);
            Assert.IsFalse(loadouts.StowPending);
            Assert.IsNull(loadouts.ActiveLoadoutKey);
        }

        [Test]
        public void ScheduleStow_DoesNothingWhenNoStowIsPending()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.ToggleLoadout("armed"));

            // TriggerCastAnimation guards on SwappedThisFrame, but the call itself has to be
            // inert too: every cast in the game reaches it.
            loadouts.ScheduleStow(9f);
            loadouts.TickPendingStow(20f);
            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey);
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void SettingTheLoadoutAlreadyWorn_ReportsNoChange()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);

            Assert.IsTrue(loadouts.SetLoadout("armed"));
            Assert.IsFalse(loadouts.SetLoadout("armed"),
                "Re-applying the loadout already worn must report no change, or a spell that " +
                "plays its draw animation on success replays it on every cast.");
        }

        [Test]
        public void AnUnknownKey_IsRefusedRatherThanTreatedAsUnequip()
        {
            var config = ConfigWithArmedLoadout();
            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.SetLoadout("armed"));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "PlayerLoadoutController.*no loadout"));
            Assert.IsFalse(loadouts.SetLoadout("shield"));

            Assert.AreEqual("armed", loadouts.ActiveLoadoutKey,
                "A typo must not silently undress the character — that reads as the armed " +
                "art having failed to load rather than as a bad key.");
            StringAssert.StartsWith("armed_idle_", FirstFrameName(go, DirectionalAnimator.AnimState.Idle));
        }

        [Test]
        public void HasLoadout_AnswersFromTheConfig()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);

            Assert.IsTrue(loadouts.HasLoadout("armed"));
            Assert.IsTrue(loadouts.HasLoadout("ARMED"), "Keys compare case-insensitively.");
            Assert.IsFalse(loadouts.HasLoadout("shield"));
            Assert.IsFalse(loadouts.HasLoadout(null));
        }

        // ---- Swap direction (drives reversed playback) ----------------------

        [Test]
        public void DrawingReportsAForwardSwap_StowingReportsAStow()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.SwappedThisFrame);
            Assert.IsFalse(loadouts.LastSwapStowed,
                "Drawing must not report a stow, or the draw animation plays backwards.");

            Assert.IsTrue(loadouts.ToggleLoadout("armed"));
            Assert.IsTrue(loadouts.SwappedThisFrame);
            Assert.IsTrue(loadouts.LastSwapStowed,
                "Stowing is what makes PlayerController run the equip animation in reverse — " +
                "the sheathe is the draw backwards, and playing it forwards reads as drawing " +
                "the weapon a second time.");
        }

        [Test]
        public void ARefusedSwap_DoesNotReportOne()
        {
            var config = ConfigWithArmedLoadout();
            Character(config, out var loadouts);
            Assert.IsTrue(loadouts.SetLoadout("armed"));

            // Re-applying what is already worn changes nothing, so it must not look like a
            // swap: the toggle spell would otherwise replay its animation on every cast.
            Assert.IsFalse(loadouts.SetLoadout("armed"));
            Assert.IsFalse(loadouts.LastSwapStowed,
                "A refused swap must leave the last real swap's direction alone.");
        }

        // ---- Degenerate data ------------------------------------------------

        [Test]
        public void AnOverrideWithNoFrames_FallsThroughToTheBaseArt()
        {
            var config = ConfigWithArmedLoadout();
            config.loadouts[0].states.Add(new LoadoutStateSheets
            {
                state = "cast",
                sheets = new List<Sprite>(),
            });

            var go = Character(config, out var loadouts);
            Assert.IsTrue(loadouts.SetLoadout("armed"));

            StringAssert.StartsWith("base_cast_", FirstFrameName(go, DirectionalAnimator.AnimState.Cast),
                "An override that resolved to nothing is authoring debris, not an instruction " +
                "to blank the state.");
        }
    }
}
