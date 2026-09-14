using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Skills
{
    /// <summary>
    /// What a skill gain looks like: a small number over the player's head, and a toast when
    /// something worth stopping for happens — a round milestone, or a new kind of goods becoming
    /// findable.
    ///
    /// <para><b>GAINS ARE COALESCED, PER KEY.</b> At 0 % a woodcutting gain lands on roughly
    /// every third blow, which is twice a second of text if each one is announced. The number
    /// accumulates for <see cref="COALESCE_SECONDS"/> and is shown once, so "+0.3% Tala" reads
    /// as progress rather than as a ticker. Late in the climb gains are minutes apart and every
    /// one is shown on its own, which is exactly when a single tenth is worth seeing.</para>
    ///
    /// <para>A second skill gaining mid-window used to flush the first one early, so felling a
    /// tree while <c>athletics</c> gains land from running would split the woodcutting toast in
    /// two. Each key now has its own pending entry and its own deadline, so two skills raising
    /// at once never sum or interrupt each other. <c>athletics</c> gets a longer window
    /// (<see cref="PHYSICAL_COALESCE_SECONDS"/>): running rolls a gain every few metres, far more
    /// often than a blow lands, so the ordinary window would still read as a ticker.</para>
    ///
    /// <para><b>A LOAD IS NOT A GAIN.</b> Only <see cref="PlayerSkills.SkillChanged"/>
    /// drives this, and the restore path does not raise it, so loading a 60 % woodcutter does not
    /// toast six milestones.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillFeedback : MonoBehaviour
    {
        private const float COALESCE_SECONDS = 1.1f;
        private const float PHYSICAL_COALESCE_SECONDS = 3f;
        private static readonly Color GainColor = new Color(0.55f, 0.92f, 0.78f, 1f);

        /// <summary>One skill's accumulating gain, waiting for its own flush.</summary>
        private sealed class Pending
        {
            public string Key;
            public int FromTenths;
            public int ToTenths;
            public float FlushAt;
        }

        private PlayerSkills _skills;

        // Bounded by how many distinct skills can gain inside one coalesce window — a handful at
        // most — so scanning it on every gain and every tick costs nothing.
        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Gains announced so far. A test seam.</summary>
        public int Announcements { get; private set; }

        private void Awake() => Bind(GetComponent<PlayerSkills>());

        public void Bind(PlayerSkills skills)
        {
            if (_skills == skills) return;
            if (_skills != null) _skills.SkillChanged -= OnSkillChanged;
            _skills = skills;
            if (_skills != null) _skills.SkillChanged += OnSkillChanged;
        }

        private void OnDestroy()
        {
            if (_skills != null) _skills.SkillChanged -= OnSkillChanged;
        }

        private void OnSkillChanged(string key, int oldTenths, int newTenths)
        {
            if (newTenths <= oldTenths) return;

            var entry = FindPending(key);
            if (entry == null)
            {
                entry = new Pending { Key = key, FromTenths = oldTenths, FlushAt = Time.time + CoalesceWindow(key) };
                _pending.Add(entry);
            }
            entry.ToTenths = newTenths;
            enabled = true;

            AnnounceMilestones(key, oldTenths, newTenths);
        }

        private Pending FindPending(string key)
        {
            for (int i = 0; i < _pending.Count; i++)
                if (string.Equals(_pending[i].Key, key, System.StringComparison.OrdinalIgnoreCase))
                    return _pending[i];
            return null;
        }

        private static float CoalesceWindow(string key)
        {
            var skill = SkillCatalog.Shared != null ? SkillCatalog.Shared.Find(key) : null;
            return skill != null && skill.category == SkillCategory.Physical
                ? PHYSICAL_COALESCE_SECONDS
                : COALESCE_SECONDS;
        }

        private void Update()
        {
            if (_pending.Count == 0) { enabled = false; return; }

            float now = Time.time;
            // Walked backwards so flushing (which removes) never skips the next entry to check.
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (now >= _pending[i].FlushAt) Flush(i);
        }

        private void Flush(int index)
        {
            var entry = _pending[index];
            _pending.RemoveAt(index);

            var skill = SkillCatalog.Shared != null ? SkillCatalog.Shared.Find(entry.Key) : null;
            string name = skill != null ? skill.displayName : entry.Key;
            int delta = entry.ToTenths - entry.FromTenths;

            string text = "+" + SkillDefinition.ToPercent(delta).ToString("0.0",
                              System.Globalization.CultureInfo.InvariantCulture)
                          + "% " + name + " (" + SkillDefinition.FormatPercent(entry.ToTenths) + ")";

            FloatingDamageSpawner.ShowAt(HeadPosition(), text, GainColor);
            Announcements++;
        }

        private void AnnounceMilestones(string key, int oldTenths, int newTenths)
        {
            var skill = SkillCatalog.Shared != null ? SkillCatalog.Shared.Find(key) : null;
            if (skill == null) return;

            float before = SkillDefinition.ToPercent(oldTenths);
            float after = SkillDefinition.ToPercent(newTenths);

            var unlocked = skill.yieldTable != null ? skill.yieldTable.TierUnlockedBetween(before, after) : null;
            if (unlocked != null)
            {
                string where = string.IsNullOrEmpty(unlocked.requiredTag) ? "" : " (en árboles especiales)";
                ToastSystem.Show($"{skill.displayName}: ya puedes encontrar {unlocked.displayName}{where}");
            }

            int step = Mathf.Max(1, skill.milestonePercent) * 10;
            if (newTenths / step > oldTenths / step)
            {
                ToastSystem.Show(newTenths >= SkillDefinition.MaxTenths
                    ? $"{skill.displayName}: maestría al 100%"
                    : $"{skill.displayName} alcanza el {newTenths / 10}%");
            }
        }

        private Vector3 HeadPosition()
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            float top = sr != null && sr.sprite != null ? sr.bounds.max.y : transform.position.y + 1.8f;

            // One line ABOVE where the yield line sits (HarvestFeedback puts it at top + 0.2): a
            // gain and a log arrive on the same blow all the time, and at the same point the two
            // strings printed over each other — measured on a live capture.
            return new Vector3(transform.position.x, top + 0.75f, 0f);
        }
    }
}
