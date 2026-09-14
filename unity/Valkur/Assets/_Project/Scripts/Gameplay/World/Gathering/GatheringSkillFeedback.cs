using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What a skill gain looks like: a small number over the player's head, and a toast when
    /// something worth stopping for happens — a round milestone, or a new kind of goods becoming
    /// findable.
    ///
    /// <para><b>GAINS ARE COALESCED.</b> At 0 % a gain lands on roughly every third blow, which
    /// is twice a second of text if each one is announced. The number accumulates for
    /// <see cref="COALESCE_SECONDS"/> and is shown once, so "+0.3% Tala" reads as progress rather
    /// than as a ticker. Late in the climb gains are minutes apart and every one is shown on its
    /// own, which is exactly when a single tenth is worth seeing.</para>
    ///
    /// <para><b>A LOAD IS NOT A GAIN.</b> Only <see cref="PlayerGatheringSkills.SkillChanged"/>
    /// drives this, and the restore path does not raise it, so loading a 60 % woodcutter does not
    /// toast six milestones.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GatheringSkillFeedback : MonoBehaviour
    {
        private const float COALESCE_SECONDS = 1.1f;
        private static readonly Color GainColor = new Color(0.55f, 0.92f, 0.78f, 1f);

        private PlayerGatheringSkills _skills;
        private string _pendingKey;
        private int _pendingTenths;
        private int _pendingFrom = -1;
        private float _flushAt;

        /// <summary>Gains announced so far. A test seam.</summary>
        public int Announcements { get; private set; }

        private void Awake() => Bind(GetComponent<PlayerGatheringSkills>());

        public void Bind(PlayerGatheringSkills skills)
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

            // A different skill arriving mid-window flushes the first so the two never sum.
            if (_pendingKey != null && !string.Equals(_pendingKey, key, System.StringComparison.OrdinalIgnoreCase))
                Flush();

            if (_pendingKey == null)
            {
                _pendingKey = key;
                _pendingFrom = oldTenths;
                _flushAt = Time.time + COALESCE_SECONDS;
            }
            _pendingTenths = newTenths;
            enabled = true;

            AnnounceMilestones(key, oldTenths, newTenths);
        }

        private void Update()
        {
            if (_pendingKey == null) { enabled = false; return; }
            if (Time.time >= _flushAt) Flush();
        }

        private void Flush()
        {
            if (_pendingKey == null) return;

            var skill = GatheringSkillCatalog.Shared != null ? GatheringSkillCatalog.Shared.Find(_pendingKey) : null;
            string name = skill != null ? skill.displayName : _pendingKey;
            int delta = _pendingTenths - _pendingFrom;

            string text = "+" + GatheringSkillDefinition.ToPercent(delta).ToString("0.0",
                              System.Globalization.CultureInfo.InvariantCulture)
                          + "% " + name + " (" + GatheringSkillDefinition.FormatPercent(_pendingTenths) + ")";

            FloatingDamageSpawner.ShowAt(HeadPosition(), text, GainColor);
            Announcements++;

            _pendingKey = null;
            _pendingFrom = -1;
        }

        private void AnnounceMilestones(string key, int oldTenths, int newTenths)
        {
            var skill = GatheringSkillCatalog.Shared != null ? GatheringSkillCatalog.Shared.Find(key) : null;
            if (skill == null) return;

            float before = GatheringSkillDefinition.ToPercent(oldTenths);
            float after = GatheringSkillDefinition.ToPercent(newTenths);

            var unlocked = skill.yieldTable != null ? skill.yieldTable.TierUnlockedBetween(before, after) : null;
            if (unlocked != null)
            {
                string where = string.IsNullOrEmpty(unlocked.requiredTag) ? "" : " (en árboles especiales)";
                ToastSystem.Show($"{skill.displayName}: ya puedes encontrar {unlocked.displayName}{where}");
            }

            int step = Mathf.Max(1, skill.milestonePercent) * 10;
            if (newTenths / step > oldTenths / step)
            {
                ToastSystem.Show(newTenths >= GatheringSkillDefinition.MaxTenths
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
