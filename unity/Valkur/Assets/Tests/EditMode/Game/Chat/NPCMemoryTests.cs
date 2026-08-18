using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Tests for the in-memory <see cref="NPCMemory"/> model and its
    /// <see cref="EphemeralMessage"/> entries — the data contract shared with the
    /// Python <c>data/chat/memories/{npc-key}/memory.json</c> schema.
    ///
    /// This fixture deliberately touches NO disk: the sibling
    /// <c>NPCMemoryStoreTests</c> owns everything about atomic writes, backups and
    /// path routing. What is covered here is the part that silently breaks save
    /// files without any I/O failing:
    ///   * constructor defaults (a fresh record must already be a *valid* record),
    ///   * the EPHEMERAL_CAP rolling-window boundary — exactly at the cap, one over,
    ///     and far over — since an off-by-one here either leaks unbounded prompt
    ///     tokens to the LLM provider or silently truncates conversation context,
    ///   * who owns the trimming (the store, not the model),
    ///   * JsonUtility round-trips, including the null-string and missing-field
    ///     behaviours that decide whether an old save file still loads.
    /// </summary>
    [TestFixture]
    public class NPCMemoryTests
    {
        private NPCMemory _memory;

        [SetUp]
        public void SetUp()
        {
            // Nothing Unity-lifecycle-bound is created by this fixture (no
            // GameObjects, no ScriptableObjects, no files) — NPCMemory is a plain
            // [Serializable] class. The flag only guards against incidental engine
            // logs from JsonUtility failing an otherwise-passing assertion.
            LogAssert.ignoreFailingMessages = true;

            _memory = new NPCMemory();
        }

        [TearDown]
        public void TearDown()
        {
            _memory = null;
            LogAssert.ignoreFailingMessages = false;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>Appends <paramref name="count"/> messages named "msg-0".."msg-(n-1)".</summary>
        private static void AppendNumbered(NPCMemory mem, int count)
        {
            for (int i = 0; i < count; i++)
                NPCMemoryStore.AppendEphemeral(mem, "user", "msg-" + i);
        }

        // ── constructor defaults ──────────────────────────────────────────────

        [Test]
        public void Constructor_Default_ProducesUsableRecordWithSpanishDefault()
        {
            // A record built directly with `new` (not via NPCMemoryStore.CreateFresh)
            // must already be safe to append to and to show to the player.
            Assert.AreEqual(1, _memory.schemaVersion,
                "schemaVersion must default to 1; a 0 default would make every fresh record look stale and trigger a pointless migration.");
            Assert.AreEqual("es", _memory.preferredLanguage,
                "preferredLanguage must default to 'es' — the Python schema default. A null/'en' default would flip every new NPC to the wrong language.");
            Assert.IsNotNull(_memory.ephemeralHistory,
                "ephemeralHistory must be initialised by the field initialiser; a null list would NRE inside AppendEphemeral on the very first message.");
            Assert.AreEqual(0, _memory.ephemeralHistory.Count,
                "A fresh record must start with no conversation history.");
            Assert.AreEqual(0, _memory.visitCount, "visitCount must start at 0.");
            Assert.IsFalse(_memory.hasGreeted,
                "hasGreeted must start false, otherwise the one-time greeting is skipped for brand-new NPCs.");
            Assert.AreEqual(0, _memory.friendshipScore,
                "friendshipScore must start neutral (0), not at either end of the -100..100 range.");
            Assert.IsNull(_memory.npcKey, "npcKey is assigned by the store, not defaulted to a placeholder.");
            Assert.IsNull(_memory.personaId, "personaId is assigned by the store, not defaulted to a placeholder.");
        }

        [Test]
        public void Constructor_TwoInstances_DoNotShareEphemeralList()
        {
            var other = new NPCMemory();

            NPCMemoryStore.AppendEphemeral(_memory, "user", "only-for-first");

            Assert.AreNotSame(_memory.ephemeralHistory, other.ephemeralHistory,
                "Each NPCMemory must own its list. A shared/static list would cross-contaminate every NPC's conversation.");
            Assert.AreEqual(0, other.ephemeralHistory.Count,
                "Appending to one NPC's memory must not touch another NPC's history.");
        }

        [Test]
        public void SchemaVersionDefault_MatchesStoreCurrentVersion()
        {
            // Tripwire: if CURRENT_SCHEMA_VERSION is bumped without updating the
            // field default, every `new NPCMemory()` is born "outdated" and takes a
            // migration path that was written for genuinely old files.
            Assert.AreEqual(NPCMemoryStore.CURRENT_SCHEMA_VERSION, new NPCMemory().schemaVersion,
                "NPCMemory.schemaVersion default and NPCMemoryStore.CURRENT_SCHEMA_VERSION must be bumped together — update this test deliberately when adding a migration.");
        }

        [Test]
        public void EphemeralCap_Value_IsTwelveForPythonParity()
        {
            // Pinned, not derived: the Python memory.json contract and the LLM
            // prompt-token budget both assume a 12-message window. Changing it
            // silently changes per-request cost and breaks cross-implementation parity.
            Assert.AreEqual(12, NPCMemory.EPHEMERAL_CAP,
                "EPHEMERAL_CAP is a cross-language contract (Python ephemeral_history) and a prompt-cost knob — changing it must be an explicit decision.");
        }

        // ── cap boundary: at / one over / far over ────────────────────────────

        [Test]
        public void AppendEphemeral_ExactlyAtCap_KeepsEveryMessage()
        {
            AppendNumbered(_memory, NPCMemory.EPHEMERAL_CAP);

            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP, _memory.ephemeralHistory.Count,
                "Filling the window exactly to the cap must not drop anything — an off-by-one (>= instead of >) would lose the oldest message one append early.");
            Assert.AreEqual("msg-0", _memory.ephemeralHistory[0].content,
                "At exactly the cap the very first message must still be present.");
            Assert.AreEqual("msg-" + (NPCMemory.EPHEMERAL_CAP - 1),
                _memory.ephemeralHistory[NPCMemory.EPHEMERAL_CAP - 1].content,
                "At exactly the cap the last message must be the most recent append.");
        }

        [Test]
        public void AppendEphemeral_OneOverCap_DropsExactlyTheOldest()
        {
            AppendNumbered(_memory, NPCMemory.EPHEMERAL_CAP + 1);

            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP, _memory.ephemeralHistory.Count,
                "One append past the cap must trim back to exactly the cap, never to cap-1 or cap+1.");
            Assert.AreEqual("msg-1", _memory.ephemeralHistory[0].content,
                "Exactly one message (msg-0) must be dropped — trimming from the wrong end would drop the newest instead.");
            Assert.AreEqual("msg-" + NPCMemory.EPHEMERAL_CAP,
                _memory.ephemeralHistory[NPCMemory.EPHEMERAL_CAP - 1].content,
                "The newest message must survive the trim and sit last.");
        }

        [Test]
        public void AppendEphemeral_FarOverCap_RetainsOnlyTheLastWindowInOrder()
        {
            int total = NPCMemory.EPHEMERAL_CAP * 5; // 60 appends into a 12-slot window
            AppendNumbered(_memory, total);

            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP, _memory.ephemeralHistory.Count,
                "The window must stay bounded no matter how long the conversation runs — a `if` instead of a `while` would let it grow past the cap.");

            // The surviving window must be the contiguous, chronologically ordered
            // tail [total-CAP .. total-1] — not a shuffled or reversed subset.
            int firstSurviving = total - NPCMemory.EPHEMERAL_CAP;
            for (int i = 0; i < NPCMemory.EPHEMERAL_CAP; i++)
            {
                Assert.AreEqual("msg-" + (firstSurviving + i), _memory.ephemeralHistory[i].content,
                    $"Slot {i} must hold msg-{firstSurviving + i}: the retained window must be the contiguous newest messages, oldest-first.");
            }
        }

        [Test]
        public void EphemeralHistory_DirectAddBeyondCap_DoesNotSelfTrim()
        {
            // Contract: NPCMemory is a dumb DTO. Capping belongs to
            // NPCMemoryStore.AppendEphemeral. If someone swaps the plain List for a
            // self-capping collection, callers that bypass the store would silently
            // change behaviour — this test makes that visible.
            int over = NPCMemory.EPHEMERAL_CAP + 5;
            for (int i = 0; i < over; i++)
                _memory.ephemeralHistory.Add(new EphemeralMessage { role = "user", content = "raw-" + i });

            Assert.AreEqual(over, _memory.ephemeralHistory.Count,
                "The model itself must not enforce the cap — trimming is the store's job, and hiding it in the model would double-trim.");
        }

        // ── awkward inputs ────────────────────────────────────────────────────

        [Test]
        public void AppendEphemeral_NullAndEmptyContent_StoredVerbatimWithoutThrowing()
        {
            Assert.DoesNotThrow(() => NPCMemoryStore.AppendEphemeral(_memory, null, null),
                "A null role/content must not throw — chat providers can legitimately return an empty completion.");
            Assert.DoesNotThrow(() => NPCMemoryStore.AppendEphemeral(_memory, "assistant", string.Empty),
                "An empty completion must not throw.");

            Assert.AreEqual(2, _memory.ephemeralHistory.Count,
                "Empty/null messages are stored, not silently discarded — discarding them would desync the user/assistant turn pairing.");
            Assert.IsNull(_memory.ephemeralHistory[0].content,
                "In memory a null content stays null (it only becomes \"\" after a JsonUtility round-trip).");
            Assert.AreEqual(string.Empty, _memory.ephemeralHistory[1].content,
                "An empty string must be preserved as an empty string.");
        }

        [Test]
        public void AppendEphemeral_DuplicateContent_KeepsBothEntries()
        {
            NPCMemoryStore.AppendEphemeral(_memory, "user", "hola");
            NPCMemoryStore.AppendEphemeral(_memory, "user", "hola");

            Assert.AreEqual(2, _memory.ephemeralHistory.Count,
                "Repeated identical messages must both be kept — de-duplicating would erase the fact that the player said it twice and break turn pairing.");
        }

        [Test]
        public void AppendEphemeral_Timestamp_IsRoundTrippableUtcIso8601()
        {
            NPCMemoryStore.AppendEphemeral(_memory, "user", "when?");
            string stamp = _memory.ephemeralHistory[0].timestampIso8601;

            Assert.IsFalse(string.IsNullOrEmpty(stamp), "Every ephemeral message must carry a timestamp.");

            DateTime parsed;
            bool ok = DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out parsed);

            Assert.IsTrue(ok,
                $"Timestamp '{stamp}' must parse as round-trippable ISO-8601 — a culture-dependent format would be unreadable by the Python side.");
            Assert.AreEqual(DateTimeKind.Utc, parsed.Kind,
                "Timestamps must be UTC ('o' on DateTime.UtcNow); a local-time stamp would jump when the player changes time zone.");
            Assert.Less(Math.Abs((DateTime.UtcNow - parsed).TotalMinutes), 5.0,
                "The timestamp must be the moment of the append, not a default/epoch value.");
        }

        // ── struct semantics ──────────────────────────────────────────────────

        [Test]
        public void EphemeralMessage_IsValueType_MutatingACopyLeavesTheListEntryIntact()
        {
            NPCMemoryStore.AppendEphemeral(_memory, "user", "original");

            EphemeralMessage copy = _memory.ephemeralHistory[0]; // value copy
            copy.content = "mutated";

            Assert.IsTrue(typeof(EphemeralMessage).IsValueType,
                "EphemeralMessage is documented as a struct to avoid heap pressure; turning it into a class changes assignment semantics everywhere it is read out of the list.");
            Assert.AreEqual("original", _memory.ephemeralHistory[0].content,
                "Reading an entry yields a copy — a caller editing it must not mutate stored history. This is the same pit trap as InventorySlot.");
        }

        // ── JsonUtility serialisation ─────────────────────────────────────────

        [Test]
        public void JsonRoundTrip_FullRecordAtCap_PreservesEveryFieldAndOrder()
        {
            _memory.npcKey = "blacksmith-42";
            _memory.personaId = "persona-blacksmith";
            _memory.visitCount = 9;
            _memory.hasGreeted = true;
            _memory.friendshipScore = -37;
            _memory.preferredLanguage = "en";
            _memory.lastUpdatedIso8601 = "2026-08-18T10:11:12.0000000Z";
            AppendNumbered(_memory, NPCMemory.EPHEMERAL_CAP);

            string json = JsonUtility.ToJson(_memory);
            var loaded = JsonUtility.FromJson<NPCMemory>(json);

            Assert.AreEqual("blacksmith-42", loaded.npcKey, "npcKey must survive serialisation — it is the file identity.");
            Assert.AreEqual("persona-blacksmith", loaded.personaId, "personaId must survive serialisation.");
            Assert.AreEqual(9, loaded.visitCount, "visitCount must survive serialisation.");
            Assert.IsTrue(loaded.hasGreeted, "hasGreeted must survive serialisation.");
            Assert.AreEqual(-37, loaded.friendshipScore,
                "A negative friendshipScore must survive — sign loss would silently reset hostile relationships.");
            Assert.AreEqual("en", loaded.preferredLanguage, "preferredLanguage must survive serialisation.");
            Assert.AreEqual(1, loaded.schemaVersion, "schemaVersion must survive serialisation, otherwise every load re-migrates.");
            Assert.AreEqual("2026-08-18T10:11:12.0000000Z", loaded.lastUpdatedIso8601, "lastUpdatedIso8601 must survive serialisation.");

            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP, loaded.ephemeralHistory.Count,
                "A full window must round-trip without losing entries.");
            for (int i = 0; i < NPCMemory.EPHEMERAL_CAP; i++)
            {
                Assert.AreEqual("msg-" + i, loaded.ephemeralHistory[i].content,
                    $"Slot {i} must keep its content and position — JsonUtility list order is the conversation order fed to the LLM.");
                Assert.AreEqual("user", loaded.ephemeralHistory[i].role,
                    $"Slot {i} must keep its role; a lost role turns the whole transcript into one speaker.");
                Assert.IsFalse(string.IsNullOrEmpty(loaded.ephemeralHistory[i].timestampIso8601),
                    $"Slot {i} must keep its timestamp.");
            }
        }

        [Test]
        public void JsonRoundTrip_NullStringFields_BecomeEmptyStrings()
        {
            // Pinned JsonUtility behaviour: null strings serialise as "" and come
            // back as "", never as null. Code that does `if (npcKey == null)` after a
            // load is therefore dead — it must test string.IsNullOrEmpty instead.
            _memory.npcKey = null;
            _memory.personaId = null;
            _memory.preferredLanguage = null;
            NPCMemoryStore.AppendEphemeral(_memory, null, null);

            var loaded = JsonUtility.FromJson<NPCMemory>(JsonUtility.ToJson(_memory));

            Assert.AreEqual(string.Empty, loaded.npcKey,
                "JsonUtility turns a null string into \"\" — null-checks after load never fire.");
            Assert.AreEqual(string.Empty, loaded.personaId, "Same null-to-empty conversion for personaId.");
            Assert.AreEqual(string.Empty, loaded.preferredLanguage,
                "A null preferredLanguage round-trips to \"\", NOT back to the 'es' default — callers must treat empty as 'unset'.");
            Assert.AreEqual(string.Empty, loaded.ephemeralHistory[0].content,
                "Null content inside a serialised struct also comes back empty.");
        }

        [Test]
        public void JsonRoundTrip_UnicodeAndVeryLongContent_PreservedExactly()
        {
            // Spanish accents + inverted punctuation, CJK, a surrogate-pair emoji,
            // an embedded quote/backslash/newline, then a 20k-char message.
            const string tricky = "¡Hola! ¿Qué tal, señor? "
                                + "你好 \U0001F525 \"quoted\" back\\slash\nnewline\ttab";
            string huge = new string('ñ', 20000);

            NPCMemoryStore.AppendEphemeral(_memory, "assistant", tricky);
            NPCMemoryStore.AppendEphemeral(_memory, "user", huge);
            _memory.npcKey = "herrero-ñoño";

            var loaded = JsonUtility.FromJson<NPCMemory>(JsonUtility.ToJson(_memory));

            Assert.AreEqual(tricky, loaded.ephemeralHistory[0].content,
                "Accents, CJK, surrogate-pair emoji and escaped characters must survive verbatim — mangling here corrupts saved dialogue for every non-English player.");
            Assert.AreEqual(20000, loaded.ephemeralHistory[1].content.Length,
                "A very long message must not be truncated by serialisation.");
            Assert.AreEqual(huge, loaded.ephemeralHistory[1].content,
                "The long message must be byte-identical, not just the same length.");
            Assert.AreEqual("herrero-ñoño", loaded.npcKey,
                "Non-ASCII npcKeys must survive (slugification happens at the path layer, not in the record).");
        }

        [Test]
        public void FromJson_MissingFields_KeepsConstructorDefaults()
        {
            // Simulates an older / hand-trimmed memory.json that predates some
            // fields. JsonUtility only overwrites the fields present in the JSON,
            // so the field initialisers must carry the rest.
            var loaded = JsonUtility.FromJson<NPCMemory>("{\"npcKey\":\"legacy-npc\"}");

            Assert.IsNotNull(loaded, "A partial record must still deserialise rather than returning null.");
            Assert.AreEqual("legacy-npc", loaded.npcKey, "The one present field must be applied.");
            Assert.AreEqual("es", loaded.preferredLanguage,
                "A record written before preferredLanguage existed must fall back to 'es', not to null.");
            Assert.IsNotNull(loaded.ephemeralHistory,
                "A record with no ephemeralHistory key must still get a usable list — a null here NREs on the next AppendEphemeral.");
            Assert.AreEqual(0, loaded.ephemeralHistory.Count, "The recovered list must start empty.");
            Assert.AreEqual(0, loaded.visitCount, "Absent numeric fields fall back to 0.");
        }

        [Test]
        public void FromJson_EmptyHistoryArray_ProducesEmptyNonNullList()
        {
            var loaded = JsonUtility.FromJson<NPCMemory>(
                "{\"npcKey\":\"quiet-npc\",\"ephemeralHistory\":[]}");

            Assert.IsNotNull(loaded.ephemeralHistory,
                "An explicitly empty array must deserialise to an empty list, never to null.");
            Assert.AreEqual(0, loaded.ephemeralHistory.Count, "An empty array must not invent entries.");

            Assert.DoesNotThrow(() => NPCMemoryStore.AppendEphemeral(loaded, "user", "first"),
                "The deserialised record must be immediately appendable — this is the exact path an NPC with a saved-but-empty history takes.");
            Assert.AreEqual(1, loaded.ephemeralHistory.Count, "The append must land in the recovered list.");
        }

        [Test]
        public void EphemeralMessage_StandaloneJsonRoundTrip_PreservesAllThreeFields()
        {
            // Guards the [Serializable] attribute on the struct itself: without it
            // JsonUtility emits "{}" and the nested list silently serialises as empty.
            var msg = new EphemeralMessage
            {
                role = "assistant",
                content = "Bienvenido",
                timestampIso8601 = "2026-08-18T09:00:00.0000000Z"
            };

            string json = JsonUtility.ToJson(msg);
            var loaded = JsonUtility.FromJson<EphemeralMessage>(json);

            Assert.IsTrue(json.Contains("role"),
                "EphemeralMessage must stay [Serializable] with public fields — otherwise ToJson yields '{}' and history is silently lost.");
            Assert.AreEqual("assistant", loaded.role, "role must round-trip.");
            Assert.AreEqual("Bienvenido", loaded.content, "content must round-trip.");
            Assert.AreEqual("2026-08-18T09:00:00.0000000Z", loaded.timestampIso8601, "timestampIso8601 must round-trip.");
        }

        [Test]
        public void JsonRoundTrip_Repeated_IsStableAndIndependent()
        {
            _memory.npcKey = "stable-npc";
            AppendNumbered(_memory, 3);

            string first = JsonUtility.ToJson(_memory);
            var once = JsonUtility.FromJson<NPCMemory>(first);
            string second = JsonUtility.ToJson(once);
            var twice = JsonUtility.FromJson<NPCMemory>(second);

            Assert.AreEqual(first, second,
                "Serialising a deserialised record must be idempotent — drift here means every Save() rewrites the file differently and defeats change detection.");
            Assert.AreNotSame(once.ephemeralHistory, twice.ephemeralHistory,
                "Each deserialisation must produce its own list instance, not a shared one.");

            NPCMemoryStore.AppendEphemeral(twice, "user", "extra");
            Assert.AreEqual(3, once.ephemeralHistory.Count,
                "Appending to one deserialised copy must not affect a previously deserialised copy of the same JSON.");
        }
    }
}
