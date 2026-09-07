using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// The rows that are specific to a spawner's form — an enum choice, a roster entry, the
    /// reapply pair.
    ///
    /// <para>They live here rather than in <c>EntitiesEditorUIBuilder</c> because they are
    /// this editor's vocabulary, and the shared builder already carries the generic three
    /// (<c>AddPropertyRow</c>, <c>AddEditableRow</c>, <c>AddToggleRow</c>) which these reuse
    /// for their labels and metrics so the two panels stay visually identical.</para>
    /// </summary>
    public static partial class SpawnerEditorUIBuilder
    {
        private const float ROW_LABEL_W = 110f;
        private const float ROW_H       = 20f;

        /// <summary>
        /// A labelled dropdown over a fixed set of choices — the widget the four spawner enums
        /// (Trigger, Mode, Advance On, Spawn Shape) needed and never had. They shipped as
        /// read-only labels, so four of the decisions that most define a spawner were
        /// displayed and unreachable, and the only way to get a different one was a new asset.
        /// </summary>
        public static TMP_Dropdown AddChoiceRow(RectTransform section, string label,
                                                IReadOnlyList<string> options, int selectedIndex,
                                                Action<string> onChanged)
        {
            var row = MakeRow(section, $"Row_{label}", ROW_H);
            AddLabel(row.transform, label, ROW_LABEL_W);

            var host = MakeChild("DropdownHost", row.transform);
            host.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var dd = UIDropdown.Add(host.transform, options, selectedIndex, 10f);
            dd.onValueChanged.AddListener(i =>
            {
                if (options != null && i >= 0 && i < options.Count) onChanged?.Invoke(options[i]);
            });
            return dd;
        }

        /// <summary>
        /// One wave entry: which entity, how many, how far they scatter, and a remove button.
        ///
        /// <para>The entity is a DROPDOWN over the shipped monster catalogue, never a text
        /// field. <c>WaveSpawnEntry.entityId</c> is a string that nothing validated, so a typo
        /// cost one console warning at spawn time and a camp that never filled — invisible
        /// until somebody walked to that clearing. A picker makes the illegal value
        /// unrepresentable, which is the only version of the fix that also protects the author
        /// who never reads the console.</para>
        ///
        /// <para>A <c>kind: "building"</c> entry keeps its numeric id and is shown read-only:
        /// a building template id is not in the monster catalogue, and silently offering the
        /// wrong list would let one click turn a fish shoal into a monster.</para>
        /// </summary>
        public static void AddRosterRow(RectTransform section, string waveLabel,
                                        IReadOnlyList<string> monsterKeys, WaveSpawnEntry entry,
                                        Action<string> onEntityChanged,
                                        Action<string> onCountChanged,
                                        Action<string> onSpreadChanged,
                                        Action onRemove)
        {
            var row = MakeRow(section, $"Roster_{entry.entityId}", ROW_H);
            AddLabel(row.transform, waveLabel, 22f);

            bool isBuilding = string.Equals(entry.kind, "building", StringComparison.OrdinalIgnoreCase);
            if (isBuilding)
            {
                var idGo = MakeChild("BuildingId", row.transform);
                idGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var tmp = idGo.AddComponent<TextMeshProUGUI>();
                tmp.text      = $"building #{entry.entityId}";
                tmp.fontSize  = 10f;
                tmp.color     = UITheme.TEXT_PRIMARY;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
            }
            else
            {
                var host = MakeChild("EntityHost", row.transform);
                host.AddComponent<LayoutElement>().flexibleWidth = 1f;

                int index = -1;
                if (monsterKeys != null)
                {
                    for (int i = 0; i < monsterKeys.Count; i++)
                    {
                        if (string.Equals(monsterKeys[i], entry.entityId, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                var dd = UIDropdown.Add(host.transform, monsterKeys, index, 10f);
                dd.onValueChanged.AddListener(i =>
                {
                    if (monsterKeys != null && i >= 0 && i < monsterKeys.Count)
                        onEntityChanged?.Invoke(monsterKeys[i]);
                });
            }

            AddCompactInput(row.transform, entry.count.ToString(), 34f, onCountChanged,
                            TMP_InputField.ContentType.IntegerNumber);
            AddCompactInput(row.transform,
                            entry.spreadRadius.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                            38f, onSpreadChanged, TMP_InputField.ContentType.DecimalNumber);

            var rm   = EditorUIHelpers.AddActionBtn(row.transform, "x", 18f, onRemove, out _);
            var rmLe = rm.GetComponent<LayoutElement>();
            if (rmLe != null) rmLe.preferredWidth = 20f;
        }

        /// <summary>The "add one more to this camp" row. Only built when the catalogue resolved.</summary>
        public static void AddRosterAddRow(RectTransform section, IReadOnlyList<string> monsterKeys,
                                           Action<string> onAdd)
        {
            var row = MakeRow(section, "Roster_Add", ROW_H);
            AddLabel(row.transform, "Add", 22f);

            var host = MakeChild("DropdownHost", row.transform);
            host.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var dd = UIDropdown.Add(host.transform, monsterKeys, 0, 10f);

            string selected = monsterKeys != null && monsterKeys.Count > 0 ? monsterKeys[0] : null;
            dd.onValueChanged.AddListener(i =>
            {
                if (monsterKeys != null && i >= 0 && i < monsterKeys.Count) selected = monsterKeys[i];
            });

            var add   = EditorUIHelpers.AddActionBtn(row.transform, "+", 18f, () => onAdd?.Invoke(selected), out _);
            var addLe = add.GetComponent<LayoutElement>();
            if (addLe != null) addLe.preferredWidth = 20f;
        }

        /// <summary>
        /// The two ways back to the preset. Copy-on-place makes a global retune impossible by
        /// accident; without these it would be impossible on purpose, which is a different
        /// mistake. The Particles editor offers exactly this pair for the same reason.
        /// </summary>
        public static void AddReapplyRows(RectTransform section, Action onReapplyThis,
                                          Action onReapplyAll)
        {
            var row = MakeRow(section, "Row_Reapply", ROW_H);
            AddLabel(row.transform, "Reapply preset", ROW_LABEL_W);

            var thisImg = EditorUIHelpers.AddActionBtn(row.transform, "This", 18f, onReapplyThis, out _);
            var thisLe  = thisImg.GetComponent<LayoutElement>();
            if (thisLe != null) thisLe.preferredWidth = 44f;

            var allImg = EditorUIHelpers.AddActionBtn(row.transform, "All", 18f, onReapplyAll, out _);
            var allLe  = allImg.GetComponent<LayoutElement>();
            if (allLe != null) allLe.preferredWidth = 40f;
        }

        // ── Row plumbing ────────────────────────────────────────────────────────

        private static GameObject MakeRow(RectTransform section, string name, float height)
        {
            var go = MakeChild(name, section);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            return go;
        }

        private static GameObject MakeChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void AddLabel(Transform parent, string text, float width)
        {
            var go = MakeChild("Label", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 10f;
            tmp.color     = UITheme.TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Truncate;
        }

        /// <summary>
        /// A fixed-width committed input. Declares its width explicitly because a button or a
        /// field in a HorizontalLayoutGroup with childControlWidth and no forced expand is
        /// laid out at its MINIMUM otherwise — which is how the Controls editor once shipped
        /// two buttons collapsed into a single overprinted character column.
        /// </summary>
        private static void AddCompactInput(Transform parent, string value, float width,
                                            Action<string> onCommit,
                                            TMP_InputField.ContentType contentType)
        {
            var input = UIInputField.AddCommit(parent, value ?? string.Empty, onCommit, 18f, 10f);
            input.contentType = contentType;
            var le = input.GetComponent<LayoutElement>();
            if (le == null) le = input.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth  = 0f;
        }
    }
}
