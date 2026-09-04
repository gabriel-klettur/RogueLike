using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// That every drawing a character ships can actually appear in a conversation.
    ///
    /// <para>THIS IS THE TEST THE FEATURE EXISTS FOR. Gatita shipped ten expressions and, in
    /// the offline provider that is this project's default, reached exactly four of them:
    /// measured over thirty-two exchanges, Neutral 13, Happy 8, Wink 7, Laugh 4 and the
    /// other six never. The art was imported, wired, visible in the Inspector and dead —
    /// the same "authored and inert" failure CLAUDE.md records for
    /// <c>animation_map.json</c>, the FSM's <c>Actions</c> block and the four casting flags
    /// nothing reads. Nothing failed then and nothing would fail again: an expression that
    /// cannot be reached still RESOLVES, so the portrait simply shows a different drawing
    /// and no error is ever logged.</para>
    ///
    /// <para>Which is why the assertion is over the COMPOSITION rather than over any one
    /// layer. Each of the three — what she says, what the player did, what the world is
    /// doing — is individually easy to test and individually proves nothing: the classifier
    /// can be perfect while the character has no angry line, and the character can have one
    /// while no intent ever selects it. The same reasoning as
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>, where both halves of the round trip were
    /// internally consistent and only their composition was wrong.</para>
    /// </summary>
    public class ExpressionCoverageTests
    {
        private const string GATITA = "Assets/_Project/Data/ChatPersonas/vendor_cheff_gatita.asset";

        // Deliberately phrased as a player would type them, accents and all, rather than as
        // the keyword the classifier is looking for. A test that feeds the table back into
        // the table only proves the table equals itself.
        private static readonly string[] PlayerLines =
        {
            "hola",
            "¿cuánto vale el borsch?",
            "cuéntame del pueblo",
            "cuéntame un chiste",
            "eres una ladrona",
            "qué guapa eres",
            "hay monstruos en el bosque",
            "estoy triste, lo perdí todo",
            "adiós",
            "qué día tan raro",
        };

        private static readonly ChatMoodContext Noon =
            new ChatMoodContext(DayNightCycle.DayPhase.Day, WeatherIntensity.Off, true);

        private static readonly ChatMoodContext Night =
            new ChatMoodContext(DayNightCycle.DayPhase.Night, WeatherIntensity.Off, true);

        private static readonly ChatMoodContext Storm =
            new ChatMoodContext(DayNightCycle.DayPhase.Day, WeatherIntensity.Heavy, true);

        [Test]
        public void Gatita_ReachesEveryExpressionSheHasArtFor()
        {
            NPCPersonaDefinition gatita = LoadGatita();

            var reached = new HashSet<FacialExpression>();
            foreach (ChatMoodContext world in new[] { Noon, Night, Storm })
            {
                foreach (var pair in Converse(gatita, world, rounds: 3))
                    reached.Add(pair.Value);
            }

            var unreachable = new List<string>();
            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                if (gatita.HasOwnFace(e) && !reached.Contains(e)) unreachable.Add(e.ToString());
            }

            CollectionAssert.IsEmpty(unreachable,
                "Gatita has a drawing for these expressions that no conversation can produce, " +
                "so the art is imported and dead. Reaching one needs a CAUSE: a line of hers " +
                "the classifier recognises, a DialogueIntent that maps to it, or a world state " +
                "ChatMoodContext reports. Unreachable: " + string.Join(", ", unreachable));
        }

        [Test]
        public void EveryAuthoredReactionCanBeSaid()
        {
            NPCPersonaDefinition gatita = LoadGatita();
            Assert.IsNotEmpty(gatita.reactions, "Gatita authored no reactions.");

            var said = new HashSet<string>();
            foreach (ChatMoodContext world in new[] { Noon, Night, Storm })
            {
                foreach (var pair in Converse(gatita, world, rounds: 6))
                    said.Add(pair.Key);
            }

            var never = new List<string>();
            foreach (NPCPersonaDefinition.ReactionLine r in gatita.reactions)
            {
                if (!said.Contains(r.line)) never.Add(r.expression + ": " + r.line);
            }

            CollectionAssert.IsEmpty(never,
                "A reaction line nothing can select is the same defect as an unreachable " +
                "drawing, one level down — and the shape it took here was a pool picked with " +
                "'first entry that is not what I just said', which returns entry 0 forever " +
                "whenever anything else speaks in between. Never said: " +
                string.Join(" | ", never));
        }

        [Test]
        public void TheWorldColoursTheFace_ButDoesNotTakeOverTheConversation()
        {
            NPCPersonaDefinition gatita = LoadGatita();

            var provider = new OfflineDialogueProvider();
            var memory = new NPCMemory();
            var spoken = new List<string>();

            // The same neutral line over and over: nothing the player says colours it, so
            // every one of these is an exchange the world is eligible to claim.
            for (int i = 0; i < 9; i++)
                spoken.Add(Ask(provider, gatita, memory, "qué día tan raro", Night).Key);

            int ordinary = 0;
            foreach (string line in spoken)
            {
                if (gatita.dialogueLines.Contains(line)) ordinary++;
            }

            Assert.Greater(ordinary, 0,
                "At night the mood claimed EVERY neutral exchange, so Gatita stopped saying " +
                "anything she was written to say and repeated one tired line instead. The " +
                "hour and the weather do not change between two sentences — they would answer " +
                "identically every time — so they must speak rarely and hand the conversation " +
                "back.");
        }

        [Test]
        public void APlayerInsult_IsAnsweredEveryTime()
        {
            NPCPersonaDefinition gatita = LoadGatita();
            var provider = new OfflineDialogueProvider();
            var memory = new NPCMemory();

            for (int i = 0; i < 4; i++)
            {
                var reply = Ask(provider, gatita, memory, "eres una ladrona", Noon);
                Assert.AreEqual(FacialExpression.Angry, reply.Value,
                    "Something the player DID is not ambient and is never rationed the way a " +
                    "mood is. A character who ignores an insult twice running reads as deaf.");
            }
        }

        [Test]
        public void AnAuthoredReactionKeepsItsOwnFace_AndIsNotReClassified()
        {
            NPCPersonaDefinition gatita = LoadGatita();
            var provider = new OfflineDialogueProvider();
            var memory = new NPCMemory();

            var reply = Ask(provider, gatita, memory, "estoy triste, lo perdí todo", Noon);

            Assert.AreEqual(FacialExpression.Sad, reply.Value);
            CollectionAssert.Contains(ReactionLinesFor(gatita, FacialExpression.Sad), reply.Key,
                "A line written FOR a feeling must be delivered with it. Re-reading the words " +
                "is how the drawing and the sentence end up disagreeing — most of these hold " +
                "no keyword at all and would come back Neutral.");
        }

        [Test]
        public void NoMoodIsSuggestedByAnOrdinaryAfternoon()
        {
            Assert.AreEqual(FacialExpression.Neutral, Noon.SuggestedFace());
            Assert.AreEqual(FacialExpression.Neutral, default(ChatMoodContext).SuggestedFace(),
                "The default context is what every existing caller and test fake passes. It " +
                "has to change nothing.");

            Assert.AreEqual(FacialExpression.Tired, Night.SuggestedFace());
            Assert.AreEqual(FacialExpression.Worry, Storm.SuggestedFace());
        }

        [Test]
        public void LightWeatherIsScenery_NotAMood()
        {
            var drizzle = new ChatMoodContext(
                DayNightCycle.DayPhase.Day, WeatherIntensity.Light, true);

            Assert.AreEqual(FacialExpression.Neutral, drizzle.SuggestedFace(),
                "A vendor visibly unsettled by drizzle looks unsettled most of the time, " +
                "which spends the expression for nothing.");
        }

        [Test]
        public void EveryNewIntentMapsToAFace()
        {
            // The four intents added for this exist ONLY to give six drawings a cause. One
            // that mapped to Neutral would be a keyword table nothing downstream reads.
            var mustMove = new[]
            {
                DialogueIntent.Insult, DialogueIntent.Flirt,
                DialogueIntent.Danger, DialogueIntent.Distress,
            };

            foreach (DialogueIntent intent in mustMove)
            {
                Assert.AreNotEqual(FacialExpression.Neutral, ExpressionClassifier.FaceForIntent(intent),
                    intent + " reaches no face, so recognising it changes nothing on screen.");
            }
        }

        [TestCase("eres una ladrona", DialogueIntent.Insult)]
        [TestCase("me estás robando, tramposa", DialogueIntent.Insult)]
        [TestCase("qué guapa eres", DialogueIntent.Flirt)]
        [TestCase("hay monstruos en el bosque", DialogueIntent.Danger)]
        [TestCase("me atacaron unos bandidos", DialogueIntent.Danger)]
        [TestCase("estoy triste", DialogueIntent.Distress)]
        [TestCase("you are a thief", DialogueIntent.Insult)]
        [TestCase("beware, monsters", DialogueIntent.Danger)]
        public void PlayerTreatment_IsRecognised(string playerText, DialogueIntent expected)
        {
            Assert.AreEqual(expected, DialogueIntentClassifier.Classify(playerText));
        }

        [Test]
        public void AnInsultInsideAHaggle_IsReadAsTheInsult()
        {
            Assert.AreEqual(DialogueIntent.Insult,
                DialogueIntentClassifier.Classify("¿cuánto vale esto, ladrona?"),
                "The emotional sets are narrow and the trade set is broad, so a collision is " +
                "far likelier to be a real insult inside a haggle than the reverse — and of " +
                "the two, answering that with a price and a smile is the worse failure.");
        }

        [Test]
        public void OrdinaryTrade_StillReachesTheHagglingPools()
        {
            Assert.AreEqual(DialogueIntent.Trade,
                DialogueIntentClassifier.Classify("¿cuánto vale el borsch?"),
                "Putting four intents ahead of Trade must not have shadowed it.");
        }

        // ── helpers ─────────────────────────────────────────────────────────

        /// <summary>Line and face for one exchange.</summary>
        private static KeyValuePair<string, FacialExpression> Ask(
            OfflineDialogueProvider provider, NPCPersonaDefinition persona,
            NPCMemory memory, string playerText, ChatMoodContext world)
        {
            var request = new ChatRequest(persona, memory, playerText, default, world);
            ChatReply reply = provider.GenerateReplyAsync(
                request, System.Threading.CancellationToken.None).Result;

            return new KeyValuePair<string, FacialExpression>(reply.Text, reply.Expression);
        }

        /// <summary>
        /// A whole conversation, on a provider built fresh so the cursors start where a real
        /// Play entry starts.
        /// </summary>
        private static List<KeyValuePair<string, FacialExpression>> Converse(
            NPCPersonaDefinition persona, ChatMoodContext world, int rounds)
        {
            var provider = new OfflineDialogueProvider();
            var memory = new NPCMemory();
            var said = new List<KeyValuePair<string, FacialExpression>>();

            for (int r = 0; r < rounds; r++)
            {
                foreach (string line in PlayerLines)
                    said.Add(Ask(provider, persona, memory, line, world));
            }
            return said;
        }

        private static List<string> ReactionLinesFor(NPCPersonaDefinition persona, FacialExpression e)
        {
            var lines = new List<string>();
            persona.CollectReactions(e, lines);
            return lines;
        }

        private static NPCPersonaDefinition LoadGatita()
        {
            var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(GATITA);
            Assert.IsNotNull(persona, "Gatita's persona asset is missing at " + GATITA);
            return persona;
        }
    }
}
