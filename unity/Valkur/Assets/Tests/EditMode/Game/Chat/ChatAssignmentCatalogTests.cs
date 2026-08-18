using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Locks in the contract of <see cref="ChatAssignmentCatalog"/> (entity-name to persona
    /// lookup plus its lazily built cache) and of <see cref="NPCPersonaDefinition"/>
    /// (vendor discount-limit resolution and the Python-parity defaults).
    ///
    /// Why it matters: the catalog is the only thing connecting an NPC in the world to the
    /// persona that drives its dialogue. A regression here is silent — the NPC simply resolves
    /// to "no persona" and falls back to generic behaviour — so the awkward paths (missing name,
    /// wrong casing, duplicate rows, rows whose persona asset was deleted, and the cache going
    /// stale after the list is mutated) are all pinned down explicitly.
    ///
    /// Both types are ScriptableObjects: every instance created here is tracked and destroyed
    /// with DestroyImmediate in TearDown (Destroy is illegal in edit mode).
    /// </summary>
    [TestFixture]
    public class ChatAssignmentCatalogTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var so in _created)
            {
                // Unity's overloaded == also covers assets already destroyed inside a test.
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            }
            _created.Clear();
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private ChatAssignmentCatalog NewCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<ChatAssignmentCatalog>();
            _created.Add(catalog);
            return catalog;
        }

        private NPCPersonaDefinition NewPersona(string personaId)
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            persona.personaId = personaId;
            persona.displayName = personaId;
            _created.Add(persona);
            return persona;
        }

        private static void AddAssignment(
            ChatAssignmentCatalog catalog, string entityName, NPCPersonaDefinition persona)
        {
            catalog.assignments.Add(new ChatAssignmentCatalog.ChatAssignment
            {
                entityName = entityName,
                persona = persona
            });
        }

        private static void AddDiscount(NPCPersonaDefinition persona, string itemKey, float max)
        {
            persona.discountLimits.Add(new NPCPersonaDefinition.DiscountEntry
            {
                itemKey = itemKey,
                maxDiscount = max
            });
        }

        // ── ChatAssignmentCatalog: basic lookup ──────────────────────────────────

        [Test]
        public void GetPersona_ExistingEntityName_ReturnsTheAssignedInstance()
        {
            var catalog = NewCatalog();
            var gatita = NewPersona("vendor_cheff_gatita");
            var guard = NewPersona("guard_generic");
            AddAssignment(catalog, "Cheff Gatita", gatita);
            AddAssignment(catalog, "Town Guard", guard);

            Assert.AreSame(gatita, catalog.GetPersona("Cheff Gatita"),
                "The catalog must return the very same persona instance that was assigned, " +
                "not a copy or a different row.");
            Assert.AreSame(guard, catalog.GetPersona("Town Guard"),
                "Every registered row must be reachable, not only the first one.");
        }

        [Test]
        public void GetPersona_UnknownEntityName_ReturnsNull()
        {
            var catalog = NewCatalog();
            AddAssignment(catalog, "Cheff Gatita", NewPersona("vendor_cheff_gatita"));

            Assert.IsNull(catalog.GetPersona("Nobody"),
                "An unmapped entity name must resolve to null so callers fall back to generic " +
                "dialogue instead of receiving a wrong persona.");
        }

        [Test]
        public void GetPersona_EmptyCatalog_ReturnsNullWithoutThrowing()
        {
            var catalog = NewCatalog();

            Assert.IsNotNull(catalog.assignments,
                "The assignments list must be initialised on a fresh instance; a null list would " +
                "make RebuildLookup throw on the first lookup.");
            Assert.IsNull(catalog.GetPersona("Anyone"),
                "A freshly created catalog must lazily build an empty lookup and return null " +
                "rather than NRE on the very first call.");
        }

        [Test]
        public void GetPersona_EmptyEntityName_ReturnsNull()
        {
            var catalog = NewCatalog();
            // Rows with an empty entity name are intentionally never registered.
            AddAssignment(catalog, string.Empty, NewPersona("orphan"));
            AddAssignment(catalog, "Cheff Gatita", NewPersona("vendor_cheff_gatita"));

            Assert.IsNull(catalog.GetPersona(string.Empty),
                "An empty entity name must never match: unnamed rows are skipped while the " +
                "lookup is built, so querying the empty string must not resolve the orphan row.");
        }

        [Test]
        public void GetPersona_NullEntityName_ThrowsArgumentNullException()
        {
            var catalog = NewCatalog();
            AddAssignment(catalog, "Cheff Gatita", NewPersona("vendor_cheff_gatita"));

            // Current behaviour: GetPersona forwards the key straight into
            // Dictionary.TryGetValue, which rejects a null key. Callers MUST null-check the
            // entity name before asking the catalog. If a null guard is ever added inside
            // GetPersona, this is the place to relax the contract to "returns null".
            Assert.Throws<ArgumentNullException>(() => catalog.GetPersona(null),
                "GetPersona(null) currently propagates ArgumentNullException from the backing " +
                "dictionary; nothing swallows it, and callers depend on that being explicit.");
        }

        // ── ChatAssignmentCatalog: key matching semantics ────────────────────────

        [Test]
        public void GetPersona_EntityNameDifferingOnlyInCase_ReturnsNull()
        {
            var catalog = NewCatalog();
            var gatita = NewPersona("vendor_cheff_gatita");
            AddAssignment(catalog, "Cheff Gatita", gatita);

            // The lookup uses a plain Dictionary<string, …> with the default comparer, i.e.
            // ordinal and CASE-SENSITIVE. This deliberately differs from the project's zone-name
            // convention (OrdinalIgnoreCase); assignment rows must match the NPC display name
            // exactly.
            Assert.IsNull(catalog.GetPersona("cheff gatita"),
                "Entity-name lookup is case-sensitive today. If the comparer ever becomes " +
                "OrdinalIgnoreCase this test must be updated deliberately, not by accident.");
            Assert.AreSame(gatita, catalog.GetPersona("Cheff Gatita"),
                "The exactly cased key must still resolve.");
        }

        [Test]
        public void GetPersona_EntityNameWithSurroundingWhitespace_ReturnsNull()
        {
            var catalog = NewCatalog();
            AddAssignment(catalog, "Cheff Gatita", NewPersona("vendor_cheff_gatita"));

            Assert.IsNull(catalog.GetPersona(" Cheff Gatita "),
                "Neither the stored key nor the query is trimmed. A regression that starts " +
                "trimming (or stops) would silently change which NPCs get a persona.");
        }

        [Test]
        public void GetPersona_NonAsciiAndVeryLongEntityNames_MatchExactly()
        {
            var catalog = NewCatalog();
            // Accented key: the same literal is used for the row and the query, so the test
            // asserts exact matching regardless of how the source file is encoded.
            const string accented = "Señor Ñandú";
            string veryLong = new string('n', 4096) + "-tail";

            var accentedPersona = NewPersona("accented");
            var longPersona = NewPersona("long");
            AddAssignment(catalog, accented, accentedPersona);
            AddAssignment(catalog, veryLong, longPersona);

            Assert.AreSame(accentedPersona, catalog.GetPersona(accented),
                "Non-ASCII entity names must round-trip through the lookup untouched — NPC " +
                "display names in this project are Spanish and carry accents.");
            Assert.AreSame(longPersona, catalog.GetPersona(veryLong),
                "Very long keys must resolve too — nothing in the path may truncate them.");
            Assert.IsNull(catalog.GetPersona(new string('n', 4096)),
                "A prefix of a registered long key must NOT match — comparison is full-string, " +
                "never prefix-based.");
        }

        // ── ChatAssignmentCatalog: malformed rows ────────────────────────────────

        [Test]
        public void GetPersona_DuplicateEntityNames_LastRowWins()
        {
            var catalog = NewCatalog();
            var first = NewPersona("first");
            var second = NewPersona("second");
            AddAssignment(catalog, "Duplicate", first);
            AddAssignment(catalog, "Duplicate", second);

            // Indexer assignment, not Add() — duplicates overwrite instead of throwing.
            Assert.AreSame(second, catalog.GetPersona("Duplicate"),
                "Duplicated entity names must not throw; the last row in the list wins. Switching " +
                "to Dictionary.Add() would turn a designer typo into a runtime exception.");
        }

        [Test]
        public void RebuildLookup_RowWithNullPersona_IsSkipped()
        {
            var catalog = NewCatalog();
            AddAssignment(catalog, "Ghost", null);

            Assert.IsNull(catalog.GetPersona("Ghost"),
                "A row whose persona field was left empty must not be registered, otherwise " +
                "callers receive a null persona for a name that looks mapped.");
        }

        [Test]
        public void RebuildLookup_DuplicateRowWithNullPersona_DoesNotEraseTheValidRow()
        {
            var catalog = NewCatalog();
            var valid = NewPersona("valid");
            AddAssignment(catalog, "Cheff Gatita", valid);
            AddAssignment(catalog, "Cheff Gatita", null); // half-filled duplicate row

            Assert.AreSame(valid, catalog.GetPersona("Cheff Gatita"),
                "An empty duplicate row must be skipped entirely — it must not overwrite the " +
                "valid mapping with null.");
        }

        [Test]
        public void RebuildLookup_RowsWithNullOrEmptyEntityName_AreSkippedWithoutThrowing()
        {
            var catalog = NewCatalog();
            AddAssignment(catalog, null, NewPersona("null_name"));
            AddAssignment(catalog, string.Empty, NewPersona("empty_name"));
            var valid = NewPersona("valid");
            AddAssignment(catalog, "Cheff Gatita", valid);

            Assert.DoesNotThrow(catalog.RebuildLookup,
                "A null entity name must be filtered out before it reaches the dictionary — a " +
                "null dictionary key would throw while building the cache.");
            Assert.AreSame(valid, catalog.GetPersona("Cheff Gatita"),
                "Malformed rows must not stop the well-formed rows after them from registering.");
        }

        [Test]
        public void RebuildLookup_PersonaAssetDestroyed_RowIsSkipped()
        {
            var catalog = NewCatalog();
            var doomed = NewPersona("doomed");
            AddAssignment(catalog, "Cheff Gatita", doomed);

            UnityEngine.Object.DestroyImmediate(doomed);

            Assert.DoesNotThrow(catalog.RebuildLookup,
                "Rebuilding with a destroyed persona asset must not throw.");
            Assert.IsNull(catalog.GetPersona("Cheff Gatita"),
                "The null check uses Unity's overloaded operator, so a destroyed (fake-null) " +
                "asset must be filtered out. Rewriting it as ((object)a.persona != null) would " +
                "leak a dead asset to the chat system.");
        }

        [Test]
        public void GetPersona_SamePersonaSharedByManyEntityNames_AllResolve()
        {
            var catalog = NewCatalog();
            var shared = NewPersona("guard_generic");
            AddAssignment(catalog, "Guard A", shared);
            AddAssignment(catalog, "Guard B", shared);

            Assert.AreSame(shared, catalog.GetPersona("Guard A"),
                "One persona must be assignable to several NPCs — many-to-one is the normal case " +
                "for generic guards and villagers.");
            Assert.AreSame(shared, catalog.GetPersona("Guard B"),
                "The second name sharing the persona must resolve to the same instance.");
        }

        // ── ChatAssignmentCatalog: lazy cache lifecycle ──────────────────────────

        [Test]
        public void GetPersona_ListMutatedAfterFirstLookup_ReturnsStaleResultUntilRebuild()
        {
            var catalog = NewCatalog();
            var first = NewPersona("first");
            AddAssignment(catalog, "First", first);

            // Force the lazy cache to materialise.
            Assert.AreSame(first, catalog.GetPersona("First"));

            var late = NewPersona("late");
            AddAssignment(catalog, "Late", late);

            Assert.IsNull(catalog.GetPersona("Late"),
                "The cache is built once and is NOT invalidated by mutating the list from code " +
                "(OnValidate only fires from the Inspector). Any code editing assignments at " +
                "runtime must call RebuildLookup() itself.");

            catalog.RebuildLookup();

            Assert.AreSame(late, catalog.GetPersona("Late"),
                "After an explicit RebuildLookup the newly added row must become visible.");
            Assert.AreSame(first, catalog.GetPersona("First"),
                "Rebuilding must not lose rows that were already registered.");
        }

        [Test]
        public void RebuildLookup_AfterRowRemoved_DropsTheStaleKey()
        {
            var catalog = NewCatalog();
            var keep = NewPersona("keep");
            var drop = NewPersona("drop");
            AddAssignment(catalog, "Keep", keep);
            AddAssignment(catalog, "Drop", drop);
            catalog.RebuildLookup();
            Assert.AreSame(drop, catalog.GetPersona("Drop"));

            catalog.assignments.RemoveAt(1);
            catalog.RebuildLookup();

            Assert.IsNull(catalog.GetPersona("Drop"),
                "RebuildLookup must start from a fresh dictionary. An incremental rebuild would " +
                "keep serving personas for rows the designer deleted.");
            Assert.AreSame(keep, catalog.GetPersona("Keep"),
                "The surviving row must still resolve after the rebuild.");
        }

        [Test]
        public void RebuildLookup_CalledRepeatedly_IsIdempotent()
        {
            var catalog = NewCatalog();
            var gatita = NewPersona("vendor_cheff_gatita");
            AddAssignment(catalog, "Cheff Gatita", gatita);

            for (int i = 0; i < 3; i++) catalog.RebuildLookup();

            Assert.AreSame(gatita, catalog.GetPersona("Cheff Gatita"),
                "Rebuilding several times must not throw on a duplicate key nor change the " +
                "resolved persona — hot reload and OnValidate can trigger extra rebuilds.");
        }

        // ── NPCPersonaDefinition: defaults ───────────────────────────────────────

        [Test]
        public void NewPersona_Defaults_MatchPythonParityValues()
        {
            var persona = NewPersona("fresh");

            Assert.AreEqual("generic", persona.role,
                "role must default to 'generic'; an empty role breaks the vendor / quest-giver " +
                "branch checks that read it.");
            Assert.AreEqual(10f, persona.chatRange, 0.0001f,
                "chatRange must default to the Python value of 10 world units — a 0 default " +
                "would make every NPC unreachable for chat.");
            Assert.AreEqual(3, persona.maxSentences,
                "maxSentences must default to 3 (Python parity for reply length).");
            Assert.AreEqual("medium", persona.verbosity,
                "verbosity must default to 'medium'.");
            Assert.IsTrue(persona.useEmoji,
                "useEmoji must default to true (Python parity).");
            Assert.IsNotNull(persona.allowedItemTypes,
                "allowedItemTypes must be initialised so vendor code can iterate it without a " +
                "null check.");
            Assert.IsNotNull(persona.discountLimits,
                "discountLimits must be initialised — GetDiscountLimit iterates it directly.");
            Assert.IsNotNull(persona.dialogueLines,
                "dialogueLines must be initialised so the offline dialogue provider can index it.");
        }

        // ── NPCPersonaDefinition: GetDiscountLimit ───────────────────────────────

        [Test]
        public void GetDiscountLimit_NoEntries_ReturnsPythonFallback()
        {
            var persona = NewPersona("vendor");

            Assert.AreEqual(0.05f, persona.GetDiscountLimit("sword"), 0.0001f,
                "With no discount rows the hard fallback of 0.05 (Python parity) must be " +
                "returned — never 0, which would forbid haggling entirely.");
        }

        [Test]
        public void GetDiscountLimit_ExactKey_ReturnsThatEntryLimit()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "default", 0.10f);
            AddDiscount(persona, "sword", 0.25f);

            Assert.AreEqual(0.25f, persona.GetDiscountLimit("sword"), 0.0001f,
                "An exact item key must win over the generic 'default' cap.");
        }

        [Test]
        public void GetDiscountLimit_ExactKeyListedAfterDefault_StillWinsOverDefault()
        {
            var persona = NewPersona("vendor");
            // 'default' deliberately placed FIRST: the exact-match pass must scan the whole list
            // before the fallback pass runs, so list order must not decide the result.
            AddDiscount(persona, "default", 0.10f);
            AddDiscount(persona, "potion", 0.40f);

            Assert.AreEqual(0.40f, persona.GetDiscountLimit("potion"), 0.0001f,
                "Resolution is two-pass (exact first, then 'default'). Collapsing it into a " +
                "single pass would let a leading 'default' row shadow every specific cap.");
        }

        [Test]
        public void GetDiscountLimit_UnknownKey_FallsBackToDefaultEntry()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "sword", 0.25f);
            AddDiscount(persona, "default", 0.10f);

            Assert.AreEqual(0.10f, persona.GetDiscountLimit("shield"), 0.0001f,
                "An unlisted item must fall back to the 'default' row — not to the hard-coded " +
                "0.05, and never to an unrelated item's cap.");
        }

        [Test]
        public void GetDiscountLimit_DefaultKeyQueriedDirectly_ReturnsDefaultEntry()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "default", 0.10f);

            Assert.AreEqual(0.10f, persona.GetDiscountLimit("default"), 0.0001f,
                "Querying the literal key 'default' must resolve through the exact-match pass.");
        }

        [Test]
        public void GetDiscountLimit_KeyDifferingOnlyInCase_FallsBackToDefault()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "sword", 0.25f);
            AddDiscount(persona, "default", 0.10f);

            // Item keys are compared with ordinal string equality, so casing matters here too.
            Assert.AreEqual(0.10f, persona.GetDiscountLimit("Sword"), 0.0001f,
                "Item-key matching is case-sensitive: a row authored as 'sword' does not match a " +
                "caller passing 'Sword'. Pinned so any move to a case-insensitive compare is a " +
                "deliberate change.");
        }

        [Test]
        public void GetDiscountLimit_DuplicateKeys_FirstRowWins()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "sword", 0.25f);
            AddDiscount(persona, "sword", 0.50f);

            Assert.AreEqual(0.25f, persona.GetDiscountLimit("sword"), 0.0001f,
                "With duplicate item keys the FIRST row wins (the linear scan returns early). " +
                "That is the opposite of the catalog's last-row-wins rule, so it is pinned here.");
        }

        [Test]
        public void GetDiscountLimit_NullKey_FallsBackWithoutThrowing()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "sword", 0.25f);
            AddDiscount(persona, "default", 0.10f);

            float limit = 0f;
            Assert.DoesNotThrow(() => limit = persona.GetDiscountLimit(null),
                "A null item key must not throw — vendor code may pass an unresolved key.");
            Assert.AreEqual(0.10f, limit, 0.0001f,
                "A null key matches no row, so it must land on the 'default' cap.");
        }

        [Test]
        public void GetDiscountLimit_NullKeyAndNoDefaultRow_ReturnsPythonFallback()
        {
            var persona = NewPersona("vendor");
            AddDiscount(persona, "sword", 0.25f);

            Assert.AreEqual(0.05f, persona.GetDiscountLimit(null), 0.0001f,
                "Without a 'default' row a null key must still resolve to the 0.05 fallback " +
                "instead of throwing or returning 0.");
        }

        [Test]
        public void GetDiscountLimit_ValueOutsideInspectorRange_IsReturnedUnclamped()
        {
            var persona = NewPersona("vendor");
            // [Range(0f, 0.5f)] only constrains the Inspector slider; code-assigned values pass
            // straight through.
            AddDiscount(persona, "sword", 0.9f);

            Assert.AreEqual(0.9f, persona.GetDiscountLimit("sword"), 0.0001f,
                "GetDiscountLimit performs no clamping — callers must not assume the returned " +
                "value sits inside the [0, 0.5] Inspector range.");
        }
    }
}
