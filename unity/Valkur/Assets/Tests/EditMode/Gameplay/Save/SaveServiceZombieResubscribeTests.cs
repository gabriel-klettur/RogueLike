using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;

// Regression coverage for incident `.github/incidents/RUN_TWIN_SAVE.md`
// (the 2026-05-09 x12 recurrence).
//
// Mechanism the test reproduces:
//   1. EditMode test pollutes static state by AddComponent<SaveService> +
//      ForceSingletonInit (which manually invokes OnSingletonAwake — the
//      production Awake never runs in EditMode).
//   2. OnSingletonAwake subscribes to `SceneManager.sceneLoaded` and the
//      `GameEvents.On*` event bus.
//   3. TearDown calls DestroyImmediate on the GameObject. Components added
//      in EditMode without prior Awake do NOT receive OnDestroy, so the
//      static `SceneManager.sceneLoaded` delegate keeps the now-Unity-null
//      C# component alive ("zombie").
//   4. User enters Play Mode. The first runtime scene load fires
//      `SceneManager.sceneLoaded`. Pre-fix, the zombie's OnSceneLoaded
//      ran RebindGameEvents and re-subscribed itself to GameEvents.OnZoneChanged.
//      The very first ZoneManager.Update Lobby→Alpha transition then drove
//      one autosave per zombie into Saves/<zombie_runId>/autosave.json.
//
// Post-fix contract: SaveService.OnSceneLoaded detects the dead-Unity-object
// state via `this == null`, removes itself from `SceneManager.sceneLoaded`,
// and refuses to re-bind to GameEvents. GameEvents also wipes its static
// subscriber lists at SubsystemRegistration as defence-in-depth.

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    [TestFixture]
    public class SaveServiceZombieResubscribeTests
    {
        // ── Reflection helpers ────────────────────────────────────────────────

        private static void ForceSingletonInit(SaveService svc)
        {
            var baseType = typeof(SaveService).BaseType;
            var instanceField = baseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, svc);

            var onAwake = typeof(SaveService).GetMethod("OnSingletonAwake",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            onAwake?.Invoke(svc, null);
        }

        private static MethodInfo GetOnSceneLoaded()
        {
            return typeof(SaveService).GetMethod("OnSceneLoaded",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static Delegate GetGameEventsField(string name)
        {
            var f = typeof(GameEvents).GetField(name,
                BindingFlags.Static | BindingFlags.NonPublic);
            return (Delegate)f?.GetValue(null);
        }

        // ── SetUp / TearDown ──────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            // Start every test from a clean slate — no leftover subscribers
            // from earlier fixtures (TestRunner re-uses the AppDomain when
            // Domain Reload is OFF).
            GameEvents.Clear();
            if (SaveService.HasInstance)
                UnityEngine.Object.DestroyImmediate(SaveService.Instance.gameObject);
            var instanceField = typeof(SaveService).BaseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.Clear();
            if (SaveService.HasInstance)
                UnityEngine.Object.DestroyImmediate(SaveService.Instance.gameObject);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void GameEvents_HasRuntimeInitializeOnLoadResetHook()
        {
            // Defence-in-depth: GameEvents must wipe its static subscribers at
            // Play Mode entry so any leftover delegates from EditMode tests
            // (with Domain Reload OFF) cannot survive into Play Mode and fire
            // on the first runtime ZoneManager.Update.
            var method = typeof(GameEvents).GetMethod("ResetSubscribersOnPlayModeEnter",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method,
                "GameEvents must declare a private static ResetSubscribersOnPlayModeEnter " +
                "method to clear all event subscribers on Play Mode entry. " +
                "See incident .github/incidents/RUN_TWIN_SAVE.md.");

            var attrs = method.GetCustomAttributes(
                typeof(RuntimeInitializeOnLoadMethodAttribute), false);
            Assert.AreEqual(1, attrs.Length,
                "ResetSubscribersOnPlayModeEnter must have exactly one " +
                "[RuntimeInitializeOnLoadMethod] attribute.");
            var attr = (RuntimeInitializeOnLoadMethodAttribute)attrs[0];
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attr.loadType,
                "Reset must run at SubsystemRegistration so it precedes any " +
                "Awake — i.e. before zombie OnSceneLoaded callbacks fire.");
        }

        [Test]
        public void OnSceneLoaded_ZombieInstance_DoesNotResubscribeToGameEvents()
        {
            // Arrange: simulate a leaked SaveService from an EditMode test —
            // AddComponent without Awake, ForceSingletonInit subscribes, then
            // DestroyImmediate(go) leaves the C# component alive but
            // Unity-null. (Production OnDestroy would fire if the component
            // had been Awake'd; in EditMode-via-AddComponent it is not.)
            var go = new GameObject("ZombieSaveService");
            var svc = go.AddComponent<SaveService>();
            ForceSingletonInit(svc);

            Assert.IsNotNull(GetGameEventsField("OnZoneChanged"),
                "Pre-condition: OnSingletonAwake must have subscribed " +
                "HandleZoneChanged so OnZoneChanged is non-null.");

            // Detach the component from the singleton slot the way the next
            // test would (via reflection) so SaveService.HasInstance returns
            // false even before we destroy the GO. This mirrors what
            // ForceSingletonInit on the next test fixture does.
            var instanceField = typeof(SaveService).BaseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, null);

            // Now wipe the static subscriber lists (mirrors the next test's
            // GameEvents.Clear in TearDown — and the Play Mode reset hook).
            GameEvents.Clear();

            // Destroy the GO. With no Awake having ever run, OnDestroy is
            // skipped — the SceneManager.sceneLoaded delegate now points at a
            // Unity-null component. This is the leak that drove the x12
            // recurrence.
            UnityEngine.Object.DestroyImmediate(go);
            Assert.IsTrue(svc == null,
                "Component must compare Unity-null after DestroyImmediate.");

            // Act: invoke OnSceneLoaded the same way Unity would when the
            // next runtime scene loads. The zombie must NOT call
            // RebindGameEvents and re-subscribe its dead delegates.
            GetOnSceneLoaded().Invoke(svc, new object[]
            {
                default(Scene),
                LoadSceneMode.Single
            });

            // Assert: GameEvents stays clean.
            Assert.IsNull(GetGameEventsField("OnZoneChanged"),
                "Zombie OnSceneLoaded must NOT re-subscribe HandleZoneChanged " +
                "to GameEvents. This is the regression that produced 12 " +
                "duplicate Saves/<runId>/ folders on 2026-05-09.");
            Assert.IsNull(GetGameEventsField("OnPlayerDamaged"),
                "Zombie OnSceneLoaded must not re-subscribe any GameEvent.");
            Assert.IsNull(GetGameEventsField("OnXpGained"),
                "Zombie OnSceneLoaded must not re-subscribe any GameEvent.");
        }

        [Test]
        public void OnSceneLoaded_LiveInstance_StillRebindsAfterClear()
        {
            // Production contract: the live SaveService MUST re-bind to
            // GameEvents on every scene load (because SceneTransitionManager
            // and LoadingScreenController call GameEvents.Clear() to flush
            // stale subscribers). The zombie short-circuit must not regress
            // this — only Unity-null components may skip the re-bind.
            var go = new GameObject("LiveSaveService");
            var svc = go.AddComponent<SaveService>();
            ForceSingletonInit(svc);

            Assert.IsNotNull(GetGameEventsField("OnZoneChanged"),
                "Pre-condition: subscribed after OnSingletonAwake.");

            GameEvents.Clear();
            Assert.IsNull(GetGameEventsField("OnZoneChanged"),
                "Pre-condition: GameEvents.Clear wiped the subscriber.");

            GetOnSceneLoaded().Invoke(svc, new object[]
            {
                default(Scene),
                LoadSceneMode.Single
            });

            Assert.IsNotNull(GetGameEventsField("OnZoneChanged"),
                "Live SaveService.OnSceneLoaded must re-bind to GameEvents " +
                "after Clear() so the next FireZoneChanged still hits its handler.");

            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void EndToEnd_ManyZombiesPlusOneLive_OnlyLiveHandlesFireZoneChanged()
        {
            // End-to-end reconstruction of the 2026-05-09 incident: 11 zombie
            // SaveService components subscribed to GameEvents, plus 1 live
            // production-like instance. Pre-fix, FireZoneChanged would invoke
            // HandleZoneChanged on all 12 (each writing to its own runId).
            // Post-fix:
            //   • the first scene load runs OnSceneLoaded on every subscriber,
            //   • zombies self-unsubscribe (this == null short-circuit),
            //   • only the live instance remains bound to GameEvents,
            //   • a subsequent FireZoneChanged dispatches to exactly one
            //     HandleZoneChanged (visible as one MarkDirty log line).

            const int zombieCount = 11;

            // Build the 11 zombies: subscribe, then destroy the GO without
            // calling OnDestroy (mirrors the EditMode test pollution path).
            for (int i = 0; i < zombieCount; i++)
            {
                var zgo = new GameObject($"Zombie_{i}");
                var zsvc = zgo.AddComponent<SaveService>();
                ForceSingletonInit(zsvc);
                // Mint a distinct runId per zombie (matches incident pattern).
                zsvc.BeginNewRun();
                zsvc.SetRunOrdinal(1);
                // Detach from singleton slot so the next ForceSingletonInit doesn't
                // overwrite _instance with a now-zombie reference.
                var instanceField = typeof(SaveService).BaseType?.GetField("_instance",
                    BindingFlags.Static | BindingFlags.NonPublic);
                instanceField?.SetValue(null, null);
                // Destroy the GO. With no Awake having run, OnDestroy is skipped
                // — exactly the bug-source path that produced the incident.
                UnityEngine.Object.DestroyImmediate(zgo);
            }

            // Build the live production-like instance.
            var liveGo = new GameObject("LiveSaveService");
            var liveSvc = liveGo.AddComponent<SaveService>();
            ForceSingletonInit(liveSvc);
            liveSvc.BeginNewRun();
            liveSvc.SetRunOrdinal(99);

            // Sanity: at this point GameEvents.OnZoneChanged should hold the
            // live subscriber AND every zombie's HandleZoneChanged delegate
            // because the zombies' subscriptions were never unwired.
            var onZone = GetGameEventsField("OnZoneChanged");
            Assert.IsNotNull(onZone,
                "Pre-condition: at least the live SaveService is subscribed.");
            int subscribersBefore = onZone.GetInvocationList().Length;
            Assert.GreaterOrEqual(subscribersBefore, zombieCount + 1,
                $"Pre-condition: {zombieCount} zombies + 1 live = at least " +
                $"{zombieCount + 1} subscribers expected. Got {subscribersBefore}. " +
                "If this assertion ever falls below the floor, the test is no " +
                "longer reproducing the bug — it would silently pass even on " +
                "a regression.");

            // Simulate Unity firing SceneManager.sceneLoaded once on Play Mode
            // entry. Every subscribed OnSceneLoaded handler runs — zombies
            // self-unsubscribe, the live instance re-binds.
            //
            // We can't fire SceneManager.sceneLoaded directly (engine-side),
            // so we drive each subscriber through OnSceneLoaded via reflection,
            // emulating Unity's broadcast.
            foreach (var d in onZone.GetInvocationList())
            {
                // Each delegate's Target is a SaveService whose HandleZoneChanged
                // we want to unwire. Actually easier: invoke each subscriber's
                // OnSceneLoaded directly.
                if (d.Target is SaveService svc)
                {
                    GetOnSceneLoaded().Invoke(svc, new object[]
                    {
                        default(Scene),
                        LoadSceneMode.Single
                    });
                }
            }

            // After the simulated scene-load broadcast, re-read the subscriber
            // list. Only the live instance should remain.
            var onZoneAfter = GetGameEventsField("OnZoneChanged");
            Assert.IsNotNull(onZoneAfter,
                "Post-condition: live SaveService stays subscribed.");
            var listAfter = onZoneAfter.GetInvocationList();
            Assert.AreEqual(1, listAfter.Length,
                "Post-condition: zombies self-unsubscribed; exactly one " +
                $"subscriber must remain. Got {listAfter.Length}. " +
                "This is THE invariant that prevents the x12 RUN_TWIN_SAVE recurrence.");
            Assert.AreSame(liveSvc, listAfter[0].Target,
                "Post-condition: the surviving subscriber must be the live instance.");

            UnityEngine.Object.DestroyImmediate(liveGo);
        }
    }
}
