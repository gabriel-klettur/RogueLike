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
    ///   • White text on a semi-transparent dark background for legibility,
    ///     anchored top-left just below the DayNightClockHUD.
    ///   • Rows stack vertically — newest at the bottom — driven by a
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
        private const float RowHeight      = 24f;
        private const float RowPaddingX    = 8f;   // left/right inset for the TMP child
        private const float MinDisplayTime = 0.05f; // hide rows whose CD ≤ this

        // Semi-transparent dark background per row so the text stays readable
        // against any world tint (matches the DayNightClockHUD bottom panel).
        private static readonly Color RowBgColor = new Color(0.04f, 0.05f, 0.08f, 0.65f);

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
            // Tracks the last `Remaining` value that was actually written to
            // the label, quantised to the same 0.1-s precision the display
            // uses. Skipping the string-build + TMP rebuild on frames where
            // the visible text would not change saves ~5x per cooldown row
            // (60 Hz Update → ~10 Hz actual text refresh) and eliminates a
            // recurring string allocation while spells are on cooldown.
            // Catalog row: VendorShopUI ("conditional text rebuild").
            private int _lastDisplayedTenths = int.MinValue;

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
                // Row root: holds the semi-transparent background + LayoutElement.
                // TMP lives on a *child* GameObject because Image+TMP on the same
                // GameObject triggers an NRE in TMP's MaskableGraphic init path.
                var go = new GameObject($"CD_{displayName}", typeof(RectTransform));
                go.transform.SetParent(parent, false);

                var bg = go.AddComponent<Image>();
                bg.color = RowBgColor;
                bg.raycastTarget = false;

                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = height;

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(go.transform, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = new Vector2(RowPaddingX, 0f);
                labelRt.offsetMax = new Vector2(-RowPaddingX, 0f);

                var label = labelGo.AddComponent<TextMeshProUGUI>();
                label.fontSize = fontSize;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;

                return new CooldownRow(go, label, displayName, total);
            }

            public void Reset(string displayName, float total)
            {
                _displayName = displayName;
                Remaining    = total;
                // Force the next Refresh() to actually write the label, even if
                // the new `total` quantises to the same tenths as the previous
                // value — the displayName may have changed too.
                _lastDisplayedTenths = int.MinValue;
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
                // Quantise to tenths of a second — the format string ("0.0") only
                // surfaces that precision, so any frame where the tenth-of-a-second
                // bucket hasn't changed would produce identical text. Skipping the
                // string interpolation + .text setter on those frames eliminates
                // the per-frame GC alloc and the TMP mesh rebuild.
                int tenths = Mathf.Max(0, Mathf.RoundToInt(Remaining * 10f));
                if (tenths == _lastDisplayedTenths) return;
                _lastDisplayedTenths = tenths;
                _label.text = $"{_displayName}  {Remaining:0.0}s";
            }
        }
    }
}
