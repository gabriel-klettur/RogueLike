using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for the request budget — the only thing standing between a held-down
    /// Enter key and an unbounded bill.
    ///
    /// <para>Nothing else in the chat rate-limits anything: every message is one request, and
    /// the whole persona prompt (profile, stock list, purse, shared rules) is re-sent on each
    /// one. The two rules are deliberately different in kind — the cooldown bounds the RATE
    /// and postpones, the ceiling bounds the TOTAL and ends the session's spending — and both
    /// degrade to the authored lines rather than refusing, which is the same trade every other
    /// failure in this provider makes.</para>
    ///
    /// <para>The rules are tested through the pure <c>CheckBudget</c> rather than through a
    /// conversation, because reaching them any other way needs a real API key in the
    /// environment: without one the gate stops at the key check and the budget is never
    /// consulted. A limit that can only be exercised on a machine that has a key is a limit
    /// nobody can trust.</para>
    /// </summary>
    [TestFixture]
    public class ChatLlmBudgetTests
    {
        private ChatLlmSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<ChatLlmSettings>();
            _settings.minSecondsBetweenRequests = 2f;
            _settings.maxRequestsPerSession = 3;
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) Object.DestroyImmediate(_settings);
        }

        // ── The ceiling ─────────────────────────────────────────────────────

        [Test]
        public void CheckBudget_UnderTheCeiling_Allows()
        {
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.Allowed,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 2, lastRequestAt: -1f, now: 100f));
        }

        [Test]
        public void CheckBudget_AtTheCeiling_Stops()
        {
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.CeilingReached,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 3, lastRequestAt: -1f, now: 100f),
                "The ceiling is a count of requests ALLOWED, so the fourth against a limit of " +
                "three is the one refused.");
        }

        [Test]
        public void CheckBudget_CeilingBeatsCooldown()
        {
            // Both rules are violated. The ceiling has to win, because the two produce
            // different outcomes: the ceiling puts the model away for the session, while
            // cooling is expected to pass on the next message. Reporting "cooling" here
            // would mean the session never actually stops spending.
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.CeilingReached,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 9, lastRequestAt: 100f, now: 100.1f));
        }

        [Test]
        public void CheckBudget_ZeroCeiling_MeansNoCeiling()
        {
            _settings.maxRequestsPerSession = 0;

            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.Allowed,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 10_000, lastRequestAt: -1f, now: 100f),
                "0 is documented as 'no ceiling'. Reading it as 'no requests' would silently " +
                "take the model away from anyone who cleared the field.");
        }

        // ── The cooldown ────────────────────────────────────────────────────

        [Test]
        public void CheckBudget_InsideTheCooldown_Cools()
        {
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.Cooling,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 0, lastRequestAt: 100f, now: 101f));
        }

        [Test]
        public void CheckBudget_AfterTheCooldown_Allows()
        {
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.Allowed,
                OpenAiChatProvider.CheckBudget(_settings, requestsSoFar: 0, lastRequestAt: 100f, now: 102.5f));
        }

        [Test]
        public void RemainingCooldown_BeforeAnyRequest_IsZero()
        {
            Assert.AreEqual(0f, OpenAiChatProvider.RemainingCooldown(_settings, lastRequestAt: -1f, now: 0f),
                "A negative stamp means nothing has been sent yet. Using 0 as that sentinel " +
                "would make the very first message of a session wait out the cooldown, since " +
                "0 is a real timestamp at boot.");
        }

        [Test]
        public void RemainingCooldown_CountsDownAndNeverGoesNegative()
        {
            Assert.AreEqual(1.5f, OpenAiChatProvider.RemainingCooldown(_settings, 100f, 100.5f), 0.001f);
            Assert.AreEqual(0f, OpenAiChatProvider.RemainingCooldown(_settings, 100f, 999f), 0.001f);
        }

        [Test]
        public void RemainingCooldown_ZeroInterval_NeverWaits()
        {
            _settings.minSecondsBetweenRequests = 0f;

            Assert.AreEqual(0f, OpenAiChatProvider.RemainingCooldown(_settings, 100f, 100f), 0.001f);
        }

        [Test]
        public void CheckBudget_NullSettings_Allows()
        {
            Assert.AreEqual(OpenAiChatProvider.BudgetVerdict.Allowed,
                OpenAiChatProvider.CheckBudget(null, 0, -1f, 0f),
                "The gate checks for null settings before this is reached; answering " +
                "'CeilingReached' here would hide that ordering behind the wrong reason.");
        }

        // ── The shipped asset ───────────────────────────────────────────────

        [Test]
        public void ShippedSettings_ArrivesWithBothLimitsArmed()
        {
            var shipped = Resources.Load<ChatLlmSettings>("Chat/ChatLlmSettings");
            Assert.IsNotNull(shipped,
                "Resources/Chat/ChatLlmSettings is what bootstrap loads; without it the chat " +
                "is offline-only.");

            Assert.Greater(shipped.minSecondsBetweenRequests, 0f,
                "A cooldown of 0 puts the shipped game back to one request per keystroke.");
            Assert.Greater(shipped.maxRequestsPerSession, 0,
                "The ceiling is the backstop the cooldown cannot be: a cooldown bounds the " +
                "rate, not the afternoon.");
        }

        [Test]
        public void ShippedSettings_KeepsTheKeyOutOfTheAsset()
        {
            var shipped = Resources.Load<ChatLlmSettings>("Chat/ChatLlmSettings");

            Assert.IsNotEmpty(shipped.apiKeyEnvVar,
                "The asset names the environment variable and never holds the key itself.");
            StringAssert.DoesNotContain("sk-", shipped.apiKeyEnvVar,
                "A key pasted into this field would be committed to the repository.");
        }
    }
}
