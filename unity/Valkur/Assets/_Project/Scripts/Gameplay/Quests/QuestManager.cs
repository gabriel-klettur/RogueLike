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
    /// (XP, skill points, items) on completion.
    ///
    /// Design choices:
    ///   - <see cref="StartQuest"/> is idempotent: starting an already-
    ///     active or already-completed quest is a silent no-op (saves
    ///     UI from having to track button-disabled state).
    ///   - Active quests own their <see cref="Quest"/> + objective
    ///     instances. Completed quests are stored as ids only — the
    ///     objective objects can GC.
    ///   - Save/load: <see cref="ToSnapshot"/> persists active quest ids
    ///     and progress per objective. <see cref="FromSnapshot"/> rebuilds
    ///     IObjective instances; objectives that no longer exist on the
    ///     QuestDefinition (designer pruned) are silently dropped.
    ///
    /// Item rewards rely on the player having an Inventory component;
    /// the manager looks one up on the levelled entity each time. NPCs
    /// completing quests (rare but possible for companion AI) without
    /// inventories silently skip item rewards.
    /// </summary>
    public sealed class QuestManager : MonoBehaviour
    {
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

        public IReadOnlyCollection<string> ActiveIds    => _active.Keys;
        public IReadOnlyCollection<string> CompletedIds => _completed;

        /// <summary>Fires (questId) when a quest is started.</summary>
        public event Action<string> OnQuestStarted;
        /// <summary>Fires (questId) when a quest completes.</summary>
        public event Action<string> OnQuestCompleted;

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

        public void AbandonQuest(string questId)
        {
            if (questId == null || !_active.TryGetValue(questId, out var entry)) return;
            entry.Quest.End();
            _active.Remove(questId);
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
                        // Replay each objective's Current by re-firing the
                        // backing event source. KillCountObjective listens to
                        // GameEvents.OnEntityDied — we can't replay actual
                        // deaths, but we CAN reflect Current via reflection
                        // since that's the persistence path.
                        for (int j = 0; j < entry.Objectives.Count && j < counters.Length; j++)
                            ForceObjectiveProgress(entry.Objectives[j], counters[j]);
                    }
                }
            }
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private List<IObjective> BuildObjectives(QuestDefinition def)
        {
            var list = new List<IObjective>(def.objectives?.Length ?? 0);
            if (def.objectives == null) return list;
            for (int i = 0; i < def.objectives.Length; i++)
            {
                var entry = def.objectives[i];
                IObjective obj = BuildObjective(def.questId, i, entry);
                if (obj != null) list.Add(obj);
            }
            return list;
        }

        private static IObjective BuildObjective(string questId, int index, ObjectiveEntry entry)
        {
            string id = $"{questId}.obj{index}";
            string desc = string.IsNullOrEmpty(entry.description)
                ? AutoDescription(entry) : entry.description;

            switch (entry.kind)
            {
                case ObjectiveKind.KillCount:
                    return new KillCountObjective(id, desc, entry.count, entry.targetId);
                default:
                    Debug.LogWarning($"[QuestManager] Unsupported ObjectiveKind '{entry.kind}' " +
                                     $"on quest '{questId}'. Add a case to BuildObjective.");
                    return null;
            }
        }

        private static string AutoDescription(ObjectiveEntry entry)
        {
            switch (entry.kind)
            {
                case ObjectiveKind.KillCount:
                    string what = string.IsNullOrEmpty(entry.targetId) ? "enemies" : entry.targetId;
                    return entry.count == 1 ? $"Kill 1 {what}" : $"Kill {entry.count} {what}";
                default:
                    return "Objective";
            }
        }

        private void HandleCompletion(QuestDefinition def)
        {
            if (def == null) return;
            _active.Remove(def.questId);
            _completed.Add(def.questId);

            // Rewards. XP and skill points fire as game events so existing
            // systems (Experience, LevelUpSkillPointSystem) pick them up.
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
                if (def.itemRewards != null && def.itemRewards.Length > 0)
                {
                    var inv = Inventory ?? player.GetComponent<Inventory.Inventory>();
                    if (inv != null) GrantItemRewards(inv, def.itemRewards);
                }
            }

            OnQuestCompleted?.Invoke(def.questId);

            // Quest completion is a sandbox-game milestone — never lose it
            // to a crash, even if the autosave timer was nowhere near firing.
            SaveService.Instance?.SaveImmediately($"quest '{def.questId}' completed");
        }

        private static void GrantItemRewards(Inventory.Inventory inv, string[] itemIds)
        {
            // ItemDefinition lookup: Resources.FindObjectsOfTypeAll catches
            // both already-loaded SOs and the catalog. Slow per-call but
            // quest completion is a rare event.
            var allDefs = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            foreach (var id in itemIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                ItemDefinition def = null;
                foreach (var d in allDefs)
                    if (d.itemId.Equals(id, StringComparison.OrdinalIgnoreCase))
                    { def = d; break; }
                if (def == null)
                {
                    Debug.LogWarning($"[QuestManager] Item reward '{id}' not found in any " +
                                     "ItemDefinition asset — skipping. Catalog may be out of sync.");
                    continue;
                }
                inv.AddItem(def, 1);
            }
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

        // KillCountObjective.Current is { get; private set; }. The save
        // path is the only legitimate caller that needs to seed it
        // post-construction; reach in via reflection rather than expose
        // a setter that gameplay code could misuse.
        private static void ForceObjectiveProgress(IObjective obj, int targetCurrent)
        {
            if (obj == null || targetCurrent <= 0) return;
            var prop = obj.GetType().GetProperty("Current",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
            {
                // KillCountObjective.Current uses { get; private set; } — locate
                // the backing field instead.
                var field = obj.GetType().GetField("<Current>k__BackingField",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) field.SetValue(obj, targetCurrent);
                return;
            }
            prop.SetValue(obj, targetCurrent);
        }
    }
}
