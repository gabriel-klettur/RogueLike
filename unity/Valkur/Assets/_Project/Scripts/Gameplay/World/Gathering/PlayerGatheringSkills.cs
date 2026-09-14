using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The player's gathering skills — woodcutting and whatever follows — as 0.0 % .. 100.0 %.
    ///
    /// <para><b>ONE COMPONENT, EVERY SKILL, KEYED BY STRING.</b> The same contract
    /// <c>PlayerProfessions</c> uses and for the same reason: a save names a skill by a stable
    /// key, never by an asset reference an importer can regenerate.</para>
    ///
    /// <para><b>GET-OR-ADD, ON THE PLAYER ONLY.</b> <see cref="For"/> attaches the component the
    /// first time a player works a node, so no bootstrap step has to remember it, and it refuses
    /// anything not tagged <c>Player</c>: a monster that clips a tree with a slash must not grow a
    /// woodcutting skill, and a component that appeared on every creature would be a save-shaped
    /// leak nobody reads.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGatheringSkills : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public string key;
            public int tenths;
        }

        [SerializeField]
        [Tooltip("Serialized for Inspector visibility while debugging. Written through Gain / Set.")]
        private List<Entry> _entries = new List<Entry>();

        /// <summary>A skill moved. Args: (key, oldTenths, newTenths).</summary>
        public event Action<string, int, int> SkillChanged;

        /// <summary>The RNG gains roll against. Replaceable so a test can pin a sequence.</summary>
        public System.Random Rng { get; set; } = new System.Random();

        public int Count => _entries.Count;

        /// <summary>
        /// The skills component of the player this object belongs to, created on first use.
        /// Null for anything that is not the player.
        /// </summary>
        public static PlayerGatheringSkills For(GameObject worker)
        {
            if (worker == null) return null;

            var existing = worker.GetComponentInParent<PlayerGatheringSkills>();
            if (existing != null) return existing;

            var root = worker.transform.root.gameObject;
            if (!root.CompareTag("Player") && !worker.CompareTag("Player")) return null;

            var host = worker.CompareTag("Player") ? worker : root;
            var skills = host.AddComponent<PlayerGatheringSkills>();
            host.AddComponent<GatheringSkillFeedback>();
            return skills;
        }

        /// <summary>Read without creating. Null when this worker has never gathered.</summary>
        public static PlayerGatheringSkills Peek(GameObject worker) =>
            worker != null ? worker.GetComponentInParent<PlayerGatheringSkills>() : null;

        private int IndexOf(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].key, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        public int GetTenths(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            int i = IndexOf(key);
            return i < 0 ? 0 : Mathf.Clamp(_entries[i].tenths, 0, GatheringSkillDefinition.MaxTenths);
        }

        public float GetPercent(string key) => GatheringSkillDefinition.ToPercent(GetTenths(key));

        /// <summary>Set a skill outright. The console and the save layer; clamped.</summary>
        public void SetTenths(string key, int tenths)
        {
            if (string.IsNullOrEmpty(key)) return;
            tenths = Mathf.Clamp(tenths, 0, GatheringSkillDefinition.MaxTenths);

            int i = IndexOf(key);
            int old = i < 0 ? 0 : _entries[i].tenths;
            if (i < 0) _entries.Add(new Entry { key = key, tenths = tenths });
            else _entries[i] = new Entry { key = _entries[i].key, tenths = tenths };

            if (old != tenths) Raise(key, old, tenths);
        }

        /// <summary>
        /// Roll for a gain after one productive blow. Returns true when the skill moved.
        ///
        /// <para>The roll is the skill definition's pure chance against THIS node's difficulty,
        /// which is what sends a player to harder trees to keep climbing.</para>
        /// </summary>
        public bool TryGain(GatheringSkillDefinition skill, int difficulty, bool wrongTool)
        {
            if (skill == null || string.IsNullOrEmpty(skill.skillKey)) return false;

            int current = GetTenths(skill.skillKey);
            float chance = skill.GainChance(current, difficulty, wrongTool);
            if (chance <= 0f || Rng.NextDouble() >= chance) return false;

            SetTenths(skill.skillKey, current + Mathf.Max(1, skill.gainTenths));
            return true;
        }

        private void Raise(string key, int oldTenths, int newTenths)
        {
            SkillChanged?.Invoke(key, oldTenths, newTenths);
            GameEvents.FireGatheringSkillChanged(gameObject, key, newTenths);
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        public void WriteTo(ProgressionSaveData data)
        {
            if (data == null) return;
            data.gatheringSkillKeys = new List<string>(_entries.Count);
            data.gatheringSkillTenths = new List<int>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                data.gatheringSkillKeys.Add(_entries[i].key);
                data.gatheringSkillTenths.Add(_entries[i].tenths);
            }
        }

        /// <summary>
        /// Restore silently — no SkillChanged, so loading a 60 % woodcutter does not toast six
        /// milestones. Reads only as far as the shorter list, the PlayerProfessions rule.
        /// </summary>
        public void ReadFrom(ProgressionSaveData data)
        {
            _entries.Clear();
            if (data?.gatheringSkillKeys == null || data.gatheringSkillTenths == null) return;

            int count = Mathf.Min(data.gatheringSkillKeys.Count, data.gatheringSkillTenths.Count);
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(data.gatheringSkillKeys[i])) continue;
                _entries.Add(new Entry
                {
                    key = data.gatheringSkillKeys[i],
                    tenths = Mathf.Clamp(data.gatheringSkillTenths[i], 0, GatheringSkillDefinition.MaxTenths),
                });
            }
        }
    }
}
