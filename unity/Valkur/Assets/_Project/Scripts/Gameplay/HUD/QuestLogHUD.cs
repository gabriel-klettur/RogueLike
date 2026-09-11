using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The quest tracker: the corner window that says what the player is carrying and how
    /// far along each errand is.
    ///
    /// <para><b>IT IS A WINDOW NOW, NOT A LABEL.</b> It shipped as one TMP blob inside a
    /// non-interactive rectangle — no drag, no resize, no way to put it away, and nothing in
    /// it could be clicked. Three consequences followed from that one decision. A player who
    /// wanted the corner back had no verb for it. A quest DROPPED from the conversation panel
    /// stayed listed here (see <see cref="QuestManager.OnQuestAbandoned"/>, which did not
    /// exist). And the one action a tracker invites — "I am done carrying this" — was
    /// reachable only by walking back across the map to the character who gave it.</para>
    ///
    /// <para><b>Geometry is per MACHINE, not per save</b>, so it rides PlayerPrefs under
    /// <c>valkur.questlog.*</c> exactly as <c>MusicPlayerHUD</c>'s does — the editor workspace
    /// layer cannot take it, because every entry point there is typed on
    /// <c>GameEditorManager.IGameEditor</c> and keyed on an <c>EditorName</c> this is not.</para>
    ///
    /// <para><b>Refresh is EVENT-DRIVEN and that is why the abandon event had to exist.</b>
    /// Started, completed, abandoned, plus one subscription per live objective. The publisher
    /// that drives the minimap re-scans the world on a timer and heals itself; this panel
    /// cannot, so anything that changes the active log and stays quiet leaves a row here
    /// forever.</para>
    /// </summary>
    public sealed partial class QuestLogHUD : SingletonMonoBehaviour<QuestLogHUD>
    {
        [Tooltip("Manager driving the log. Auto-resolved via FindObjectOfType when null.")]
        [SerializeField] private QuestManager manager;

        /// <summary>Heading over the list. Spanish, like every other player-facing string.</summary>
        private const string QuestLogTitle = "MISIONES";

        // Per-objective subscription so we can detach on quest completion
        // without leaking event handlers.
        //
        // Keyed on ObjectiveBase rather than on KillCountObjective, which is what it
        // watched while that was the only kind of objective in existence. Every other
        // kind ticked in silence: a Collect or a Craft objective advanced, the quest
        // advanced with it, and the panel went on displaying the numbers it had drawn
        // when the quest was accepted until an unrelated kill forced a repaint.
        private readonly Dictionary<ObjectiveBase, System.Action<ObjectiveBase>> _objectiveHandlers
            = new Dictionary<ObjectiveBase, System.Action<ObjectiveBase>>();

        protected override bool Persist => false;

        public QuestManager Manager => manager;

        public void BindManager(QuestManager mgr)
        {
            UnbindManager();
            manager = mgr;
            if (manager == null) return;
            manager.OnQuestStarted   += OnQuestStarted;
            manager.OnQuestCompleted += OnQuestClosed;
            manager.OnQuestAbandoned += OnQuestClosed;
            // Subscribe to whatever active objectives already exist (rare but
            // possible if the HUD binds AFTER quests started — e.g. save load).
            foreach (var id in manager.ActiveIds)
                SubscribeQuestObjectives(id);
            Refresh();
        }

        protected override void OnSingletonAwake()
        {
            LoadWindowPrefs();
            EnsureBuilt();
            if (manager == null) manager = FindObjectOfType<QuestManager>();
            if (manager != null) BindManager(manager);
        }

        protected override void OnDestroy()
        {
            UnbindManager();
            base.OnDestroy();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private void UnbindManager()
        {
            if (manager != null)
            {
                manager.OnQuestStarted   -= OnQuestStarted;
                manager.OnQuestCompleted -= OnQuestClosed;
                manager.OnQuestAbandoned -= OnQuestClosed;
            }
            foreach (var kv in _objectiveHandlers)
                if (kv.Key != null) kv.Key.Progressed -= kv.Value;
            _objectiveHandlers.Clear();
        }

        private void OnQuestStarted(string questId)
        {
            SubscribeQuestObjectives(questId);

            // A quest arriving is the one moment a closed tracker has to come back. Without
            // it, CLOSE is a one-way door in a shipped build: the Quest Editor can reopen the
            // panel and the Quest Editor does not exist outside the Editor, so a player who
            // pressed the X once would never see a tracker again. Accepting an errand is the
            // player's own action and the instant the panel is most worth having, which makes
            // this the cheapest honest way back — the alternative was a new key, and a new key
            // means an action in ValkurInputActions AND a descriptor in the closed
            // InputActionCatalog.
            if (_closed) SetClosed(false);

            Refresh();
        }

        /// <summary>
        /// A quest leaving the active log, for either reason.
        ///
        /// <para>Completion and abandonment are DIFFERENT events elsewhere — one pays rewards
        /// and the other must not — and they are the same event here, because the tracker's
        /// only question is whether the row is still worth drawing.</para>
        /// </summary>
        private void OnQuestClosed(string questId)
        {
            // Drop any per-objective subscriptions for this quest so the
            // dictionary doesn't grow forever as quests rotate. The
            // objective objects themselves stop ticking after completion.
            var toRemove = new List<ObjectiveBase>();
            foreach (var kv in _objectiveHandlers)
            {
                if (kv.Key != null && kv.Key.Id.StartsWith(questId + "."))
                {
                    kv.Key.Progressed -= kv.Value;
                    toRemove.Add(kv.Key);
                }
            }
            foreach (var k in toRemove) _objectiveHandlers.Remove(k);

            // An armed drop button on the quest that just left would survive as an armed
            // button on whatever row slid up into its place.
            if (string.Equals(_armedDropId, questId, System.StringComparison.OrdinalIgnoreCase))
                _armedDropId = null;

            Refresh();
        }

        private void SubscribeQuestObjectives(string questId)
        {
            if (manager == null) return;
            var quest = manager.GetActiveQuest(questId);
            if (quest == null) return;
            foreach (var obj in quest.Objectives)
            {
                if (obj is ObjectiveBase ob && !_objectiveHandlers.ContainsKey(ob))
                {
                    System.Action<ObjectiveBase> handler = _ => Refresh();
                    ob.Progressed += handler;
                    _objectiveHandlers[ob] = handler;
                }
            }
        }

        /// <summary>
        /// The whole log as one string.
        ///
        /// <para>Kept as the panel became rows, because it is the seam a fixture can read
        /// without a Canvas — uGUI performs no layout in Edit Mode, so an assertion against
        /// the drawn rows would be measuring rects nothing has laid out.</para>
        /// </summary>
        public string ComputeLogText()
        {
            if (manager == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var id in manager.ActiveIds)
            {
                var quest = manager.GetActiveQuest(id);
                if (quest == null) continue;
                sb.Append(string.IsNullOrEmpty(quest.DisplayName) ? quest.Id : quest.DisplayName);
                sb.Append('\n');
                foreach (var obj in quest.Objectives)
                {
                    if (obj == null) continue;
                    sb.Append("  - ");
                    sb.Append(obj.Description);
                    sb.Append(" (");
                    sb.Append(obj.Current);
                    sb.Append('/');
                    sb.Append(obj.Target);
                    sb.Append(')');
                    if (obj.IsComplete) sb.Append(" (done)");
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
