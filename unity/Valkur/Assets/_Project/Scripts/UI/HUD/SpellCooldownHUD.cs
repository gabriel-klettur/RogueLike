using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Stacked countdown labels above the XP bar — one per active spell cooldown.
    ///
    /// Subscribes to <see cref="GameEvents.OnSpellCast"/> filtered to the bound
    /// player. On each cast a row is added (or its remaining time reset, in
    /// the unlikely event the same key is re-cast mid-cooldown). Each row owns
    /// a private countdown that decrements every frame and self-removes when
    /// it hits zero, so the panel grows and shrinks naturally as the player
    /// chains spells.
    ///
    /// Visual contract (per user request):
    ///   • White text, anchored bottom-center directly above the XP bar
    ///     (which itself sits at <c>anchoredPosition.y = 14</c> with height 28).
    ///   • Rows stack vertically — newest at the top — driven by a
    ///     <see cref="VerticalLayoutGroup"/> + <see cref="ContentSizeFitter"/>.
    ///   • Format: "<displayName>  <remaining:F1>s"
    ///
    /// The HUD is built and initialized by
    /// <c>HUDManager.CreateSpellCooldownHUD</c>; this component is purely
    /// the runtime driver — the panel structure (Canvas parent, layout group)
    /// is supplied by the bootstrap.
    /// </summary>
    public class SpellCooldownHUD : MonoBehaviour
    {
        // Visual constants — kept here (not on the panel builder) so the row
        // factory is self-contained and unit-testable.
        private const float RowFontSize    = 16f;
        private const float RowHeight      = 22f;
        private const float MinDisplayTime = 0.05f; // hide rows whose CD ≤ this

        private GameObject _player;
        private RectTransform _stackRoot;

        // Active rows keyed by spell key so a re-cast (rare — usually CD blocks
        // it) reuses the same UI element instead of stacking duplicates.
        private readonly Dictionary<string, CooldownRow> _rows
            = new Dictionary<string, CooldownRow>();

        // Buffer reused each frame to avoid allocating a List during the tick.
        private readonly List<string> _expiredScratch = new List<string>();

        /// <summary>
        /// Wire this HUD instance to the player whose casts should produce rows.
        /// Stack root is the Transform under which row GameObjects are
        /// instantiated — typically a panel with VerticalLayoutGroup +
        /// ContentSizeFitter prepared by <c>HUDManager</c>.
        /// </summary>
        public void Initialize(GameObject player, RectTransform stackRoot)
        {
            _player    = player;
            _stackRoot = stackRoot;
            GameEvents.OnSpellCast += OnSpellCast;
        }

        private void OnDestroy()
        {
            GameEvents.OnSpellCast -= OnSpellCast;
        }

        private void OnSpellCast(GameObject caster, string spellKey, string displayName, float cooldownDuration)
        {
            if (caster == null || caster != _player) return;
            if (cooldownDuration <= MinDisplayTime) return;
            if (string.IsNullOrEmpty(spellKey)) return;

            if (_rows.TryGetValue(spellKey, out var existing) && existing != null)
            {
                existing.Reset(displayName, cooldownDuration);
                return;
            }

            var row = CooldownRow.Create(_stackRoot, displayName, cooldownDuration, RowFontSize, RowHeight);
            _rows[spellKey] = row;
        }

        private void Update()
        {
            if (_rows.Count == 0) return;

            float dt = Time.deltaTime;
            _expiredScratch.Clear();

            foreach (var kv in _rows)
            {
                var row = kv.Value;
                if (row == null) { _expiredScratch.Add(kv.Key); continue; }
                row.Tick(dt);
                if (row.Remaining <= 0f) _expiredScratch.Add(kv.Key);
            }

            for (int i = 0; i < _expiredScratch.Count; i++)
            {
                var key = _expiredScratch[i];
                if (_rows.TryGetValue(key, out var row) && row != null)
                    row.Destroy();
                _rows.Remove(key);
            }
        }

        /// <summary>
        /// One label inside the stack. Encapsulates its own GameObject + TMP +
        /// remaining-time state so <see cref="SpellCooldownHUD"/> can treat
        /// rows as opaque value-like objects.
        /// </summary>
        private class CooldownRow
        {
            private readonly GameObject _go;
            private readonly TextMeshProUGUI _label;
            private string _displayName;

            public float Remaining { get; private set; }

            private CooldownRow(GameObject go, TextMeshProUGUI label, string displayName, float total)
            {
                _go         = go;
                _label      = label;
                _displayName = displayName;
                Remaining    = total;
                Refresh();
            }

            public static CooldownRow Create(RectTransform parent, string displayName, float total, float fontSize, float height)
            {
                var go = new GameObject($"CD_{displayName}", typeof(RectTransform));
                go.transform.SetParent(parent, false);

                var label = go.AddComponent<TextMeshProUGUI>();
                label.fontSize = fontSize;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;

                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = height;

                return new CooldownRow(go, label, displayName, total);
            }

            public void Reset(string displayName, float total)
            {
                _displayName = displayName;
                Remaining    = total;
                Refresh();
            }

            public void Tick(float dt)
            {
                Remaining -= dt;
                if (Remaining < 0f) Remaining = 0f;
                Refresh();
            }

            public void Destroy()
            {
                if (_go != null) Object.Destroy(_go);
            }

            private void Refresh()
            {
                if (_label == null) return;
                _label.text = $"{_displayName}  {Remaining:0.0}s";
            }
        }
    }
}
