using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Runtime quest tracker. Owns the player's active and completed
    /// quests, builds <see cref="IObjective"/> instances from
    /// <see cref="QuestDefinition"/> assets, and dispatches rewards
    /// (XP, skill points, arcane points, coins, items) on completion.
    ///
    /// Design choices:
    ///   - <see cref="StartQuest"/> is idempotent: starting an already-
    ///     active or already-completed quest is a silent no-op (saves
    ///     UI from having to track button-disabled state). It does NOT
    ///     check level or prerequisites — that is <see cref="IsEligible"/>,
    ///     kept separate so the console and the tests can force a quest on.
    ///   - Active quests own their <see cref="Quest"/> + objective
    ///     instances. Completed quests are stored as ids only — the
    ///     objective objects can GC.
    ///   - Save/load: <see cref="ToSnapshot"/> persists active quest ids
    ///     and progress per objective. <see cref="FromSnapshot"/> rebuilds
    ///     IObjective instances; objectives that no longer exist on the
    ///     QuestDefinition (designer pruned) are silently dropped.
    ///
    /// <para><b>The turn-in objective is generated, never authored.</b> A quest
    /// declaring <c>turnInPersonaId</c> gets an extra <see cref="TalkObjective"/>
    /// appended, gated on every authored objective already being complete. Authored
    /// by hand it would tick on the very conversation that handed the quest over,
    /// because the giver and the turn-in are usually the same character.</para>
    ///
    /// <para><b>Polled objectives are ticked here.</b> Collect / EarnCoins /
    /// ReachLevel / Survive answer by looking at the world rather than by
    /// subscribing, and this is the only MonoBehaviour in the layer, so it owns the
    /// clock. <see cref="PollIntervalSeconds"/> is a quarter of a second: a bag
    /// count and a purse balance change a few times a minute, and the one
    /// time-based objective accumulates <c>deltaTime</c> itself.</para>
    ///
    /// Item rewards rely on the player having an Inventory component;
    /// the manager looks one up on the levelled entity each time. NPCs
    /// completing quests (rare but possible for companion AI) without
    /// inventories silently skip item rewards.
    /// </summary>
    public sealed partial class QuestManager : MonoBehaviour
    {
        /// <summary>How often polled objectives re-read the world.</summary>
        public const float PollIntervalSeconds = 0.25f;

        // Per-quest entry holding the live IObjectives and the SO it came
        // from. Keys by questId.
        private sealed class ActiveEntry
        {
            public QuestDefinition Definition;
            public Quest Quest;
            public List<IObjective> Objectives;
        }

        private readonly Dictionary<string, ActiveEntry> _active = new Dictionary<string, ActiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reused by the poll tick so a per-frame iteration allocates nothing.
        private readonly List<ActiveEntry> _pollScratch = new List<ActiveEntry>();

        private float _pollTimer;

        public IReadOnlyCollection<string> ActiveIds    => _active.Keys;
        public IReadOnlyCollection<string> CompletedIds => _completed;

        /// <summary>Fires (questId) when a quest is started.</summary>
        public event Action<string> OnQuestStarted;
        /// <summary>Fires (questId) when a quest completes.</summary>
        public event Action<string> OnQuestCompleted;

        /// <summary>
        /// Fires (questId) when a quest is DROPPED. Its own event, and it has to be.
        ///
        /// <para>There were two, and abandoning is neither: it is not a start, and reusing
        /// the completion event would pay the player for quitting — <c>QuestService</c>
        /// subscribes <c>AnnounceCompletion</c> to that one and would toast the rewards of a
        /// quest nobody finished. So the drop was SILENT, and the one listener that cannot
        /// survive silence is the tracker: <c>QuestMarkerPublisher</c> re-scans the world
        /// twice a second and heals itself, the badge with it, but the corner panel is
        /// event-driven — so a dropped quest stayed listed there for the rest of the
        /// session, with its counters live and no way to be rid of it.</para>
        /// </summary>
        public event Action<string> OnQuestAbandoned;

        /// <summary>Optional inventory used to grant item rewards. Auto-resolved
        /// from EntityRegistry.Player on first need when null.</summary>
        public Inventory.Inventory Inventory;

        // ── Public API ──────────────────────────────────────────────────────────

        public bool IsActive(string questId)    => questId != null && _active.ContainsKey(questId);
        public bool IsCompleted(string questId) => questId != null && _completed.Contains(questId);

        public Quest GetActiveQuest(string questId)
        {
            if (questId == null) return null;
            return _active.TryGetValue(questId, out var entry) ? entry.Quest : null;
        }

        public QuestDefinition GetActiveDefinition(string questId)
        {
            if (questId == null) return null;
            return _active.TryGetValue(questId, out var entry) ? entry.Definition : null;
        }

        /// <summary>
        /// Whether <paramref name="def"/> may be OFFERED to the player right now:
        /// not already taken, not already done, level met, prerequisites done.
        ///
        /// <para>Deliberately separate from <see cref="StartQuest"/>. The console
        /// and the test fixtures need to force a quest on without standing up a
        /// levelled player and a chain of prerequisites, and folding the check into
        /// StartQuest would make "give me that quest" untestable.</para>
        /// </summary>
        public bool IsEligible(QuestDefinition def, int playerLevel)
        {
            if (def == null || string.IsNullOrEmpty(def.questId)) return false;
            if (IsActive(def.questId) || IsCompleted(def.questId)) return false;
            if (def.requiredLevel > 0 && playerLevel < def.requiredLevel) return false;

            if (def.prerequisiteQuestIds != null)
            {
                for (int i = 0; i < def.prerequisiteQuestIds.Length; i++)
                {
                    string pre = def.prerequisiteQuestIds[i];
                    if (string.IsNullOrEmpty(pre)) continue;
                    if (!IsCompleted(pre)) return false;
                }
            }
            return true;
        }

        public bool StartQuest(QuestDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.questId)) return false;
            if (_active.ContainsKey(def.questId)) return false;
            if (_completed.Contains(def.questId)) return false;

            var objectives = BuildObjectives(def);
            var quest = new Quest(def.questId, def.displayName, objectives);
            quest.OnCompleted += () => HandleCompletion(def);
            quest.Begin();

            // Edge case: a degenerate quest (zero objectives) auto-completes
            // inside Begin; HandleCompletion already fired and the quest is
            // in _completed. Don't add it back to _active.
            if (quest.IsCompleted)
            {
                OnQuestStarted?.Invoke(def.questId);
                return true;
            }

            _active[def.questId] = new ActiveEntry
            {
                Definition = def,
                Quest = quest,
                Objectives = objectives,
            };
            OnQuestStarted?.Invoke(def.questId);
            return true;
        }

        /// <summary>
        /// Drops an accepted quest: its objectives stop listening, it leaves the active log,
        /// and it goes back on offer (it is NOT added to <c>_completed</c>, so the giver will
        /// hand it over again — quitting is a change of mind, not a failure state).
        /// </summary>
        public void AbandonQuest(string questId)
        {
            if (questId == null || !_active.TryGetValue(questId, out var entry)) return;
            entry.Quest.End();
            _active.Remove(questId);

            // AFTER the removal, never before: a listener asks the manager what is active
            // the moment it is told, and one told mid-removal redraws the very row it was
            // notified about.
            OnQuestAbandoned?.Invoke(questId);
        }

        /// <summary>
        /// Takes a quest OUT of the completed set so it can be offered and played again.
        /// An AUTHORING action, reached only from the Quests editor.
        ///
        /// <para>There is no gameplay path to this and there must not be: <c>_completed</c> is
        /// what stops a quest paying out twice, so anything the player can press that empties
        /// it is a way to farm rewards. What it exists for is the other half of testing a
        /// chain — an author who has just watched a quest finish cannot watch it again without
        /// deleting a save, and deleting the save takes the rest of the run with it.</para>
        /// </summary>
        public bool ForgetCompleted(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return false;
            return _completed.Remove(questId);
        }

        // ── Poll tick ──────────────────────────────────────────────────────────

        private void Update()
        {
            if (_active.Count == 0) return;

            _pollTimer += Time.deltaTime;
            if (_pollTimer < PollIntervalSeconds) return;
            _pollTimer = 0f;

            // Snapshot before iterating: a poll can complete a quest, and
            // HandleCompletion removes it from _active mid-loop.
            _pollScratch.Clear();
            foreach (var entry in _active.Values) _pollScratch.Add(entry);
            for (int i = 0; i < _pollScratch.Count; i++) _pollScratch[i].Quest.Poll();
            _pollScratch.Clear();
        }

        // ── Save/load ──────────────────────────────────────────────────────────

        [Serializable]
        public class Snapshot
        {
            public List<string> activeQuestIds = new List<string>();
            public List<string> completedQuestIds = new List<string>();
            // Per-active-quest objective progress. List index aligns with
            // activeQuestIds; each entry is the per-objective Current values.
            public List<int[]> activeProgress = new List<int[]>();
        }

        public Snapshot ToSnapshot()
        {
            var snap = new Snapshot();
            foreach (var kv in _active)
            {
                snap.activeQuestIds.Add(kv.Key);
                var counters = new int[kv.Value.Objectives.Count];
                for (int i = 0; i < kv.Value.Objectives.Count; i++)
                    counters[i] = kv.Value.Objectives[i]?.Current ?? 0;
                snap.activeProgress.Add(counters);
            }
            foreach (var id in _completed)
                snap.completedQuestIds.Add(id);
            return snap;
        }

        /// <summary>
        /// Rebuild quest state from <paramref name="snap"/> using the supplied
        /// catalog to resolve quest ids. Active quests rebuild their
        /// objectives and replay progress so a save loaded mid-quest
        /// resumes where it left off. Completed quests come back as id-only.
        /// </summary>
        public void FromSnapshot(Snapshot snap, IReadOnlyList<QuestDefinition> catalog)
        {
            // Tear down current state.
            foreach (var entry in _active.Values) entry.Quest.End();
            _active.Clear();
            _completed.Clear();

            if (snap == null) return;

            // Completed FIRST, because a restored active quest's prerequisites and
            // any chain logic read that set, and because StartQuest refuses a quest
            // already marked done.
            if (snap.completedQuestIds != null)
                foreach (var id in snap.completedQuestIds)
                    if (!string.IsNullOrEmpty(id)) _completed.Add(id);

            if (snap.activeQuestIds == null || catalog == null) return;
            for (int i = 0; i < snap.activeQuestIds.Count; i++)
            {
                string id = snap.activeQuestIds[i];
                var def = FindDef(catalog, id);
                if (def == null)
                {
                    Debug.LogWarning($"[QuestManager] Snapshot references unknown quest " +
                                     $"id '{id}' — skipping. Definition may have been pruned.");
                    continue;
                }
                StartQuest(def);
                if (snap.activeProgress != null && i < snap.activeProgress.Count &&
                    _active.TryGetValue(id, out var entry))
                {
                    var counters = snap.activeProgress[i];
                    if (counters != null)
                    {
                        // Seed each objective's counter WITHOUT raising its progress
                        // event: a restore that reported progress would re-run the
                        // completion path and pay the rewards a second time.
                        // Polled objectives overwrite this on their first tick, which
                        // is correct — a bag is the truth about what is in the bag.
                        for (int j = 0; j < entry.Objectives.Count && j < counters.Length; j++)
                            RestoreObjectiveProgress(entry.Objectives[j], counters[j]);
                    }
                }
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private List<IObjective> BuildObjectives(QuestDefinition def)
        {
            var list = new List<IObjective>((def.objectives?.Length ?? 0) + 1);
            if (def.objectives != null)
            {
                for (int i = 0; i < def.objectives.Length; i++)
                {
                    var entry = def.objectives[i];
                    IObjective obj = BuildObjective(def.questId, i, entry);
                    if (obj != null) list.Add(obj);
                }
            }

            AppendTurnIn(def, list);
            return list;
        }

        /// <summary>
        /// Adds the "go back and report" step for a quest that names a
        /// <c>turnInPersonaId</c>. The gate closes over the list built so far, so
        /// the conversation only counts once every authored objective is done —
        /// including the case where the giver IS the turn-in, which is the normal
        /// one and the one a hand-authored Talk objective gets wrong.
        /// </summary>
        private static void AppendTurnIn(QuestDefinition def, List<IObjective> list)
        {
            if (def == null || string.IsNullOrEmpty(def.turnInPersonaId)) return;

            var authored = list.ToArray();
            bool AllAuthoredDone()
            {
                for (int i = 0; i < authored.Length; i++)
                    if (authored[i] != null && !authored[i].IsComplete) return false;
                return true;
            }

            // The CHARACTER's name, never the persona id. Measured live, this line read
            // "Vuelve a hablar con vendor_banker_abigail" in the tracker, in the quest
            // sheet and on the minimap label — a database key shown to the player in the
            // one sentence that tells them where to go.
            string who = ResolvePersonaDisplayName(def.turnInPersonaId);
            string desc = string.IsNullOrEmpty(who)
                ? "Informa del resultado"
                : $"Vuelve a hablar con {who}";

            list.Add(new TalkObjective($"{def.questId}.turnin", desc,
                                       def.turnInPersonaId, AllAuthoredDone));
        }

        private static IObjective BuildObjective(string questId, int index, ObjectiveEntry entry)
        {
            string id = $"{questId}.obj{index}";
            string desc = string.IsNullOrEmpty(entry.description)
                ? AutoDescription(entry) : entry.description;
            int count = Mathf.Max(1, entry.count);

            switch (entry.kind)
            {
                case ObjectiveKind.KillCount:
                    return new KillCountObjective(id, desc, count, entry.targetId);
                case ObjectiveKind.Collect:
                    return new CollectItemObjective(id, desc, count, entry.targetId, entry.consumeOnComplete);
                case ObjectiveKind.Reach:
                    return new ReachZoneObjective(id, desc, entry.targetId);
                case ObjectiveKind.Talk:
                    return new TalkObjective(id, desc, entry.targetId);
                case ObjectiveKind.Craft:
                    return new CraftObjective(id, desc, count, entry.targetId);
                case ObjectiveKind.CastSpell:
                    return new CastSpellObjective(id, desc, count, entry.targetId);
                case ObjectiveKind.Survive:
                    return new SurviveObjective(id, desc, count);
                case ObjectiveKind.ReachLevel:
                    return new ReachLevelObjective(id, desc, count);
                case ObjectiveKind.EarnCoins:
                    return new EarnCoinsObjective(id, desc, count);
                case ObjectiveKind.FellTrees:
                    return new FellTreesObjective(id, desc, count, entry.targetId);
                case ObjectiveKind.ReachSkill:
                    return new ReachSkillObjective(id, desc, count, entry.targetId);
                default:
                    Debug.LogWarning($"[QuestManager] Unsupported ObjectiveKind '{entry.kind}' " +
                                     $"on quest '{questId}'. Add a case to BuildObjective.");
                    return null;
            }
        }

        /// <summary>
        /// The line the player will see for <paramref name="entry"/> once the quest is
        /// running — the authored override when there is one, the generated sentence
        /// otherwise.
        ///
        /// <para>Public so the OFFER card can preview the tasks before the quest exists.
        /// It deliberately reuses the same resolution the live objective uses rather than
        /// formatting its own: a preview that reads differently from the tracker is a
        /// second description of the same task, and the two would drift.</para>
        /// </summary>
        public static string DescribeObjective(ObjectiveEntry entry) =>
            string.IsNullOrEmpty(entry.description) ? AutoDescription(entry) : entry.description;

        private static string AutoDescription(ObjectiveEntry entry)
        {
            string what = entry.targetId;
            switch (entry.kind)
            {
                case ObjectiveKind.KillCount:
                    what = string.IsNullOrEmpty(what) ? "enemigos" : what;
                    return entry.count == 1 ? $"Derrota a 1 {what}" : $"Derrota a {entry.count} {what}";
                case ObjectiveKind.Collect:
                    return $"Consigue {entry.count} x {what}";
                case ObjectiveKind.Reach:
                    return $"Llega a {what}";
                case ObjectiveKind.Talk:
                    return $"Habla con {what}";
                case ObjectiveKind.Craft:
                    what = string.IsNullOrEmpty(what) ? "algo" : what;
                    return $"Elabora {entry.count} x {what}";
                case ObjectiveKind.CastSpell:
                    what = string.IsNullOrEmpty(what) ? "hechizos" : what;
                    return $"Lanza {what} {entry.count} veces";
                case ObjectiveKind.Survive:
                    return $"Resiste {entry.count} segundos";
                case ObjectiveKind.ReachLevel:
                    return $"Alcanza el nivel {entry.count}";
                case ObjectiveKind.EarnCoins:
                    return $"Reune {entry.count} monedas";
                case ObjectiveKind.FellTrees:
                    return entry.count == 1 ? "Tala 1 árbol" : $"Tala {entry.count} árboles";
                case ObjectiveKind.ReachSkill:
                    return $"Alcanza el {entry.count}% en {what}";
                default:
                    return "Objetivo";
            }
        }

        private void HandleCompletion(QuestDefinition def)
        {
            if (def == null) return;

            // Take the delivered goods BEFORE the entry leaves _active — the
            // objectives are what say which items were promised, and after the
            // removal they are unreachable.
            if (_active.TryGetValue(def.questId, out var entry))
                ConsumeDeliveredItems(entry.Objectives);

            _active.Remove(def.questId);
            _completed.Add(def.questId);

            // Rewards. XP and skill points fire as game events so existing
            // systems (Experience, PlayerProgression) pick them up.
            var player = EntityRegistry.PlayerTransform;
            if (player != null)
            {
                if (def.xpReward > 0)
                {
                    var xp = player.GetComponent<Experience>();
                    if (xp != null) xp.AddXp(def.xpReward);
                }
                if (def.skillPointReward > 0)
                {
                    var skills = player.GetComponent<LearnedSkills>();
                    if (skills != null) skills.AddPoints(def.skillPointReward);
                }
                if (def.arcanePointReward > 0)
                {
                    var spells = player.GetComponent<KnownSpells>();
                    if (spells != null) spells.AddPoints(def.arcanePointReward);
                }
                if (def.coinReward > 0)
                {
                    var wallet = player.GetComponent<CurrencyWallet>();
                    if (wallet != null) wallet.Add(def.coinReward);
                }
                if (def.itemRewards != null && def.itemRewards.Length > 0)
                {
                    var inv = Inventory ?? player.GetComponent<Inventory.Inventory>();
                    if (inv != null) GrantItemRewards(inv, def.itemRewards, def.itemRewardCounts);
                }
            }

            OnQuestCompleted?.Invoke(def.questId);

            // Quest completion is a sandbox-game milestone — never lose it
            // to a crash, even if the autosave timer was nowhere near firing.
            SaveService.Instance?.SaveImmediately($"quest '{def.questId}' completed");
        }

        /// <summary>
        /// Removes the items a Collect objective marked <c>consumeOnComplete</c>.
        /// This is what separates a delivery from a fetch-and-keep: without it the
        /// player hands the smith nothing and walks away with the ore AND the fee.
        ///
        /// <para>It removes the objective's TARGET amount, never the whole stack —
        /// a player who gathered thirty of something asked for twelve keeps the
        /// other eighteen.</para>
        /// </summary>
        private static void ConsumeDeliveredItems(List<IObjective> objectives)
        {
            if (objectives == null) return;
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            var inv = player.GetComponent<Inventory.Inventory>();
            if (inv == null) return;

            for (int i = 0; i < objectives.Count; i++)
            {
                if (!(objectives[i] is CollectItemObjective collect)) continue;
                if (!collect.ConsumeOnComplete) continue;

                var def = FindItemDefinition(collect.ItemId);
                if (def == null)
                {
                    Debug.LogWarning($"[QuestManager] Delivery item '{collect.ItemId}' not found " +
                                     "in any ItemDefinition asset — nothing taken.");
                    continue;
                }
                inv.RemoveItem(def, collect.Target);
            }
        }

        private static void GrantItemRewards(Inventory.Inventory inv, string[] itemIds, int[] counts)
        {
            for (int i = 0; i < itemIds.Length; i++)
            {
                string id = itemIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                var def = FindItemDefinition(id);
                if (def == null)
                {
                    Debug.LogWarning($"[QuestManager] Item reward '{id}' not found in any " +
                                     "ItemDefinition asset — skipping. Catalog may be out of sync.");
                    continue;
                }
                int qty = (counts != null && i < counts.Length && counts[i] > 0) ? counts[i] : 1;
                inv.AddItem(def, qty);
            }
        }

        /// <summary>
        /// The display name of a persona, or empty when nothing in the loaded set carries
        /// that id.
        ///
        /// <para>Resolved the same way <see cref="FindItemDefinition"/> resolves an item —
        /// a sweep over the loaded ScriptableObjects — rather than by holding a catalogue
        /// reference. It runs once per quest ACCEPTED, which is a handful of times a
        /// session, and it keeps this class free of a second asset dependency it would
        /// otherwise need only for one string.</para>
        /// </summary>
        private static string ResolvePersonaDisplayName(string personaId)
        {
            if (string.IsNullOrEmpty(personaId)) return string.Empty;

            var all = Resources.FindObjectsOfTypeAll<NPCPersonaDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!string.Equals(all[i].personaId, personaId, StringComparison.OrdinalIgnoreCase)) continue;
                return string.IsNullOrWhiteSpace(all[i].displayName) ? personaId : all[i].displayName;
            }

            // Falls back to the id rather than to nothing: a quest whose turn-in cannot be
            // resolved must still tell the player there IS one.
            return personaId;
        }

        /// <summary>
        /// ItemDefinition lookup by id. <c>Resources.FindObjectsOfTypeAll</c> catches
        /// both already-loaded SOs and the catalog. Slow per call, but it runs on
        /// quest completion only — a handful of times per session.
        /// </summary>
        private static ItemDefinition FindItemDefinition(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !string.IsNullOrEmpty(all[i].itemId) &&
                    all[i].itemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        private static QuestDefinition FindDef(IReadOnlyList<QuestDefinition> catalog, string id)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                var d = catalog[i];
                if (d != null && string.Equals(d.questId, id, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return null;
        }

        /// <summary>
        /// Seed one objective's counter from a save. Every shipped objective derives
        /// from <see cref="ObjectiveBase"/>, which exposes this as a first-class
        /// operation — this used to reach for the compiler-generated
        /// <c>&lt;Current&gt;k__BackingField</c> by reflection, which was one
        /// auto-property rewrite away from silently restoring nothing.
        /// </summary>
        private static void RestoreObjectiveProgress(IObjective obj, int targetCurrent)
        {
            if (targetCurrent <= 0) return;
            if (obj is SurviveObjective survive) { survive.RestoreElapsed(targetCurrent); return; }
            if (obj is ObjectiveBase ob) ob.RestoreProgress(targetCurrent);
        }
    }
}
