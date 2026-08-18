using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.Spells.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// Behavioural companion to <see cref="DomainReloadStaticResetTests"/>.
    ///
    /// That fixture proves a reset hook EXISTS. This one proves each hook actually
    /// clears what it claims to, by invoking it through reflection and asserting the
    /// state is gone — because a hook whose body drifted out of sync with the fields
    /// it was written for is worse than no hook: the gate stays green while the bug
    /// comes back.
    ///
    /// Every case here corresponds to a real second-Play failure mode: a service
    /// pointing at a destroyed object, a subscriber firing into a dead component, or
    /// a decision from the previous session leaking into the next one.
    /// </summary>
    [TestFixture]
    public class StaticResetHooksTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Runs every SubsystemRegistration hook on <paramref name="type"/>, as Unity would on Play.</summary>
        private static void SimulatePlayModeEnter(Type type)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var hooks = type.GetMethods(flags)
                .Where(m => m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false)
                             .Cast<RuntimeInitializeOnLoadMethodAttribute>()
                             .Any(a => a.loadType == RuntimeInitializeLoadType.SubsystemRegistration))
                .ToList();

            Assert.IsNotEmpty(hooks,
                $"{type.Name} has no SubsystemRegistration hook. Domain Reload is OFF, so its statics " +
                "carry straight into the next Play session.");

            foreach (var h in hooks) h.Invoke(null, null);
        }

        /// <summary>Reads a private/backing static field, including the one behind an event.</summary>
        private static object ReadStatic(Type type, string fieldName)
        {
            var f = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"{type.Name}.{fieldName} not found — renamed? The hook must be updated with it.");
            return f.GetValue(null);
        }

        private sealed class FakeService { }

        // ── Registries ───────────────────────────────────────────────────────────

        [Test]
        public void ServiceLocator_Hook_DropsRegistrationsFromThePreviousSession()
        {
            ServiceLocator.Register(new FakeService());
            Assert.IsTrue(ServiceLocator.TryGet<FakeService>(out _), "Sanity: registration took.");

            SimulatePlayModeEnter(typeof(ServiceLocator));

            Assert.IsFalse(ServiceLocator.TryGet<FakeService>(out _),
                "A stale registration is the highest-fan-out second-Play bug: the resulting " +
                "MissingReferenceException surfaces at the consumer, not here.");
        }

        [Test]
        public void EntityRegistry_Hook_DropsEntitiesFromThePreviousSession()
        {
            var monster = new GameObject("HookTestMonster");
            _created.Add(monster);
            EntityRegistry.RegisterMonster(monster);
            Assert.AreEqual(1, EntityRegistry.MonsterCount, "Sanity: registration took.");

            SimulatePlayModeEnter(typeof(EntityRegistry));

            Assert.AreEqual(0, EntityRegistry.MonsterCount);
            Assert.IsFalse(EntityRegistry.HasPlayer);
        }

        // ── Leaked subscribers (the RUN_TWIN_SAVE bug class) ─────────────────────

        [Test]
        public void LoadingReporter_Hook_DropsSubscribers()
        {
            LoadingReporter.OnGameplayReady += () => { };
            LoadingReporter.OnStageProgress += (_, __) => { };

            SimulatePlayModeEnter(typeof(LoadingReporter));

            Assert.IsNull(LoadingReporter.OnGameplayReady);
            Assert.IsNull(LoadingReporter.OnStageProgress);
        }

        [Test]
        public void SaveService_Hook_DropsOnSaveRecoveredSubscribers()
        {
            SaveService.OnSaveRecovered += _ => { };
            Assert.IsNotNull(ReadStatic(typeof(SaveService), "OnSaveRecovered"), "Sanity: subscriber attached.");

            SimulatePlayModeEnter(typeof(SaveService));

            Assert.IsNull(ReadStatic(typeof(SaveService), "OnSaveRecovered"),
                "This is the exact leak behind RUN_TWIN_SAVE: 11 leaked SaveService instances each " +
                "writing their own run folder. See .github/incidents/RUN_TWIN_SAVE.md.");
        }

        [Test]
        public void CurrencyWallet_Hook_DropsOnCoinsChangedSubscribers()
        {
            CurrencyWallet.OnCoinsChanged += (_, __) => { };
            SimulatePlayModeEnter(typeof(CurrencyWallet));
            Assert.IsNull(ReadStatic(typeof(CurrencyWallet), "OnCoinsChanged"));
        }

        [Test]
        public void ItemConsumer_Hook_DropsOnItemConsumedSubscribers()
        {
            ItemConsumer.OnItemConsumed += _ => { };
            SimulatePlayModeEnter(typeof(ItemConsumer));
            Assert.IsNull(ReadStatic(typeof(ItemConsumer), "OnItemConsumed"));
        }

        [Test]
        public void TileEditorTheme_Hook_DropsOnChangedSubscribers()
        {
            TileEditorTheme.OnChanged += () => { };
            SimulatePlayModeEnter(typeof(TileEditorTheme));
            Assert.IsNull(ReadStatic(typeof(TileEditorTheme), "OnChanged"));
        }

        [Test]
        public void CurrencyWalletAndItemConsumer_ExposeEventsNotAssignableFields()
        {
            foreach (var (type, name) in new[]
                     {
                         (typeof(CurrencyWallet), "OnCoinsChanged"),
                         (typeof(ItemConsumer), "OnItemConsumed"),
                     })
            {
                Assert.IsNotNull(type.GetEvent(name, BindingFlags.Static | BindingFlags.Public),
                    $"{type.Name}.{name} must be an event. As a plain Action field, any caller could " +
                    "overwrite the whole subscriber list — or null it — from outside the class.");
            }
        }

        // ── Session intent that must not cross the Play boundary ─────────────────

        [Test]
        public void PendingSaveLoad_Hook_ClearsTheQueuedLoad()
        {
            PendingSaveLoad.Path = "C:/tmp/hook-test-save.json";
            PendingSaveLoad.PlayerClass = "mage";
            Assert.IsTrue(PendingSaveLoad.HasPending, "Sanity: a load is queued.");

            SimulatePlayModeEnter(typeof(PendingSaveLoad));

            Assert.IsFalse(PendingSaveLoad.HasPending,
                "Left set, the next Play auto-loads a save nobody asked for.");
            Assert.IsNull(PendingSaveLoad.PlayerClass);
        }

        [Test]
        public void PlayerSelectionState_Hook_ForgetsTheChosenClass()
        {
            PlayerSelectionState.SetSelectedPlayer(PlayerSelectionState.DefaultPlayerOrder.Last());
            Assert.IsTrue(PlayerSelectionState.HasExplicitSelection, "Sanity: a class was chosen.");

            SimulatePlayModeEnter(typeof(PlayerSelectionState));

            Assert.IsFalse(PlayerSelectionState.HasExplicitSelection,
                "A class picked in one session must not silently apply to the next.");
            Assert.AreEqual(PlayerSelectionState.DefaultPlayerOrder[0], PlayerSelectionState.SelectedPlayerKey);
        }

        [Test]
        public void SpellDragContext_Hook_EndsADragInterruptedByLeavingPlayMode()
        {
            SimulatePlayModeEnter(typeof(SpellDragContext));

            Assert.IsFalse(SpellDragContext.IsDragging,
                "A drag interrupted by Stop would otherwise start the next session mid-drag.");
            Assert.AreEqual(-1, SpellDragContext.SourceSlotIndex);
            Assert.IsTrue(SpellDragContext.GhostObject == null,
                "The ghost GameObject belongs to the destroyed session; holding it is a fake-null reference.");
        }

        [Test]
        public void SaveTelemetry_Hook_StartsTheSessionWithAnEmptyLedger()
        {
            SaveTelemetry.Record(new SaveTelemetryEntry());
            Assert.Greater(SaveTelemetry.TotalRecorded, 0, "Sanity: an entry was recorded.");

            SimulatePlayModeEnter(typeof(SaveTelemetry));

            Assert.AreEqual(0, SaveTelemetry.TotalRecorded);
            Assert.IsEmpty(SaveTelemetry.Snapshot());
        }

        // ── Safety guards that must re-arm ───────────────────────────────────────

        [Test]
        public void MapEditorZonesRepository_Hook_ReArmsTheEditModeWriteGuard()
        {
            JsonFileMapEditorZonesRepository.AllowEditModeWritesToRealPath = true;

            SimulatePlayModeEnter(typeof(JsonFileMapEditorZonesRepository));

            Assert.IsFalse(JsonFileMapEditorZonesRepository.AllowEditModeWritesToRealPath,
                "A test that opted in and failed before TearDown would leave production zone data " +
                "writable for the rest of the domain — the 2026-05-23 zone-loss class of bug.");
        }

        // ── Registration that must be re-run, not merely cleared ─────────────────

        [Test]
        public void SaveSchemaMigrator_Hook_LeavesTheMigrationChainPopulated()
        {
            SaveMigrationChain.Clear();

            SimulatePlayModeEnter(typeof(SaveSchemaMigrator));

            var data = new GameSaveData { schemaVersion = "1.0" };
            var migrated = SaveSchemaMigrator.Migrate(data);

            Assert.AreEqual(SaveSchemaMigrator.CURRENT_SCHEMA, migrated.schemaVersion,
                "This used to be a static constructor, so running the EditMode suite emptied the chain " +
                "and Play Mode then silently stopped migrating saves until Unity was restarted.");
        }

        [Test]
        public void SaveSchemaMigrator_Hook_IsIdempotent()
        {
            SimulatePlayModeEnter(typeof(SaveSchemaMigrator));
            SimulatePlayModeEnter(typeof(SaveSchemaMigrator));

            var migrated = SaveSchemaMigrator.Migrate(new GameSaveData { schemaVersion = "1.0" });

            Assert.AreEqual(SaveSchemaMigrator.CURRENT_SCHEMA, migrated.schemaVersion,
                "Re-entering Play repeatedly must not duplicate or corrupt the chain.");
        }
    }
}
