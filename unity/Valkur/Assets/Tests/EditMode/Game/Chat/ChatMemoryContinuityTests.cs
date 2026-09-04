using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Coverage for the half of the memory layer that did not exist: reading it back.
    ///
    /// Every message was already written to <see cref="NPCMemory.ephemeralHistory"/>,
    /// persisted atomically, recovered from a backup on corruption and migrated across
    /// schema versions — and then <c>OpenChat</c> called <c>_history.Clear()</c> two lines
    /// after loading it, so none of it reached a screen or a provider. The data was
    /// correct, durable and invisible; the only thing the whole layer changed in game was
    /// suppressing the greeting after the first visit.
    /// </summary>
    [TestFixture]
    public class ChatMemoryContinuityTests
    {
        private string _testRoot;
        private readonly List<Object> _scene = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            // ChatBubble builds TMP objects and calls Object.Destroy in teardown; neither is
            // legal in edit mode and both log.
            LogAssert.ignoreFailingMessages = true;

            _testRoot = Path.Combine(Path.GetTempPath(), "ValkurChatCont_" + System.Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;

            ServiceLocator.Clear();
            EntityRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _scene) if (o != null) Object.DestroyImmediate(o);
            _scene.Clear();

            ChatSessionLogger.CloseSession();
            ChatPersistencePaths.OverrideRoot = null;
            if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);

            ServiceLocator.Clear();
            EntityRegistry.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixture helpers ─────────────────────────────────────────────────

        private T Track<T>(T o) where T : Object { _scene.Add(o); return o; }

        private NPCPersonaDefinition MakePersona(string id, string displayName)
        {
            var p = Track(ScriptableObject.CreateInstance<NPCPersonaDefinition>());
            p.personaId = id;
            p.displayName = displayName;
            p.chatRange = 5f;
            p.dialogueLines = new List<string> { "uno", "dos", "tres" };
            return p;
        }

        private GameObject MakeNpc(NPCPersonaDefinition persona)
        {
            var go = Track(new GameObject("npc"));
            go.AddComponent<NPCInteractable>().Configure(persona.displayName, persona.chatRange);
            go.AddComponent<NPCChatIdentity>().SetPersona(persona);
            return go;
        }

        private ChatSystem MakeChatSystem()
        {
            var go = Track(new GameObject("[ChatSystem_Test]"));
            return go.AddComponent<ChatSystem>();
        }

        // ── History is seeded from what was persisted ───────────────────────

        [Test]
        public void OpenChat_ReplaysThePersistedConversationIntoHistory()
        {
            var persona = MakePersona("p-recall", "Recalla");
            var npc = MakeNpc(persona);

            var chat = MakeChatSystem();
            chat.OpenChat(npc);
            string npcKey = chat.ActiveMemory.npcKey;

            NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "user", "hola de nuevo");
            NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "assistant", "te recuerdo, viajero");
            NPCMemoryStore.Save(chat.ActiveMemory);
            chat.CloseChat();

            // A second system, as if the game had been restarted.
            var reopened = MakeChatSystem();
            reopened.OpenChat(npc);

            Assert.AreEqual(npcKey, reopened.ActiveMemory.npcKey, "Pre-condition: same memory record.");
            CollectionAssert.Contains(reopened.History.Select(m => m.text).ToList(), "te recuerdo, viajero",
                "The persisted conversation must be replayed into History. Before this, twelve " +
                "messages were loaded from disk and cleared two lines later.");
        }

        [Test]
        public void OpenChat_AttributesRecalledLinesToTheRightSpeaker()
        {
            var persona = MakePersona("p-attrib", "Atribuida");
            var npc = MakeNpc(persona);

            var chat = MakeChatSystem();
            chat.OpenChat(npc);
            NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "user", "pregunta del jugador");
            NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "assistant", "respuesta del npc");
            NPCMemoryStore.Save(chat.ActiveMemory);
            chat.CloseChat();

            var reopened = MakeChatSystem();
            reopened.OpenChat(npc);

            var byText = reopened.History.ToDictionary(m => m.text, m => m.sender);
            Assert.AreEqual(ChatSystem.PLAYER_SENDER, byText["pregunta del jugador"],
                "A 'user' role is the player. The panel colours rows on exactly this comparison.");
            Assert.AreEqual("Atribuida", byText["respuesta del npc"],
                "An 'assistant' role is the NPC, named by its persona rather than by the GameObject.");
        }

        [Test]
        public void OpenChat_ReplayingDoesNotDuplicateTheRecordOnDisk()
        {
            var persona = MakePersona("p-nodup", "Nodup");
            var npc = MakeNpc(persona);

            var chat = MakeChatSystem();
            chat.OpenChat(npc);
            NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "assistant", "una linea");
            NPCMemoryStore.Save(chat.ActiveMemory);
            chat.CloseChat();

            for (int i = 0; i < 3; i++)
            {
                var again = MakeChatSystem();
                again.OpenChat(npc);
                again.CloseChat();
            }

            var final = MakeChatSystem();
            final.OpenChat(npc);

            int occurrences = final.ActiveMemory.ephemeralHistory.Count(m => m.content == "una linea");
            Assert.AreEqual(1, occurrences,
                "Replay must not go through AddMessage. If it did, every open would re-append " +
                "the whole remembered conversation and the record would grow without bound.");
        }

        [Test]
        public void OpenChat_HistoryStaysWithinItsCap_EvenFromAFullMemory()
        {
            var persona = MakePersona("p-cap", "Capada");
            var npc = MakeNpc(persona);

            var chat = MakeChatSystem();
            chat.OpenChat(npc);
            for (int i = 0; i < NPCMemory.EPHEMERAL_CAP + 5; i++)
                NPCMemoryStore.AppendEphemeral(chat.ActiveMemory, "assistant", "linea-" + i);
            NPCMemoryStore.Save(chat.ActiveMemory);
            chat.CloseChat();

            var reopened = MakeChatSystem();
            reopened.OpenChat(npc);

            Assert.LessOrEqual(reopened.History.Count, 10,
                "The memory cap (12) is deliberately larger than the panel's (10), so a full " +
                "recall overflows and the oldest exchanges fall off exactly as they do live.");
        }

        [Test]
        public void OpenChat_WithNothingRemembered_StartsEmpty()
        {
            var persona = MakePersona("p-fresh", "Fresca");
            var npc = MakeNpc(persona);

            var chat = MakeChatSystem();
            chat.OpenChat(npc);

            Assert.IsTrue(string.IsNullOrEmpty(persona.greeting),
                "Pre-condition: this persona authors no greeting, so nothing but a recall " +
                "could put a row in the history.");
            CollectionAssert.IsEmpty(chat.History,
                "A first meeting has nothing to recall, so the panel must open blank rather " +
                "than showing a phantom exchange.");
        }

        // ── The provider stops repeating itself across a restart ───────────

        [Test]
        public void OfflineProvider_DoesNotOpenOnTheLineTheConversationEndedOn()
        {
            var persona = MakePersona("p-repeat", "Repetida");
            persona.profile = Track(ScriptableObject.CreateInstance<PersonaProfileDefinition>());
            persona.profile.personaId = persona.personaId;
            persona.profile.smallTalk.examples = new List<string> { "primera", "segunda" };

            var memory = new NPCMemory { npcKey = "k", personaId = persona.personaId };
            NPCMemoryStore.AppendEphemeral(memory, "assistant", "primera");

            // A FRESH provider — the per-session cursor and last-line cache are gone, which
            // is exactly the state after a restart.
            var provider = new OfflineDialogueProvider();
            var task = provider.GenerateReplyAsync(
                new ChatRequest(persona, memory, "cuentame algo"), CancellationToken.None);
            task.Wait();

            Assert.AreNotEqual("primera", task.Result.Text,
                "The remembered last line is what the player walked away on. Opening the next " +
                "conversation with it is the most visible way an NPC can look like a machine.");
        }
    }
}
