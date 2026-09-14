using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Quests
{
    /// <summary>
    /// Misiones in the pre-game menus' language — the first authoring editor to try it after the
    /// launcher. The two panels wear the loading bar's housing, the tabs fill like the bar, a
    /// quest row is the selected menu row with a progress bar in it, and the detail's buttons
    /// are bevelled.
    ///
    /// <para><b>Particles answer what the author DID</b>: picking a quest, accepting it, moving
    /// an objective, completing it, dropping it. Each is queued and emitted on the NEXT frame,
    /// because every one of those actions rebuilds the list and the detail, and a row built this
    /// frame has no position until the layout pass runs — emitting at once throws the sparks
    /// from the panel's corner.</para>
    ///
    /// <para><b>Everything here is additive to the editor.</b> The actions, the tabs and the two
    /// panels are the same objects they were; deleting this file (and its calls) restores the
    /// tool look exactly, which is what makes it a trial.</para>
    /// </summary>
    public partial class QuestsRuntimeEditor
    {
        private const float ROW_H = 34f;
        private const float ROW_BAR_W = 70f;
        private const float OBJECTIVE_BAR_W = 64f;
        private const int MOTE_CAPACITY = 140;
        private const float MOTE_PAD = 40f;

        private readonly List<(FrontendFillGraphic fill, FrontendHoverGraphic groove, TextMeshProUGUI tmp)> _tabSkins =
            new List<(FrontendFillGraphic, FrontendHoverGraphic, TextMeshProUGUI)>();

        private MenuFxLayer _listMotes;
        private MenuFxLayer _detailMotes;
        private FrontendFillGraphic _selectedRowFill;
        private float _skinClock;

        private struct PendingBurst
        {
            public bool Detail;
            public string Target;
            public Color Colour;
            public FrontendMoteStyle Style;
            public int Count;
        }

        private readonly List<PendingBurst> _pendingBursts = new List<PendingBurst>();

        /// <summary>The list panel's particle layer. For the tests.</summary>
        internal MenuFxLayer ListMotes => _listMotes;

        private void SkinPanels()
        {
            var gold = EditorFrontendSkin.Gold;
            if (_listPanel != null)
            {
                EditorFrontendSkin.SkinDropPanel(_listPanel.gameObject, gold);
                _listMotes = EditorFrontendSkin.CreateMotes(_listPanel.transform, "QuestListMotes", MOTE_CAPACITY, MOTE_PAD);
            }
            if (_detailPanel != null)
            {
                EditorFrontendSkin.SkinDropPanel(_detailPanel.gameObject, gold);
                _detailMotes = EditorFrontendSkin.CreateMotes(_detailPanel.transform, "QuestDetailMotes", MOTE_CAPACITY, MOTE_PAD);
            }
        }

        /// <summary>The scroll view's flat slab would sit as a blue-grey rectangle inside the gold housing.</summary>
        private static void ClearScrollSlab(ScrollRect scroll)
        {
            var slab = scroll != null ? scroll.GetComponent<Image>() : null;
            if (slab != null) slab.color = Color.clear;
        }

        private void AddSkinnedTab(Transform parent, string label, Tab tab)
        {
            var btn = EditorFrontendSkin.BuildTab(parent, "Tab_" + tab, label, () => SetTab(tab),
                                                  out var fill, out var groove, out var tmp);
            _tabButtons.Add(btn);
            _tabSkins.Add((fill, groove, tmp));
        }

        private void PaintSkinnedTabs()
        {
            for (int i = 0; i < _tabSkins.Count; i++)
            {
                var (fill, groove, tmp) = _tabSkins[i];
                EditorFrontendSkin.PaintTab(fill, groove, tmp, (int)_tab == i);
            }
        }

        /// <summary>
        /// A quest row: the menu's selected row when chosen, its groove on hover, and — for a quest
        /// that is running — a small bar of its overall progress so "which one is stuck" is a
        /// glance down the list rather than a click on each.
        /// </summary>
        private void BuildQuestRow(Transform parent, string questId, string label, float progress, bool selected)
        {
            var go = EditorUIHelpers.CreateUI("QuestRow_" + questId, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleHeight = 0f;

            var hit = go.AddComponent<Image>();
            hit.color = Color.clear;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SelectQuest(questId));

            var groove = FrontendHoverGraphic.Create(go.transform, "Groove");
            groove.Tint = EditorFrontendSkin.Gold;
            groove.enabled = false;

            var fill = FrontendFillGraphic.Create(go.transform, "Fill");
            fill.Profile = FrontendFillProfile.Row;
            fill.Border = true;
            fill.StartCore = true;
            fill.Flow = selected;
            fill.Tint = EditorFrontendSkin.Gold;
            fill.enabled = selected;
            if (selected) _selectedRowFill = fill;

            var trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (!fill.enabled) groove.enabled = true; });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => groove.enabled = false);
            trigger.triggers.Add(exit);

            var textRt = EditorUIHelpers.CreateUI("Label", go.transform).GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = new Vector2(progress >= 0f ? -(ROW_BAR_W + 16f) : -8f, 0f);
            var tmp = textRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12f;
            tmp.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = selected ? EditorFrontendSkin.Ink : UITheme.TEXT_PRIMARY;
            tmp.raycastTarget = false;

            if (progress >= 0f) BuildMiniBar(go.transform, "Progress", progress, ROW_BAR_W, rightInset: 8f);
        }

        /// <summary>The loading bar at list size: a thin bevelled housing and a molten fill, anchored to the right.</summary>
        private static FrontendFillGraphic BuildMiniBar(Transform parent, string name, float amount, float width, float rightInset)
        {
            var rt = EditorUIHelpers.CreateUI(name, parent).GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-rightInset, 0f);
            rt.sizeDelta = new Vector2(width, 10f);
            var ignore = rt.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            var frame = BevelFrameGraphic.Create(rt, "Frame");
            frame.Thickness = 2f;
            frame.ShadowScale = 0f;
            frame.Brackets = false;
            frame.Tint = EditorFrontendSkin.Gold;

            var fill = FrontendFillGraphic.Create(rt, "Fill");
            fill.Profile = FrontendFillProfile.Bar;
            fill.Tint = amount >= 1f ? UITheme.SUCCESS : EditorFrontendSkin.Gold;
            fill.Amount = Mathf.Clamp01(amount);
            fill.LeadingCore = true;
            fill.rectTransform.offsetMin = new Vector2(2f, 2f);
            fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
            return fill;
        }

        /// <summary>An objective's progress, as a fixed-width bar inside its row's layout.</summary>
        private static void AddObjectiveBar(Transform row, float amount)
        {
            var holder = EditorUIHelpers.CreateUI("Bar", row);
            var le = holder.AddComponent<LayoutElement>();
            le.preferredWidth = OBJECTIVE_BAR_W;
            le.flexibleWidth = 0f;
            BuildMiniBar(holder.transform, "Progress", amount, OBJECTIVE_BAR_W, 0f);
        }

        private static void SkinStepButton(Button btn)
            => EditorFrontendSkin.SkinButton(btn, UITheme.BTN_NORMAL, UITheme.BTN_HOVER, 2f);

        /// <summary>Queue a burst for the next frame, on a row (list) or a named object (detail).</summary>
        private void QueueBurst(bool detail, string target, Color colour, FrontendMoteStyle style, int count)
            => _pendingBursts.Add(new PendingBurst { Detail = detail, Target = target, Colour = colour, Style = style, Count = count });

        private void TickSkin(float dt)
        {
            _skinClock += dt;
            if (_selectedRowFill != null) _selectedRowFill.Clock = _skinClock;
            FlushBursts();
            _listMotes?.Tick(Mathf.Min(dt, 0.1f));
            _detailMotes?.Tick(Mathf.Min(dt, 0.1f));
        }

        private void FlushBursts()
        {
            if (_pendingBursts.Count == 0) return;
            for (int i = 0; i < _pendingBursts.Count; i++)
            {
                var b = _pendingBursts[i];
                var content = b.Detail ? _detailScrollContent : _listScrollContent;
                var layer = b.Detail ? _detailMotes : _listMotes;
                if (content == null || layer == null) continue;
                var target = string.IsNullOrEmpty(b.Target) ? null : FindDeep(content, b.Target) as RectTransform;
                if (target == null) target = content;
                var r = target.rect;
                FrontendIconMotes.Burst(layer, target, r.center, Mathf.Min(r.width, 160f) * 0.45f, b.Colour, b.Style, b.Count);
            }
            _pendingBursts.Clear();
        }

        private void ClearSkinMotes()
        {
            _pendingBursts.Clear();
            _listMotes?.Clear();
            _detailMotes?.Clear();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void Update()
        {
            if (_active) TickSkin(Time.unscaledDeltaTime);
        }
    }
}
