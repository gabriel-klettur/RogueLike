using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// The quest layer's front door: owns the catalogue, owns the one
    /// <see cref="QuestManager"/>, answers "what can this character offer me right
    /// now", and speaks the hook and completion lines through whoever is standing
    /// in front of the player.
    ///
    /// <para><b>Why a service and not more QuestManager.</b> The manager tracks
    /// PROGRESS and knows nothing about who hands a quest over or what the player
    /// has to be to receive it — that split is what lets the manager be tested with
    /// three hand-built objectives and no catalogue, no personas and no chat.</para>
    ///
    /// <para>The catalogue comes from <c>Resources</c> because this component is
    /// <c>AddComponent</c>-ed by the boot sequence and has no inspector slot anybody
    /// could fill; the field is exposed through <see cref="SetCatalog"/> so a
    /// fixture can inject one, which is the same shape <c>ChatSystem.SetCatalog</c>
    /// took after its own null-for-the-life-of-the-project defect.</para>
    /// </summary>
    public sealed class QuestService : MonoBehaviour
    {
        public static QuestService Instance { get; private set; }

        private QuestCatalog _catalog;
        private QuestManager _manager;

        private readonly List<QuestDefinition> _giverScratch = new List<QuestDefinition>();

        /// <summary>
        /// What this character has already said out loud in the CURRENT conversation.
        ///
        /// <para>Without it the same paragraph is spoken twice. The hook and completion
        /// lines are announced when the panel opens, and the Entregar button re-raises
        /// <c>OnNpcConversed</c> on purpose — one completion path whether the player
        /// pressed a button or simply walked up — so the handler ran a second time and
        /// repeated the line verbatim into the transcript, and into the journal, which
        /// records what a character is remembered to have said.</para>
        ///
        /// <para>Cleared on close rather than on open: <c>OnChatOpened</c> and the first
        /// <c>OnNpcConversed</c> land in the same frame in an order nothing pins, and
        /// clearing on the wrong side of that would wipe the entry just written.</para>
        /// </summary>
        private readonly HashSet<string> _announcedThisConversation =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _chatHooked;

        /// <summary>Fires (personaId) when a character has something new to offer.</summary>
        public event Action<string> OnOffersAvailable;

        /// <summary>
        /// The one tracker. Resolved lazily rather than only in <c>Awake</c>.
        ///
        /// <para>Unity does not call <c>Awake</c> on a component added in Edit Mode, so a
        /// fixture that builds this service got a null manager and every method here
        /// returned its empty-guard answer — the tests would have passed while measuring
        /// nothing. The same laziness removes a real ordering hazard in Play Mode: anything
        /// that reaches the service before its Awake has run now gets a working manager
        /// instead of silently getting nothing.</para>
        /// </summary>
        public QuestManager Manager
        {
            get
            {
                if (_manager == null)
                    _manager = GetComponent<QuestManager>() ?? gameObject.AddComponent<QuestManager>();
                return _manager;
            }
        }

        public QuestCatalog Catalog => _catalog;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            // Losing the race leaves the second one INERT rather than destroyed:
            // Object.Destroy is an outright error in Edit Mode, and this component is
            // reachable from EditMode fixtures.
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }
            Instance = this;

            _manager = GetComponent<QuestManager>() ?? gameObject.AddComponent<QuestManager>();

            if (_catalog == null)
                _catalog = Resources.Load<QuestCatalog>(QuestCatalog.ResourcePath);

            if (_catalog == null)
            {
                Debug.LogWarning("[QuestService] No QuestCatalog at Resources/" +
                                 QuestCatalog.ResourcePath + " — no quest can be offered. " +
                                 "Run Valkur > Quests > Seed Quest Content.");
            }

            ServiceLocator.Register(this);
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            GameEvents.OnNpcConversed += HandleNpcConversed;
            // Through the property: Awake has run by now in Play Mode, but a fixture that
            // enables this component without one would otherwise silently never subscribe.
            if (Manager != null) Manager.OnQuestCompleted += AnnounceCompletion;
        }

        private void OnDisable()
        {
            GameEvents.OnNpcConversed -= HandleNpcConversed;
            if (_manager != null) _manager.OnQuestCompleted -= AnnounceCompletion;

            if (_chatHooked && ChatSystem.Instance != null)
                ChatSystem.Instance.OnChatClosed -= ForgetConversation;
            _chatHooked = false;
        }

        /// <summary>
        /// Subscribes to the chat's close event the first time a conversation actually
        /// happens.
        ///
        /// <para>It cannot be done in <c>OnEnable</c>: this service is built BEFORE the
        /// chat system in the boot sequence — deliberately, so it is listening before the
        /// first conversation can open — and <c>ChatSystem.Instance</c> is therefore null
        /// at that moment. A subscription attempted there is not an error, it is a
        /// no-op, which is the worst of the three outcomes: the guard would never clear
        /// and every character would repeat themselves for the rest of the session.</para>
        /// </summary>
        private void EnsureChatHooked()
        {
            if (_chatHooked || ChatSystem.Instance == null) return;
            ChatSystem.Instance.OnChatClosed += ForgetConversation;
            _chatHooked = true;
        }

        private void ForgetConversation() => _announcedThisConversation.Clear();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Domain Reload is OFF, so <see cref="Instance"/> survives into the next Play
        /// session pointing at a destroyed component — and every caller here null-checks
        /// with <c>!= null</c>, which a destroyed Unity object passes for exactly as long
        /// as it takes to throw on the first member access.
        ///
        /// <para>The assignment has to be a bare <c>stsfld</c> for the ratchet to
        /// recognise it: <c>DomainReloadStaticResetTests</c> reads the hook's raw IL, and
        /// a reset routed through a helper counts as no reset at all.</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
        }

        /// <summary>Inject a catalogue. For fixtures and for the seeder's dry run.</summary>
        public void SetCatalog(QuestCatalog catalog) => _catalog = catalog;

        // ── Offers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Every quest <paramref name="personaId"/> can offer the player right now:
        /// theirs to give, not already taken, not already done, level met,
        /// prerequisites cleared.
        ///
        /// <para>Fills <paramref name="into"/> rather than returning a list, so the
        /// chat panel can call it on every repaint without allocating.</para>
        /// </summary>
        public void OffersFor(string personaId, List<QuestDefinition> into)
        {
            if (into == null) return;
            into.Clear();
            if (_catalog == null || Manager == null || string.IsNullOrEmpty(personaId)) return;

            int level = PlayerLevel();
            _catalog.CollectByGiver(personaId, _giverScratch);
            for (int i = 0; i < _giverScratch.Count; i++)
            {
                var q = _giverScratch[i];
                if (Manager.IsEligible(q, level)) into.Add(q);
            }
        }

        /// <summary>
        /// Active quests whose turn-in is <paramref name="personaId"/> and whose
        /// authored work is finished — i.e. what this character is waiting to be
        /// told about.
        /// </summary>
        public void TurnInsReadyFor(string personaId, List<QuestDefinition> into)
        {
            if (into == null) return;
            into.Clear();
            if (Manager == null || string.IsNullOrEmpty(personaId)) return;

            foreach (string id in Manager.ActiveIds)
            {
                var def = Manager.GetActiveDefinition(id);
                if (def == null) continue;
                if (!string.Equals(def.turnInPersonaId, personaId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!AuthoredWorkDone(id, def)) continue;
                into.Add(def);
            }
        }

        /// <summary>
        /// True when every objective the DESIGNER wrote is done — the generated
        /// turn-in Talk step is excluded, because it is the thing being waited for.
        /// It is identified by position (last) and only when the quest declares a
        /// turn-in, which is exactly how <c>QuestManager</c> appends it.
        /// </summary>
        private bool AuthoredWorkDone(string questId, QuestDefinition def)
        {
            var quest = Manager.GetActiveQuest(questId);
            if (quest == null) return false;

            int count = quest.Objectives.Count;
            if (!string.IsNullOrEmpty(def.turnInPersonaId)) count--;

            for (int i = 0; i < count; i++)
            {
                var obj = quest.Objectives[i];
                if (obj != null && !obj.IsComplete) return false;
            }
            return true;
        }

        /// <summary>
        /// Quests this character handed over that the player is still working on.
        ///
        /// <para>Without this the giver FORGETS YOU: <see cref="OffersFor"/> excludes an
        /// active quest and <see cref="TurnInsReadyFor"/> only lists finished ones, so
        /// from the moment the player accepts until the moment they finish, the
        /// character who sent them has nothing to say and the Misiones button
        /// disappears entirely — which reads as the quest never having existed.</para>
        /// </summary>
        public void ActiveFrom(string personaId, List<QuestDefinition> into)
        {
            if (into == null) return;
            into.Clear();
            if (Manager == null || string.IsNullOrEmpty(personaId)) return;

            foreach (string id in Manager.ActiveIds)
            {
                var def = Manager.GetActiveDefinition(id);
                if (def == null) continue;

                // Either end of the errand counts: the character who sent the player and
                // the character they must report back to are usually the same, but a
                // courier quest deliberately splits them.
                bool mine =
                    string.Equals(def.giverPersonaId, personaId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.turnInPersonaId, personaId, StringComparison.OrdinalIgnoreCase);
                if (!mine) continue;

                // The ones that are DONE belong to the turn-in list, which is drawn above
                // this one and carries the button that closes them.
                if (AuthoredWorkDone(id, def)) continue;

                into.Add(def);
            }
        }

        /// <summary>
        /// Quests this character WOULD offer if the player were further along: the level
        /// is too low, or a prerequisite is unfinished.
        ///
        /// <para><b>Why show them at all.</b> <see cref="OffersFor"/> filters them out, so
        /// a level-10 quest is simply absent — and absent is indistinguishable from
        /// non-existent. The player cannot see that the smith has something worth coming
        /// back for, which removes the only reason to come back. Greyed with its
        /// requirement, the same row becomes a goal.</para>
        ///
        /// <para>Already-active and already-completed quests are NOT here: those are not
        /// locked, they are done or under way, and each has its own section.</para>
        /// </summary>
        public void LockedFor(string personaId, List<QuestDefinition> into)
        {
            if (into == null) return;
            into.Clear();
            if (_catalog == null || Manager == null || string.IsNullOrEmpty(personaId)) return;

            int level = PlayerLevel();
            _catalog.CollectByGiver(personaId, _giverScratch);
            for (int i = 0; i < _giverScratch.Count; i++)
            {
                var q = _giverScratch[i];
                if (q == null) continue;
                if (Manager.IsActive(q.questId) || Manager.IsCompleted(q.questId)) continue;
                if (Manager.IsEligible(q, level)) continue;   // it is on offer, not locked
                into.Add(q);
            }
        }

        /// <summary>
        /// Why <paramref name="def"/> cannot be taken yet, as a line for the player. Empty
        /// when nothing blocks it.
        ///
        /// <para>Reports the LEVEL first and then a single missing prerequisite, because
        /// listing every unmet condition at once turns a nudge into a wall of text — and
        /// the level is the one the player can act on without knowing the chain.</para>
        /// </summary>
        public string DescribeLock(QuestDefinition def)
        {
            if (def == null || Manager == null) return string.Empty;

            int level = PlayerLevel();
            if (def.requiredLevel > 0 && level < def.requiredLevel)
                return Chat.ChatLanguage.QuestNeedsLevel(def.requiredLevel);

            if (def.prerequisiteQuestIds != null)
            {
                for (int i = 0; i < def.prerequisiteQuestIds.Length; i++)
                {
                    string pre = def.prerequisiteQuestIds[i];
                    if (string.IsNullOrEmpty(pre) || Manager.IsCompleted(pre)) continue;

                    var preDef = _catalog != null ? _catalog.Find(pre) : null;
                    string name = preDef != null && !string.IsNullOrWhiteSpace(preDef.displayName)
                        ? preDef.displayName : pre;
                    return Chat.ChatLanguage.QuestNeedsQuest(name);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Drops an accepted quest. A thin pass-through, so the panel never has to reach
        /// past the service into the manager.
        /// </summary>
        public bool Abandon(string questId)
        {
            if (Manager == null || !Manager.IsActive(questId)) return false;
            Manager.AbandonQuest(questId);
            return true;
        }

        /// <summary>
        /// Accept a quest by id. Refuses one the player is not eligible for, so a
        /// stale offer button cannot hand over a quest whose prerequisite was
        /// abandoned between the panel being drawn and being clicked.
        /// </summary>
        public bool TryAccept(string questId)
        {
            if (_catalog == null || Manager == null) return false;
            var def = _catalog.Find(questId);
            if (def == null) return false;
            if (!Manager.IsEligible(def, PlayerLevel())) return false;
            return Manager.StartQuest(def);
        }

        // ── Speaking through the character ─────────────────────────────────────

        /// <summary>
        /// When a conversation opens, the character mentions what they have. This is
        /// the only thing in the shipped build that makes a quest DISCOVERABLE — a
        /// control with no prompt is a control that does not exist, and the same
        /// argument that put "Conversar" over an NPC's head applies to a quest the
        /// player has no reason to ask about.
        ///
        /// <para>It speaks at most ONE offer and one completion per conversation.
        /// A character with four things to say would otherwise open with a wall of
        /// text, and the panel's own list is where the rest belong.</para>
        /// </summary>
        private void HandleNpcConversed(string personaId)
        {
            var chat = ChatSystem.Instance;
            if (chat == null || !chat.IsChatOpen) return;

            EnsureChatHooked();

            var ready = new List<QuestDefinition>();
            TurnInsReadyFor(personaId, ready);
            if (ready.Count > 0)
            {
                var done = ready[0];
                if (_announcedThisConversation.Add("done:" + done.questId))
                    chat.SpeakAsActiveNpc(string.IsNullOrWhiteSpace(done.completionLine)
                        ? $"Lo has conseguido: {done.displayName}."
                        : done.completionLine);
                return;
            }

            var offers = new List<QuestDefinition>();
            OffersFor(personaId, offers);
            if (offers.Count == 0) return;

            var offer = offers[0];
            if (_announcedThisConversation.Add("offer:" + offer.questId))
                chat.SpeakAsActiveNpc(string.IsNullOrWhiteSpace(offer.hookLine)
                    ? offer.description
                    : offer.hookLine);

            OnOffersAvailable?.Invoke(personaId);
        }

        /// <summary>
        /// Says what the quest PAID, on the frame it closes.
        ///
        /// <para>Completing one used to produce no confirmation anywhere: the rewards
        /// landed in four different systems — xp, points, coins, items — and the only
        /// evidence was that a line vanished from the tracker. A player cannot tell a
        /// quest that paid from one that silently failed to.</para>
        ///
        /// <para>It goes through the TOAST rather than the conversation, because the
        /// quest can also be completed by a poll while the player is nowhere near
        /// anybody — a Collect finishing as the last ore is picked up.</para>
        /// </summary>
        private void AnnounceCompletion(string questId)
        {
            var def = _catalog != null ? _catalog.Find(questId) : null;
            if (def == null) return;

            var parts = new List<string>(4);
            if (def.xpReward > 0)          parts.Add(def.xpReward + " xp");
            if (def.coinReward > 0)        parts.Add(def.coinReward + " monedas");
            if (def.skillPointReward > 0)  parts.Add(def.skillPointReward + " talento" + (def.skillPointReward == 1 ? "" : "s"));
            if (def.arcanePointReward > 0) parts.Add(def.arcanePointReward + " arcano" + (def.arcanePointReward == 1 ? "" : "s"));

            string reward = parts.Count > 0 ? "  (" + string.Join(", ", parts.ToArray()) + ")" : string.Empty;
            Valkur.Gameplay.Combat.ToastSystem.Show("Mision completada: " + def.displayName + reward, 4f);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// The player's level, or 1 when there is no player yet. Never 0: a quest
        /// gated at level 1 — the shipped default for an introductory quest — would
        /// be refused for the whole boot window, which is exactly when the game is
        /// restoring quests from a save.
        /// </summary>
        public static int PlayerLevel()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return 1;
            var xp = player.GetComponent<Experience>();
            return xp != null ? Mathf.Max(1, xp.Level) : 1;
        }

        /// <summary>Definitions as the save layer wants them: a flat list to resolve ids against.</summary>
        public IReadOnlyList<QuestDefinition> Definitions =>
            _catalog != null ? _catalog.quests : (IReadOnlyList<QuestDefinition>)Array.Empty<QuestDefinition>();
    }
}
