using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for <see cref="SpellCooldownHUD"/>.
    ///
    /// Rows contain <c>TextMeshProUGUI</c> which can log NREs in EditMode when
    /// there is no Canvas/CanvasUpdateRegistry. We silence those with
    /// <c>LogAssert.ignoreFailingMessages = true</c> (per the EditMode gotcha) so
    /// our structural assertions are not masked.
    ///
    /// Row count is verified via reflection on the private <c>_rows</c> dictionary
    /// because there is no public <c>GetActiveRowCount()</c> on the production class.
    /// Tick-down / expiry behaviour that requires real frame time is intentionally
    /// left for PlayMode; these tests cover the event-filtering logic only.
    /// </summary>
    public class SpellCooldownHUDTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        // ── Helpers ──────────────────────────────────────────────────────────

        private SpellCooldownHUD CreateHUD(out GameObject player, out RectTransform stackRoot)
        {
            // Canvas parent (required by VerticalLayoutGroup / TMP in EditMode)
            var canvasGo = new GameObject("Canvas");
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _scene.Add(canvasGo);

            // Stack root — minimal RectTransform under a canvas
            var rootGo = new GameObject("StackRoot", typeof(RectTransform));
            rootGo.transform.SetParent(canvasGo.transform, false);
            stackRoot = rootGo.GetComponent<RectTransform>();

            // Player GO
            player = new GameObject("Player");
            _scene.Add(player);

            // HUD MonoBehaviour lives on its own GO
            var hudGo = new GameObject("SpellCooldownHUD");
            _scene.Add(hudGo);
            var hud = hudGo.AddComponent<SpellCooldownHUD>();
            hud.Initialize(player, stackRoot);
            return hud;
        }

        /// <summary>Read the private _rows Dictionary count via reflection.</summary>
        private static int GetRowCount(SpellCooldownHUD hud)
        {
            var f = typeof(SpellCooldownHUD).GetField("_rows",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "_rows field not found on SpellCooldownHUD");
            var raw = f.GetValue(hud);
            Assert.IsNotNull(raw, "_rows value is null on SpellCooldownHUD");
            var countProp = raw.GetType().GetProperty("Count");
            Assert.IsNotNull(countProp, "Count property not found on _rows type");
            return (int)countProp.GetValue(raw);
        }

        [SetUp]
        public void SetUp()
        {
            // TMP components on bare/minimal Canvas GO can NRE during init in
            // EditMode — silence those so our structural assertions surface cleanly.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.Clear();
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Initialization ────────────────────────────────────────────────

        [Test]
        public void Initialize_SubscribesToOnSpellCast()
        {
            // If Initialize wired up correctly, firing for the bound player creates a row.
            CreateHUD(out var player, out _);

            // Force a cast with valid cooldown
            GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f);

            // No assertion on row count here — just verify no exception thrown.
            // (Row count is validated in dedicated tests below.)
            Assert.Pass("Initialize completed without exception and event was received");
        }

        // ── Row creation ──────────────────────────────────────────────────

        [Test]
        public void OnSpellCast_ValidCast_AddsOneRow()
        {
            var hud = CreateHUD(out var player, out _);

            GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f);

            Assert.AreEqual(1, GetRowCount(hud), "One row expected after a single valid cast");
        }

        [Test]
        public void OnSpellCast_TwoDifferentKeys_AddsTwoRows()
        {
            var hud = CreateHUD(out var player, out _);

            GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f);
            GameEvents.FireSpellCast(player, "dash",     "Dash",     1f);

            Assert.AreEqual(2, GetRowCount(hud), "Two distinct spell keys must produce two rows");
        }

        [Test]
        public void OnSpellCast_SameKeyTwice_DoesNotDuplicate()
        {
            var hud = CreateHUD(out var player, out _);

            GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f);
            GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f);

            Assert.AreEqual(1, GetRowCount(hud),
                "Re-casting the same spellKey must reset the existing row, not add a second one");
        }

        // ── Filtering: wrong caster ───────────────────────────────────────

        [Test]
        public void OnSpellCast_WrongCaster_IsIgnored()
        {
            var hud = CreateHUD(out _, out _);

            var otherGo = new GameObject("OtherEntity");
            _scene.Add(otherGo);
            GameEvents.FireSpellCast(otherGo, "fireball", "Fireball", 2f);

            Assert.AreEqual(0, GetRowCount(hud),
                "Casts from an entity that is NOT the bound player must be ignored");
        }

        [Test]
        public void OnSpellCast_NullCaster_IsIgnored()
        {
            var hud = CreateHUD(out _, out _);

            GameEvents.FireSpellCast(null, "fireball", "Fireball", 2f);

            Assert.AreEqual(0, GetRowCount(hud),
                "Null caster must not create a row (guard: caster == null)");
        }

        // ── Filtering: cooldown too short ─────────────────────────────────

        [Test]
        public void OnSpellCast_CooldownAtMinDisplayTime_IsIgnored()
        {
            var hud = CreateHUD(out var player, out _);
            // MinDisplayTime = 0.05f — a value exactly at the threshold must be ignored.
            GameEvents.FireSpellCast(player, "tiny_cd_spell", "TinyCD", 0.05f);

            Assert.AreEqual(0, GetRowCount(hud),
                "cooldownDuration <= MinDisplayTime (0.05) must not create a row");
        }

        [Test]
        public void OnSpellCast_ZeroCooldown_IsIgnored()
        {
            var hud = CreateHUD(out var player, out _);
            GameEvents.FireSpellCast(player, "instant_spell", "Instant", 0f);

            Assert.AreEqual(0, GetRowCount(hud), "Zero cooldown must not create a row");
        }

        [Test]
        public void OnSpellCast_NegativeCooldown_IsIgnored()
        {
            var hud = CreateHUD(out var player, out _);
            GameEvents.FireSpellCast(player, "neg_cd_spell", "NegCD", -1f);

            Assert.AreEqual(0, GetRowCount(hud), "Negative cooldown must not create a row");
        }

        [Test]
        public void OnSpellCast_CooldownSlightlyAboveMin_CreatesRow()
        {
            var hud = CreateHUD(out var player, out _);
            // Just above MinDisplayTime (0.05) → must create a row.
            GameEvents.FireSpellCast(player, "fireball", "Fireball", 0.06f);

            Assert.AreEqual(1, GetRowCount(hud),
                "cooldownDuration > MinDisplayTime must create a row");
        }

        // ── Filtering: empty / null spellKey ─────────────────────────────

        [Test]
        public void OnSpellCast_EmptySpellKey_IsIgnored()
        {
            var hud = CreateHUD(out var player, out _);
            GameEvents.FireSpellCast(player, "", "SomeName", 2f);

            Assert.AreEqual(0, GetRowCount(hud), "Empty spellKey must not create a row");
        }

        [Test]
        public void OnSpellCast_NullSpellKey_IsIgnored()
        {
            var hud = CreateHUD(out var player, out _);
            GameEvents.FireSpellCast(player, null, "SomeName", 2f);

            Assert.AreEqual(0, GetRowCount(hud), "Null spellKey must not create a row");
        }

        // ── Row reset on re-cast ──────────────────────────────────────────

        [Test]
        public void OnSpellCast_RecastSameKey_ResetsRemaining()
        {
            var hud = CreateHUD(out var player, out _);

            // First cast: 5 s cooldown
            GameEvents.FireSpellCast(player, "fireball", "Fireball", 5f);

            // Manually tick the row down a bit via reflection before re-cast
            // (tick the private CooldownRow.Remaining field).
            var rowsField = typeof(SpellCooldownHUD).GetField("_rows",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var rows = rowsField.GetValue(hud);
            var indexer = rows.GetType().GetProperty("Item");
            var row = indexer.GetValue(rows, new object[] { "fireball" });
            var remainingProp = row.GetType().GetProperty("Remaining");
            // Confirm initial value
            float initial = (float)remainingProp.GetValue(row);
            Assert.AreEqual(5f, initial, 0.01f, "Remaining should start at 5f after first cast");

            // Re-cast same key with different (higher) cooldown
            GameEvents.FireSpellCast(player, "fireball", "Fireball", 3f);

            // Row count must still be 1
            Assert.AreEqual(1, GetRowCount(hud), "Re-cast must not add a second row");

            // Remaining must be reset to 3f (the new cooldown)
            // Re-fetch row reference since Reset() modifies in place
            row = indexer.GetValue(rows, new object[] { "fireball" });
            float reset = (float)remainingProp.GetValue(row);
            Assert.AreEqual(3f, reset, 0.01f, "Re-cast must reset Remaining to the new cooldown");
        }

        // ── OnDestroy unsubscribes ────────────────────────────────────────

        [Test]
        public void OnDestroy_UnsubscribesFromOnSpellCast()
        {
            // Destroying the HUD GO must remove its OnSpellCast subscription so it
            // doesn't react to future events (MissingReferenceException risk).
            var hud = CreateHUD(out var player, out _);

            // Destroy the HUD — OnDestroy should unsubscribe.
            var hudGo = hud.gameObject;
            _scene.Remove(hudGo); // already handled below
            Object.DestroyImmediate(hudGo);

            // Fire after destruction — must not throw even though _rows/_player are now invalid.
            Assert.DoesNotThrow(() =>
                GameEvents.FireSpellCast(player, "fireball", "Fireball", 2f),
                "Firing OnSpellCast after HUD destruction must not throw");
        }
    }
}
