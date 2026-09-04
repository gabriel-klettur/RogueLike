using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Pins the badge that tells the player the interact key does something over a person, the
    /// way it already did over a tree or a seam.
    ///
    /// <para>The conversation itself worked before this component existed —
    /// <c>PlayerInteractionController</c> opened one as a fallback — but as a fallback OUTSIDE
    /// the registry, so no <see cref="InteractionPromptInfo"/> was ever produced for an NPC and
    /// nothing on screen said the key would work. That is the defect these tests exist to keep
    /// fixed, and it is invisible to any test that only asks "does E open a chat".</para>
    /// </summary>
    [TestFixture]
    public class NPCConversationPromptTests
    {
        private readonly List<Object> _created = new List<Object>();

        /// <summary>
        /// A live <see cref="ChatSystem"/>, because the component refuses to offer a badge
        /// without one — correctly: with no chat system the key would do nothing, and the
        /// interface's own rule is that a control the player is told about and that then
        /// refuses them is worse than no control. Built here rather than relaxed in production
        /// for the reason the chat fixtures already record.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("[ChatSystem_Test]");
            _created.Add(go);
            var chat = go.AddComponent<ChatSystem>();
            chat.GetType()
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(chat, null);

            // Awake resolves the SHIPPED catalogue from Resources/Chat when none is assigned.
            // Right in the game, wrong here: an NPC this fixture names "Gatita" would pick up
            // the real persona and its authored range in place of the fixture's.
            chat.GetType()
                .GetField("_catalog", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(chat, null);
        }

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

        /// <summary>
        /// A talkable character built the way <c>EntitySetup.ConfigureChat</c> builds one:
        /// <see cref="NPCInteractable"/> configured with a display name and a range, optionally
        /// a vendor. Awake never runs on a component added in Edit Mode, so the cached
        /// references are filled explicitly — the same reason the chat fixtures do it.
        /// </summary>
        private NPCConversationInteractable CreateNpc(string name, float range, bool vendor = false)
        {
            var go = new GameObject(name);
            _created.Add(go);

            var interactable = go.AddComponent<NPCInteractable>();
            interactable.Configure(name, range);

            if (vendor) go.AddComponent<VendorNPC>();

            var conversation = go.AddComponent<NPCConversationInteractable>();
            InvokeAwake(conversation);
            return conversation;
        }

        private static void InvokeAwake(MonoBehaviour behaviour)
        {
            behaviour.GetType()
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(behaviour, null);
        }

        private GameObject CreatePlayer()
        {
            var go = new GameObject("Player");
            _created.Add(go);
            return go;
        }

        // ---- The badge ------------------------------------------------------

        [Test]
        public void Prompt_OffersTheVerbAndNamesWhoIsBeingOffered()
        {
            var npc = CreateNpc("Gatita", 2f);

            var info = npc.DescribePrompt(CreatePlayer());

            Assert.AreEqual(InteractionAvailability.Ready, info.Availability,
                "Ready is the only availability that draws a key cap. Without it the player " +
                "sees a dimmed label and is told the key does NOT work here.");
            Assert.AreEqual("Conversar", info.Verb);
            Assert.AreEqual("Gatita", info.Detail,
                "A market has several people standing together and the registry picks by " +
                "nearest surface, which is not always the one the player thinks they face. " +
                "The name is the only thing that says who the key will reach.");
        }

        [Test]
        public void Prompt_SaysSoWhenTheCharacterTrades()
        {
            var vendor = CreateNpc("Pavel", 2f, vendor: true);
            var banker = CreateNpc("Abigail", 2f);

            StringAssert.Contains("comercia", vendor.DescribePrompt(CreatePlayer()).Detail,
                "That this person trades is the most useful fact about them and is otherwise " +
                "only discoverable by holding a conversation first.");
            Assert.AreEqual("Abigail", banker.DescribePrompt(CreatePlayer()).Detail,
                "Abigail has no vendorConfig on purpose — she is a banker whose goods do not " +
                "exist in ItemCatalog — so promising trade over her head would be a lie.");
        }

        [Test]
        public void Prompt_IsVisibleSoTheRegistryWillOfferIt()
        {
            var npc = CreateNpc("Roberto", 2f);

            Assert.IsTrue(npc.DescribePrompt(CreatePlayer()).IsVisible,
                "InteractableRegistry.Consider drops any candidate whose prompt is not " +
                "visible, so an invisible prompt is the same as not being registered at all.");
            Assert.IsTrue(npc.DescribePrompt(CreatePlayer()).IsActionable);
        }

        // ---- Range ----------------------------------------------------------

        [Test]
        public void Radius_IsTheRangeAuthoredOnTheCharacter()
        {
            Assert.AreEqual(2f, CreateNpc("Smith", 2f).InteractionRadius, 0.0001f);
            Assert.AreEqual(4.5f, CreateNpc("Talkative", 4.5f).InteractionRadius, 0.0001f,
                "Read from NPCInteractable rather than resolved a second time here, so the " +
                "badge and every other reader of that range can never disagree.");
        }

        [Test]
        public void Bounds_AreAFootprint_NotTheDrawnCharacter()
        {
            var npc = CreateNpc("Gatita", 2f);
            npc.transform.position = new Vector3(10f, 5f, 0f);

            Bounds bounds = npc.InteractionBounds;

            Assert.AreEqual(10f, bounds.center.x, 0.0001f);
            Assert.AreEqual(5f, bounds.center.y, 0.0001f);
            Assert.Less(bounds.size.y, 1f,
                "A villager is drawn upward from their feet — Gatita is 2.4 units tall — so " +
                "measuring against the sprite would raise the badge for a player standing on " +
                "a roof two units above her head. HarvestNode records the same rule for a " +
                "tree canopy.");
        }

        // ---- Acting ---------------------------------------------------------

        [Test]
        public void ItIsRegisteredAsDynamic_BecauseCharactersWalk()
        {
            int before = InteractableRegistry.DynamicCount;
            var npc = CreateNpc("Gatita", 2f);

            // OnEnable does not fire on a component added in Edit Mode, so drive the same call.
            InteractableRegistry.RegisterDynamic(npc);

            Assert.AreEqual(before + 1, InteractableRegistry.DynamicCount,
                "A plain Register indexes by the position held when the spatial hash was last " +
                "rebuilt, and it rebuilds only on membership change — so a character who walks " +
                "would go on being looked up where they used to stand. That only fails above " +
                "the hash threshold of 24, i.e. it passes in an empty test scene and breaks in " +
                "the shipped world, measured at 94 registered entries.");

            InteractableRegistry.Unregister(npc);
            Assert.AreEqual(before, InteractableRegistry.DynamicCount);
        }

        [Test]
        public void ItIsNeverALeashedSession()
        {
            var npc = CreateNpc("Gatita", 2f);

            Assert.IsFalse(npc.IsInteracting,
                "The controller cancels a session when the player drifts 0.35 units. A " +
                "conversation is owned by ChatSystem and closed by Escape or Enter, so " +
                "reporting a session would make walking half a step end the conversation.");

            // The controller raises this the frame after opening, because chat engages
            // InputBlocker and the controller then suppresses. It must not close anything.
            Assert.DoesNotThrow(() => npc.CancelInteraction());
        }

        [Test]
        public void RequiresTheComponentThatCarriesTheNameAndRange()
        {
            var attribute = (RequireComponent)System.Attribute.GetCustomAttribute(
                typeof(NPCConversationInteractable), typeof(RequireComponent));

            Assert.IsNotNull(attribute);
            Assert.AreEqual(typeof(NPCInteractable), attribute.m_Type0,
                "Both the badge's name and its radius come from NPCInteractable. Without it " +
                "the prompt would silently fall back to a nameless 2-unit default.");
        }

        // ---- The shipped wiring ---------------------------------------------

        [Test]
        public void EveryChatCapableShippedEntity_WouldGetTheBadge()
        {
            int checkedCount = 0;

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:MonsterDefinition"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def == null) continue;

                NPCPersonaDefinition persona = def.chatPersona != null
                    ? def.chatPersona
                    : (def.vendorConfig != null ? def.vendorConfig.persona : null);
                if (persona == null && def.vendorConfig == null) continue;

                checkedCount++;

                // The radius the badge will use, resolved exactly as EntitySetup.ConfigureChat
                // resolves it. A zero would register the character with a range nothing can
                // reach, so the prompt would never appear however close the player stood.
                float range = persona != null && persona.chatRange > 0f
                    ? persona.chatRange
                    : (def.stats.chatRange > 0f ? def.stats.chatRange : 2f);

                Assert.Greater(range, 0f, $"{path} resolves a non-positive chat range.");
            }

            Assert.Greater(checkedCount, 0,
                "No chat-capable entity found at all — the join that made the whole chat " +
                "subsystem reachable has come undone.");
        }
    }
}
