using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Asserts on the data this game actually SHIPS, and on the composition that
    /// carries it into a live entity.
    ///
    /// The rest of the chat suite — 225 tests over ten fixtures — builds its own
    /// catalogue, its own <see cref="NPCInteractable"/> and its own player, and every one
    /// of them passed for the entire period in which the feature was unreachable: the
    /// shipped catalogue held zero rows, not one persona asset existed, the field that
    /// links them was never assigned, and nothing added the components a conversation
    /// needs. That is the failure shape <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> records —
    /// a test that exercises one half proves nothing, so assert on the composition and
    /// on the shipped bytes.
    ///
    /// If this fixture goes red, the chat is mute in game, whatever the other fixtures say.
    /// </summary>
    [TestFixture]
    public class ShippedChatDataTests
    {
        private const string CATALOG_PATH = "Assets/_Project/Resources/Chat/ChatAssignmentCatalog.asset";

        /// <summary>Where <c>ChatSystem.EnsureCatalog</c> looks. Mirrored, not imported, on purpose.</summary>
        private const string CATALOG_RESOURCE_PATH = "Chat/ChatAssignmentCatalog";

        private const string MONSTER_CATALOG_DIR = "Assets/_Project/Data/Catalogs/Monsters";

        /// <summary>
        /// Personas carry so few lines that a repertoire below this reads as the same
        /// sentence every time. The shipped minimum is Felipondor's five.
        /// </summary>
        private const int MIN_DIALOGUE_LINES = 3;

        private static ChatAssignmentCatalog LoadCatalog() =>
            AssetDatabase.LoadAssetAtPath<ChatAssignmentCatalog>(CATALOG_PATH);

        private static IEnumerable<MonsterDefinition> ShippedMonsters() =>
            AssetDatabase.FindAssets("t:MonsterDefinition", new[] { MONSTER_CATALOG_DIR })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null);

        // ── The catalogue exists, is reachable, and is not empty ────────────

        [Test]
        public void Catalog_IsLoadableThroughTheResourcePathChatSystemUses()
        {
            var viaResources = Resources.Load<ChatAssignmentCatalog>(CATALOG_RESOURCE_PATH);

            Assert.IsNotNull(viaResources,
                $"ChatSystem.EnsureCatalog loads 'Resources/{CATALOG_RESOURCE_PATH}'. With nothing " +
                "there, every persona lookup returns null, no NPC greets, and GenerateReply " +
                "returns on its first line — which is the state this project shipped in.");
        }

        [Test]
        public void Catalog_HasOneRowPerShippedPersona()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog, $"No ChatAssignmentCatalog at '{CATALOG_PATH}'.");

            Assert.IsNotEmpty(catalog.assignments,
                "The shipped catalogue held 'assignments: []' for the life of the project. " +
                "An empty catalogue is indistinguishable from a missing one at runtime.");
        }

        [Test]
        public void Catalog_EveryRowResolvesToARealPersona()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog);

            foreach (var row in catalog.assignments)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(row.entityName),
                    "A row with no entity name can never be matched by GetPersona.");
                Assert.IsNotNull(row.persona,
                    $"Row '{row.entityName}' points at no persona — the lookup silently drops it.");
            }
        }

        [Test]
        public void Catalog_LookupAnswersForEveryRowItDeclares()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog);
            catalog.RebuildLookup();

            foreach (var row in catalog.assignments)
            {
                Assert.AreSame(row.persona, catalog.GetPersona(row.entityName),
                    $"GetPersona('{row.entityName}') must return the persona the row declares.");
            }
        }

        // ── Every shipped persona can hold a conversation ───────────────────

        [Test]
        public void Personas_AreSpeakable()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog);

            foreach (var persona in catalog.assignments.Select(a => a.persona).Distinct())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(persona.personaId),
                    $"'{persona.name}' has no personaId — it is the key memory files are named after.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(persona.displayName),
                    $"'{persona.name}' has no displayName — the panel title and every reply are attributed to it.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(persona.greeting),
                    $"'{persona.name}' has no greeting, so the first thing it does on meeting the player is nothing.");
                Assert.GreaterOrEqual(persona.dialogueLines.Count, MIN_DIALOGUE_LINES,
                    $"'{persona.name}' has {persona.dialogueLines.Count} dialogue lines. " +
                    "OfflineDialogueProvider cycles them, so a short list repeats visibly.");
                Assert.Greater(persona.chatRange, 0f,
                    $"'{persona.name}' has chatRange 0 — ChatSystem.ConsiderCandidates would need " +
                    "the player standing exactly on its pivot.");
            }
        }

        [Test]
        public void Personas_HaveNoBlankDialogueLines()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog);

            foreach (var persona in catalog.assignments.Select(a => a.persona).Distinct())
            {
                CollectionAssert.DoesNotContain(
                    persona.dialogueLines.Select(l => (l ?? "").Trim()), "",
                    $"'{persona.name}' ships a blank dialogue line. The cycle would land on it and " +
                    "the NPC would answer with nothing.");
            }
        }

        [Test]
        public void Personas_CarryAProfileThatAgreesOnIdentity()
        {
            var catalog = LoadCatalog();
            Assert.IsNotNull(catalog);

            foreach (var persona in catalog.assignments.Select(a => a.persona).Distinct())
            {
                Assert.IsNotNull(persona.profile,
                    $"'{persona.name}' has no PersonaProfileDefinition. The prompt builder has " +
                    "nothing to characterise it with, so an online provider would answer as a " +
                    "generic assistant wearing its name.");
                Assert.AreEqual(persona.personaId, persona.profile.personaId,
                    $"'{persona.name}' and its profile disagree on personaId. Two assets keyed " +
                    "differently is the drift the split exists to avoid.");
            }
        }

        // ── The definitions that spawn those characters are wired to them ───

        [Test]
        public void EveryNeutralMonsterDefinitionCarriesAChatPersona()
        {
            var mute = ShippedMonsters()
                .Where(m => string.Equals(m.stats.faction, "NEUTRAL", System.StringComparison.OrdinalIgnoreCase))
                .Where(m => m.chatPersona == null)
                .Select(m => m.name)
                .ToList();

            CollectionAssert.IsEmpty(mute,
                "A NEUTRAL entity is one the player is meant to approach rather than fight. " +
                "Without a chatPersona, EntitySetup.ConfigureChat adds nothing and the entity is " +
                "as unreachable as a hostile: " + string.Join(", ", mute));
        }

        [Test]
        public void NoHostileDefinitionIsAccidentallyChatCapable()
        {
            var talkative = ShippedMonsters()
                .Where(m => string.Equals(m.stats.faction, "EVIL", System.StringComparison.OrdinalIgnoreCase))
                .Where(m => m.chatPersona != null)
                .Select(m => m.name)
                .ToList();

            CollectionAssert.IsEmpty(talkative,
                "A hostile with a persona would be registered as an NPC and offered as a chat " +
                "target while it is trying to kill the player: " + string.Join(", ", talkative));
        }

        // ── Composition: definition → live entity → resolved persona ────────

        [Test]
        public void ConfigureChat_MakesAShippedVendorTalkable()
        {
            var vendor = ShippedMonsters().FirstOrDefault(m => m.chatPersona != null);
            Assert.IsNotNull(vendor, "No shipped MonsterDefinition carries a chatPersona.");

            var go = new GameObject("shipped-vendor");
            try
            {
                EntitySetup.ConfigureChat(go, vendor);

                var interactable = go.GetComponent<NPCInteractable>();
                Assert.IsNotNull(interactable,
                    "ChatSystem skips any entity without an NPCInteractable, so this component is " +
                    "the difference between a character and scenery.");
                Assert.AreEqual(vendor.chatPersona.displayName, interactable.NPCName,
                    "The interactable must carry the persona's name, not the definition's.");
                Assert.Greater(interactable.InteractionRange, 0f);

                var identity = go.GetComponent<NPCChatIdentity>();
                Assert.IsNotNull(identity, "Without NPCChatIdentity the persona is resolved by name, which drifts.");
                Assert.AreSame(vendor.chatPersona, identity.Persona);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ConfigureChat_LeavesAHostileUntouched()
        {
            var hostile = ShippedMonsters().FirstOrDefault(
                m => m.chatPersona == null && m.vendorConfig == null);
            Assert.IsNotNull(hostile);

            var go = new GameObject("shipped-hostile");
            try
            {
                EntitySetup.ConfigureChat(go, hostile);

                Assert.IsNull(go.GetComponent<NPCInteractable>(),
                    "A hostile must pay nothing for a feature it does not have.");
                Assert.IsNull(go.GetComponent<NPCChatIdentity>());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ChatSystem_ResolvesTheShippedPersonaFromAConfiguredEntity()
        {
            var vendor = ShippedMonsters().FirstOrDefault(m => m.chatPersona != null);
            Assert.IsNotNull(vendor);

            // ChatBubble builds TMP objects and calls Destroy in teardown; neither is
            // legal in edit mode and both log. The structural assertion below is what
            // this test is about.
            LogAssert.ignoreFailingMessages = true;

            string testRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ValkurShippedChat_" + System.Guid.NewGuid().ToString("N"));
            // OpenChat writes an NPCMemory. Without this it would land in the real
            // Application.persistentDataPath and leave a memory file behind for a
            // character the player has never met.
            ChatPersistencePaths.OverrideRoot = testRoot;

            var chatGo = new GameObject("ChatSystem");
            var npcGo = new GameObject("npc");
            try
            {
                EntitySetup.ConfigureChat(npcGo, vendor);

                var chat = chatGo.AddComponent<ChatSystem>();
                chat.OpenChat(npcGo);

                Assert.AreSame(vendor.chatPersona, chat.ActivePersona,
                    "This is the whole composition: a shipped definition, configured the way the " +
                    "game configures it, opened the way the game opens it. A null here means every " +
                    "conversation in the built game is silent.");
            }
            finally
            {
                Object.DestroyImmediate(npcGo);
                Object.DestroyImmediate(chatGo);
                EntityRegistry.Clear();
                ChatSessionLogger.CloseSession();
                ChatPersistencePaths.OverrideRoot = null;
                if (System.IO.Directory.Exists(testRoot))
                    System.IO.Directory.Delete(testRoot, recursive: true);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
