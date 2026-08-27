using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Guards the hazard CLAUDE.md names for adding an <see cref="DirectionalAnimator.AnimState"/>:
    /// <c>PlayerController.Movement</c> overrides locomotion on an Idle/Walk/Chase whitelist and
    /// reverts on a second one, so a state the player can ENTER but that appears in neither list
    /// is entered and never left — a soft lock that no other system rescues.
    ///
    /// Asserted against the source text rather than by driving a live PlayerController, because
    /// the two whitelists are `if` conditions inside private per-frame methods with no seam, and
    /// the thing worth pinning is precisely that the lists agree with the enum. That is the same
    /// technique <c>FSMBuiltInTransitionRegistryTests</c> uses to keep its transition table honest
    /// against the state classes, and for the same reason: the failure is a mismatch between two
    /// places that must be edited together.
    /// </summary>
    public class PlayerAnimStateRevertTests
    {
        private const string MovementSource =
            "Assets/_Project/Scripts/Gameplay/Player/PlayerController.Movement.cs";

        /// <summary>
        /// States locomotion is allowed to take back over on its own. Everything else is owned
        /// by another system while it plays.
        /// </summary>
        private static readonly string[] LocomotionStates = { "Idle", "Walk", "Chase" };

        /// <summary>
        /// States the player itself can enter. Damage and Death are excluded because the player
        /// never enters them from PlayerController — Health and the death flow own them, and
        /// Death is deliberately terminal.
        /// </summary>
        private static readonly string[] PlayerEnterableStates = { "Cast", "Attack", "Recover" };

        private static string ReadMovementSource()
        {
            string full = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, MovementSource);
            Assert.IsTrue(File.Exists(full), $"Expected to find '{MovementSource}'.");
            return File.ReadAllText(full);
        }

        /// <summary>
        /// Every AnimState named by the revert method's GUARD condition.
        ///
        /// The guard, not the whole body: the body also names Idle and Walk as the states it
        /// reverts TO, so scanning it wholesale reads a state as "handled" simply because it
        /// is a destination. Anchored on the declaration rather than on the method name,
        /// because the name appears first at its call site in HandleAnimation, and matching
        /// that scans the wrong block entirely — which is how the first version of this test
        /// managed to fail against correct code.
        /// </summary>
        private static HashSet<string> StatesNamedInRevertGuard(string source)
        {
            const string declaration = "private void TickCastAnimRevert()";
            int start = source.IndexOf(declaration, System.StringComparison.Ordinal);
            Assert.Greater(start, -1, $"'{declaration}' should exist in {MovementSource}.");

            int guard = source.IndexOf("_animator.CurrentState", start, System.StringComparison.Ordinal);
            Assert.Greater(guard, -1, "The revert method should guard on _animator.CurrentState.");

            int open = source.LastIndexOf('(', guard);
            int depth = 0;
            int end = open;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '(') depth++;
                else if (source[i] == ')' && --depth == 0) { end = i; break; }
            }

            var found = new HashSet<string>();
            foreach (Match m in Regex.Matches(source.Substring(open, end - open),
                                              @"AnimState\.(\w+)"))
            {
                found.Add(m.Groups[1].Value);
            }
            return found;
        }

        [Test]
        public void EveryStateThePlayerCanEnter_IsAlsoReverted()
        {
            HashSet<string> reverted = StatesNamedInRevertGuard(ReadMovementSource());

            var missing = new List<string>();
            foreach (string state in PlayerEnterableStates)
            {
                if (!reverted.Contains(state)) missing.Add(state);
            }

            Assert.That(missing, Is.Empty,
                "TickCastAnimRevert is the only thing that hands control back to locomotion " +
                "after a non-locomotion animation. A state the player enters that it does not " +
                "check is entered and never left: locomotion refuses to override it, and the " +
                "system that set it may be a coroutine a scene change can kill.\n  Missing: " +
                string.Join(", ", missing));
        }

        [Test]
        public void NoLocomotionState_IsInTheRevertGuard()
        {
            HashSet<string> reverted = StatesNamedInRevertGuard(ReadMovementSource());

            var overlap = new List<string>();
            foreach (string state in LocomotionStates)
            {
                if (reverted.Contains(state)) overlap.Add(state);
            }

            Assert.That(overlap, Is.Empty,
                "A locomotion state must not appear in the revert GUARD: HandleAnimation " +
                "already reassigns those every frame, so reverting them as well would fight " +
                "that assignment. They do legitimately appear in the method's BODY as the " +
                "state it reverts TO, which is why only the guard is read here.\n  " +
                "Overlapping: " + string.Join(", ", overlap));
        }

        [Test]
        public void EveryAnimState_ResolvesToItsOwnSpriteSet_ForACharacterThatShipsThemAll()
        {
            // elven is the one character with art in every slot, so it is the one that can
            // prove GetSpriteSet's switch has no silent hole. A state added to the enum but
            // not to that switch falls through to `_ => idleSprites` and renders the idle
            // pose forever, which reads as the animation being missing rather than unwired.
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                "Assets/_Project/Data/Catalogs/Players/elven.asset");
            Assert.IsNotNull(def, "elven.asset should exist.");

            var go = new GameObject("anim-state-probe");
            try
            {
                go.AddComponent<SpriteRenderer>();
                Assert.IsTrue(EntityAnimationBinder.ApplyPlayerVisuals(go, def),
                    "elven should bind onto a bare renderer.");

                var animator = go.GetComponent<DirectionalAnimator>();
                var seen = new Dictionary<string, string>();
                var failures = new List<string>();

                foreach (DirectionalAnimator.AnimState state in
                         System.Enum.GetValues(typeof(DirectionalAnimator.AnimState)))
                {
                    animator.SetState(state, DirectionalAnimator.Direction.East, -1);
                    Sprite[] frames = animator
                        .GetType()
                        .GetMethod("ResolveFrames", System.Reflection.BindingFlags.NonPublic
                                                    | System.Reflection.BindingFlags.Instance)
                        ?.Invoke(animator, new object[]
                        {
                            state, DirectionalAnimator.Direction.East, -1
                        }) as Sprite[];

                    if (frames == null || frames.Length == 0 || frames[0] == null)
                    {
                        failures.Add($"{state}: no frames");
                        continue;
                    }

                    string first = frames[0].name;
                    if (seen.TryGetValue(first, out string other))
                        failures.Add($"{state} renders the same frames as {other} ('{first}')");
                    else
                        seen[first] = state.ToString();
                }

                Assert.That(failures, Is.Empty,
                    "Each AnimState must resolve to its own set for a character that ships art " +
                    "for all of them.\n  " + string.Join("\n  ", failures));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
