using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The three things that make a conversation feel like one: the character stops walking
    /// while you talk to her, she looks like she is listening while you type, and the panel
    /// stays in the language you chose.
    ///
    /// <para>Each was inert in its own way before. The stroller walked out of range
    /// mid-sentence; the portrait only ever showed the TALKING face, so nine drawings had
    /// nowhere to appear; and the EN/ES button persisted to a per-NPC field that exactly one
    /// class read — the ONLINE provider — so with the default offline provider it saved
    /// correctly and changed nothing on screen.</para>
    /// </summary>
    public class ChatPresenceTests
    {
        private const string GATITA = "Assets/_Project/Data/ChatPersonas/vendor_cheff_gatita.asset";
        private const string PREF_KEY = "valkur.chat.language";

        private string _savedLanguage;

        [SetUp]
        public void SetUp()
        {
            // PlayerPrefs is MACHINE state, not fixture state: it survives the run, the
            // Editor and the reboot, so a test that leaves it switched would fail a default
            // assertion forever, on this machine only, for a reason nothing here names.
            _savedLanguage = PlayerPrefs.GetString(PREF_KEY, ChatLanguage.SPANISH);
            ChatLanguage.Set(ChatLanguage.SPANISH);
        }

        [TearDown]
        public void TearDown()
        {
            ChatLanguage.Set(_savedLanguage);
        }

        // ── Listening ───────────────────────────────────────────────────────

        [Test]
        public void EveryListeningDrawingIsReachable()
        {
            NPCPersonaDefinition gatita = LoadGatita();
            Assert.IsNotEmpty(gatita.listeningFaces, "Gatita has no listening art wired.");

            var reached = new HashSet<Sprite>();
            foreach (FacialExpression e in FacialExpressionFallback.All)
                reached.Add(gatita.ResolveListeningFace(e));

            var never = new List<string>();
            foreach (NPCPersonaDefinition.FacialSprite entry in gatita.listeningFaces)
            {
                if (entry.sprite != null && !reached.Contains(entry.sprite))
                    never.Add(entry.sprite.name);
            }

            CollectionAssert.IsEmpty(never,
                "A listening drawing no expression resolves to is imported and dead, exactly " +
                "like an unreachable talking face. Never shown: " + string.Join(", ", never));
        }

        [Test]
        public void ListeningAndTalkingAreDifferentDrawings()
        {
            NPCPersonaDefinition gatita = LoadGatita();

            var same = new List<string>();
            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                // Tired has no listening pose of its own — the sheet holds nine and the
                // vocabulary ten — so it resolves through the chain and is allowed to differ
                // from its own talking face without having one drawn for it.
                if (gatita.ResolveFace(e) == gatita.ResolveListeningFace(e)) same.Add(e.ToString());
            }

            CollectionAssert.IsEmpty(same,
                "These expressions show the same drawing whether she is talking or " +
                "listening, so the player cannot tell the two states apart: " +
                string.Join(", ", same));
        }

        [Test]
        public void APersonaWithNoListeningArt_IsUnchanged()
        {
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:NPCPersonaDefinition", new[] { "Assets/_Project/Data/ChatPersonas" }))
            {
                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (persona == null || persona.HasListeningFaces) continue;

                foreach (FacialExpression e in FacialExpressionFallback.All)
                {
                    Assert.AreSame(persona.ResolveFace(e), persona.ResolveListeningFace(e),
                        persona.displayName + " drew no listening art, so its portrait must " +
                        "simply not change while the player types. Falling back to a blank " +
                        "attentive stare would throw away the emotion, which is the part the " +
                        "player is actually reading.");
                }
            }
        }

        [Test]
        public void TheListeningFallbackKeepsTheEMOTION_NotTheAttentiveness()
        {
            NPCPersonaDefinition gatita = LoadGatita();

            // Tired is the one expression with no listening drawing. Its chain is
            // Tired -> Thinking -> Neutral, so it must land on listening-THINKING and not on
            // listening-neutral, which would read as her having stopped feeling anything.
            Sprite tired = gatita.ResolveListeningFace(FacialExpression.Tired);
            Sprite thinking = gatita.ResolveListeningFace(FacialExpression.Thinking);
            Sprite neutral = gatita.ResolveListeningFace(FacialExpression.Neutral);

            Assert.AreSame(thinking, tired,
                "A missing listening pose must walk the SAME fallback chain the talking " +
                "faces walk, so it degrades in intensity rather than in emotion.");
            Assert.AreNotSame(neutral, tired);
        }

        // ── Listening vs thinking ───────────────────────────────────────────

        [Test]
        public void AGreetingNeverShowsTheDeliberatingFace()
        {
            using (var chat = new Probe())
            {
                chat.Chat.SetExpression(FacialExpression.Happy);

                chat.Chat.SetPlayerTyping(true);
                chat.BeginAwaitingReply();
                chat.Chat.SetPlayerTyping(false);   // ChatUI submits, THEN clears the field

                Assert.IsTrue(chat.Chat.Listening,
                    "The wait belongs to the player's turn: she has not said a word yet, so " +
                    "the portrait must stay on the listening axis. Driving this off the text " +
                    "box alone left a hole exactly at the handover.");
                Assert.AreNotEqual(FacialExpression.Thinking, chat.Chat.CurrentExpression,
                    "This is the reported bug. The wait used to set Thinking outright, so " +
                    "every message — 'hola, que tal?' included — showed the deliberating " +
                    "face for the 500 ms before the first bubble, against a 140 ms fade.");
            }
        }

        [Test]
        public void AShortWaitNeverEscalates()
        {
            using (var chat = new Probe())
            {
                chat.BeginAwaitingReply();
                chat.Tick();

                Assert.AreNotEqual(FacialExpression.Thinking, chat.Chat.CurrentExpression,
                    "The offline provider answers synchronously and its first bubble is held " +
                    "0.5 s, so its whole wait is a third of the threshold. If this ever " +
                    "escalates, the default provider has the flash back.");
            }
        }

        [Test]
        public void ALongWaitBecomesThinking()
        {
            using (var chat = new Probe())
            {
                chat.BeginAwaitingReply();
                chat.AgeWait(2.0f);
                chat.Tick();

                Assert.IsTrue(chat.Chat.Listening,
                    "Escalating must not take her off the listening axis — what should " +
                    "appear is the LISTENING thinking pose, she is still attending.");
                Assert.AreEqual(FacialExpression.Thinking, chat.Chat.CurrentExpression);
            }
        }

        [Test]
        public void AStaleDeliberationRelaxesWhenThePlayerTypesAgain()
        {
            using (var chat = new Probe())
            {
                chat.Chat.SetExpression(FacialExpression.Thinking);   // she just quoted a price
                chat.Chat.SetPlayerTyping(true);

                Assert.AreNotEqual(FacialExpression.Thinking, chat.Chat.CurrentExpression,
                    "Otherwise the eyes-aside pose appears with NO wait behind it, so that " +
                    "one drawing would mean two different things depending on how it was " +
                    "reached — and the escalation above would stop meaning anything.");
            }
        }

        [TestCase(FacialExpression.Happy)]
        [TestCase(FacialExpression.Angry)]
        [TestCase(FacialExpression.Playful)]
        public void EveryOtherMoodSurvivesTheNextKeystroke(FacialExpression mood)
        {
            using (var chat = new Probe())
            {
                chat.Chat.SetExpression(mood);
                chat.Chat.SetPlayerTyping(true);

                Assert.AreEqual(mood, chat.Chat.CurrentExpression,
                    "Only Thinking is cleared. Staying amused or cross while you type is " +
                    "exactly the continuity the listening axis exists for — resetting every " +
                    "mood would make her forget the conversation on every keystroke.");
            }
        }

        [Test]
        public void EveryFailurePathEndsTheWait()
        {
            using (var chat = new Probe())
            {
                chat.BeginAwaitingReply();
                chat.EndAwaitingReply();

                Assert.IsFalse(chat.Chat.Listening,
                    "A cancelled or thrown provider must end the wait too, or she is left " +
                    "listening to a player who stopped typing minutes ago.");
            }
        }

        /// <summary>
        /// A bare ChatSystem with the wait plumbing reachable. The methods are internal to
        /// the Gameplay assembly, so the test assembly reaches them by reflection rather
        /// than by widening their visibility for a fixture.
        /// </summary>
        private sealed class Probe : System.IDisposable
        {
            private const System.Reflection.BindingFlags FLAGS =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            private readonly GameObject _go;
            public ChatSystem Chat { get; }

            public Probe()
            {
                _go = new GameObject("chat-probe");
                Chat = _go.AddComponent<ChatSystem>();
            }

            public void BeginAwaitingReply() => Call("BeginAwaitingReply");
            public void EndAwaitingReply() => Call("EndAwaitingReply");
            public void Tick() => Call("TickWaitEscalation");

            /// <summary>Backdates the wait so a threshold can be crossed without sleeping.</summary>
            public void AgeWait(float seconds) =>
                typeof(ChatSystem).GetField("_awaitingSince", FLAGS)
                    .SetValue(Chat, Time.time - seconds);

            private void Call(string method) =>
                typeof(ChatSystem).GetMethod(method, FLAGS).Invoke(Chat, null);

            public void Dispose() => Object.DestroyImmediate(_go);
        }

        // ── Standing still ──────────────────────────────────────────────────

        [Test]
        public void AConversationHoldsTheCharacterStill()
        {
            var go = new GameObject("stroller");
            try
            {
                // In Edit Mode Awake never runs for a component added like this, so the
                // brain's animator, health and FSM all stay null. That is the point: the
                // hold has to be null-safe on every one of them, because it also runs on a
                // character whose brain has not finished waking during a scene load.
                var brain = go.AddComponent<FSMMonsterBrain>();

                Assert.IsFalse(brain.ConversationPaused);

                brain.SetConversationPaused(true);
                Assert.IsTrue(brain.ConversationPaused);

                brain.SetConversationPaused(false);
                Assert.IsFalse(brain.ConversationPaused,
                    "Closing the panel must hand the character back its own movement, or a " +
                    "stroller freezes for the rest of the session.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HoldingAndReleasingAreIdempotent()
        {
            var go = new GameObject("stroller");
            try
            {
                var brain = go.AddComponent<FSMMonsterBrain>();

                brain.SetConversationPaused(true);
                brain.SetConversationPaused(true);
                brain.SetConversationPaused(false);
                brain.SetConversationPaused(false);

                Assert.IsFalse(brain.ConversationPaused,
                    "ChatSystem releases defensively before taking a new hold and again on " +
                    "close, so a double release is the normal path and must not re-arm it.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ChatSystemTakesAndReleasesTheHoldOnItsOwnSeams()
        {
            // A source check, the way CastOriginContractTests pins ResolveCastStart. The
            // behaviour needs a live scene with a spawned stroller, but the WIRING is the
            // half that silently rots — the hold is worthless if OpenChat stops taking it or
            // CloseChat stops giving it back, and neither would fail anything else.
            string chatSystem = ReadScript("ChatSystem.cs");
            string messages = ReadScript("ChatSystem.Messages.cs");

            StringAssert.Contains("HoldStillForConversation(target)", chatSystem,
                "OpenChat no longer holds the character still.");
            StringAssert.Contains("ReleaseConversationHold()", messages,
                "CloseChat no longer releases the hold — the single close seam every exit " +
                "runs through.");
        }

        private static string ReadScript(string fileName)
        {
            string root = System.IO.Path.Combine(
                Application.dataPath, "_Project/Scripts/Gameplay/Chat");
            string path = System.IO.Path.Combine(root, fileName);

            Assert.IsTrue(System.IO.File.Exists(path), "Missing " + path);
            return System.IO.File.ReadAllText(path);
        }

        // ── Language ────────────────────────────────────────────────────────

        [Test]
        public void LanguageIsGlobalAndPersists()
        {
            Assert.AreEqual(ChatLanguage.SPANISH, ChatLanguage.Current);

            ChatLanguage.Toggle();

            Assert.AreEqual(ChatLanguage.ENGLISH, ChatLanguage.Current);
            Assert.AreEqual(ChatLanguage.ENGLISH, PlayerPrefs.GetString(PREF_KEY, ""),
                "The preference belongs to the player, not to one NPC. Storing it only on " +
                "the open conversation's memory is what made switching with Gatita leave " +
                "every other character in Spanish.");
        }

        [Test]
        public void EveryPanelCaptionMoves()
        {
            var spanish = Captions();
            ChatLanguage.Toggle();
            var english = Captions();

            for (int i = 0; i < spanish.Count; i++)
            {
                // "No" is the same word in both, and pretending otherwise would be a
                // translation nobody asked for.
                if (spanish[i] == "No") continue;

                Assert.AreNotEqual(spanish[i], english[i],
                    "A caption that does not change leaves the panel half-translated, which " +
                    "reads worse than not offering the toggle at all: " + spanish[i]);
            }
        }

        [Test]
        public void AnUnknownLanguageFallsBackRatherThanStranding()
        {
            ChatLanguage.Set("fr");

            Assert.AreEqual(ChatLanguage.SPANISH, ChatLanguage.Current,
                "A preference file holding a code with no strings behind it would put the " +
                "panel in a language it cannot render, with the toggle as the only way out.");
        }

        [Test]
        public void TogglingIsReversible()
        {
            ChatLanguage.Toggle();
            ChatLanguage.Toggle();

            Assert.AreEqual(ChatLanguage.SPANISH, ChatLanguage.Current);
        }

        private static List<string> Captions() => new List<string>
        {
            ChatLanguage.Label,
            ChatLanguage.InputPlaceholder,
            ChatLanguage.Send,
            ChatLanguage.Trade,
            ChatLanguage.Accept,
            ChatLanguage.Decline,
            ChatLanguage.Close,
        };

        private static NPCPersonaDefinition LoadGatita()
        {
            var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(GATITA);
            Assert.IsNotNull(persona, "Gatita's persona asset is missing at " + GATITA);
            return persona;
        }
    }
}
