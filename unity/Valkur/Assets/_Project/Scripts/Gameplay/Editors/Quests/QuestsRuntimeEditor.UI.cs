using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Quests;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Quests
{
    /// <summary>
    /// Two panels: the list of quests in the current tab on the left, and everything about the
    /// selected one on the right.
    ///
    /// <para><b>The list is rebuilt on every refresh and the detail with it.</b> That is the
    /// shape the Items editor's 3.5 s freeze warns against — but the scale decides: the whole
    /// catalogue is ten rows, against 180 items times 38 columns. Virtualising ten rows would
    /// be machinery guarding nothing.</para>
    /// </summary>
    public partial class QuestsRuntimeEditor
    {
        private const float LIST_W = 300f;
        private const float DETAIL_W = 460f;
        private const float PANEL_H = 600f;

        /// <summary>
        /// Positive on purpose. <c>ApplyPanelDock</c> NEGATES the vertical offset it is given,
        /// so a negative here docks the panel above the canvas and takes its header off screen
        /// — exactly what shipped in the Controls editor and left two panels unreadable.
        /// </summary>
        private const float PANEL_TOP = TileEditorUIHelpers.PANEL_TOP_OFFSET;

        private Transform _listContent;
        private Transform _detailContent;
        private RectTransform _listScrollContent;
        private RectTransform _detailScrollContent;
        private TextMeshProUGUI _status;

        private DraggablePanel _listPanel;
        private DraggablePanel _detailPanel;

        private readonly List<Button> _tabButtons = new List<Button>();

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("QuestsEditorCanvas", 117);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            EditorUIHelpers.MakeDropPanel(
                "QuestsListPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopLeft, 16f, PANEL_TOP, LIST_W, PANEL_H,
                "Misiones", out _listContent, out _listPanel);

            EditorUIHelpers.MakeDropPanel(
                "QuestsDetailPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopRight, 16f, PANEL_TOP, DETAIL_W, PANEL_H,
                "Detalle", out _detailContent, out _detailPanel);

            // NEITHER PANEL MAY BE CLOSED. Both ship closable by default, this editor has no
            // toolbar outside them, and EditorWorkspaceService persists `open:false` for a
            // closed panel — so closing both would make the editor open empty forever with no
            // way back. That is not hypothetical: it is what shipped in the Controls editor.
            _listPanel.ShowCloseButton = false;
            _detailPanel.ShowCloseButton = false;
            // Trial of the pre-game menus' language on an authoring editor (QuestsRuntimeEditor.Skin.cs).
            SkinPanels();

            BuildListPanel();
            BuildDetailPanel();
        }

        /// <summary>
        /// Heal a workspace document written before the close buttons were removed: a panel
        /// persisted as closed stays closed on restore.
        /// </summary>
        private void ForcePanelsOpen()
        {
            Reopen(_listPanel);
            Reopen(_detailPanel);
        }

        private static void Reopen(DraggablePanel panel)
        {
            if (panel == null) return;
            panel.gameObject.SetActive(true);
            panel.MarkOpened();
        }

        private void BuildListPanel()
        {
            var tabs = EditorUIHelpers.CreateUI("Tabs", _listContent);
            var tabsRt = tabs.GetComponent<RectTransform>();
            tabsRt.sizeDelta = new Vector2(0f, 26f);
            var tabsLe = tabs.AddComponent<LayoutElement>();
            tabsLe.preferredHeight = 26f;
            tabsLe.flexibleHeight = 0f;
            var tabsHlg = tabs.AddComponent<HorizontalLayoutGroup>();
            tabsHlg.spacing = 2f;
            tabsHlg.childForceExpandWidth = true;
            tabsHlg.childForceExpandHeight = true;
            tabsHlg.childControlWidth = true;
            tabsHlg.childControlHeight = true;

            _tabButtons.Clear();
            _tabSkins.Clear();
            AddTabButton(tabs.transform, "Activas",    Tab.Active);
            AddTabButton(tabs.transform, "Ofrecidas",  Tab.Available);
            AddTabButton(tabs.transform, "Bloqueadas", Tab.Locked);
            AddTabButton(tabs.transform, "Hechas",     Tab.Completed);

            var built = EditorUIHelpers.MakeScrollView(_listContent, "QuestList", 1f);
            _listScrollContent = built.content;
            UIFactory.AddVerticalScrollbar(built.scroll);
            ClearScrollSlab(built.scroll);

            EditorFrontendSkin.Rule(_listContent);

            // The tracker controls live on the LIST panel rather than in the detail, because
            // they are about the window and not about any one quest — the same reason the
            // conversation panel's Reiniciar sits at the foot of the gutter.
            EditorFrontendSkin.SkinButton(
                EditorUIHelpers.MakeButton(_listContent, "Seguidor: mostrar / ocultar", ToggleTracker, 24f, 11f),
                UITheme.BTN_NORMAL, UITheme.BTN_HOVER);
            EditorFrontendSkin.SkinButton(
                EditorUIHelpers.MakeButton(_listContent, "Seguidor: minimizar", ToggleTrackerMinimized, 24f, 11f),
                UITheme.BTN_NORMAL, UITheme.BTN_HOVER);

            _status = EditorUIHelpers.AddLabel(_listContent, "", 11f);
            _status.color = EditorUIHelpers.TEXT_MUTED;
        }

        private void AddTabButton(Transform parent, string label, Tab tab) => AddSkinnedTab(parent, label, tab);

        private void BuildDetailPanel()
        {
            var built = EditorUIHelpers.MakeScrollView(_detailContent, "QuestDetail", 1f);
            _detailScrollContent = built.content;
            UIFactory.AddVerticalScrollbar(built.scroll);
            ClearScrollSlab(built.scroll);
        }

        // ── Refresh ────────────────────────────────────────────────────────────

        internal void RefreshAll()
        {
            if (!_uiBuilt) return;
            RefreshTabs();
            RefreshList();
            RefreshDetail();
        }

        private void RefreshTabs()
        {
            // The chosen tab is the loading bar FILLED and the rest its empty groove: geometry,
            // so no ColorBlock multiplies into it (the reason UIButton.SetTint existed here).
            PaintSkinnedTabs();
        }

        private void RefreshList()
        {
            ClearChildren(_listScrollContent);
            if (_listScrollContent == null) return;

            var quests = QuestsInTab();
            if (quests.Count == 0)
            {
                var empty = EditorUIHelpers.AddLabel(_listScrollContent, "Nada en esta pestana.", 11f);
                empty.color = EditorUIHelpers.TEXT_MUTED;
                return;
            }

            var mgr = Manager;
            _selectedRowFill = null;
            for (int i = 0; i < quests.Count; i++)
            {
                var def = quests[i];
                string label = def.displayName;
                float progress = -1f;

                // The PROGRESS is on the row, not only in the detail: the question this list
                // is opened to answer is usually "which of these is stuck", and an author
                // should not have to click ten rows to find out.
                if (mgr != null && mgr.IsActive(def.questId))
                {
                    var quest = mgr.GetActiveQuest(def.questId);
                    // The row's bar says it; a "40%" beside it was the part the bold selected label
                    // truncated first (first capture of the skinned list).
                    if (quest != null) progress = quest.OverallProgress;
                }

                bool selected = string.Equals(_selectedId, def.questId, System.StringComparison.OrdinalIgnoreCase);
                BuildQuestRow(_listScrollContent, def.questId, label, progress, selected);
            }
        }

        private void RefreshDetail()
        {
            ClearChildren(_detailScrollContent);
            if (_detailScrollContent == null) return;

            var def = Selected();
            if (def == null)
            {
                var none = EditorUIHelpers.AddLabel(_detailScrollContent,
                    "Selecciona una mision de la lista.", 12f);
                none.color = EditorUIHelpers.TEXT_MUTED;
                return;
            }

            var mgr = Manager;
            var svc = Service;
            bool isActive = mgr != null && mgr.IsActive(def.questId);
            bool isDone   = mgr != null && mgr.IsCompleted(def.questId);

            EditorUIHelpers.BuildSectionHeader(_detailScrollContent, def.displayName);
            AddInfo($"id: {def.questId}");
            AddInfo($"estado: {(isActive ? "ACTIVA" : isDone ? "COMPLETADA" : "no aceptada")}");

            if (!string.IsNullOrEmpty(def.giverPersonaId))  AddInfo($"da: {def.giverPersonaId}");
            if (!string.IsNullOrEmpty(def.turnInPersonaId)) AddInfo($"entrega: {def.turnInPersonaId}");
            if (def.requiredLevel > 0)    AddInfo($"nivel requerido: {def.requiredLevel}");
            if (def.recommendedLevel > 0) AddInfo($"nivel sugerido: {def.recommendedLevel}");
            AddInfo($"paga: {def.xpReward} XP, {def.coinReward} monedas");

            if (!isActive && !isDone && svc != null)
            {
                string lockReason = svc.DescribeLock(def);
                if (!string.IsNullOrEmpty(lockReason))
                {
                    var warn = EditorUIHelpers.AddLabel(_detailScrollContent, lockReason, 11f);
                    warn.color = EditorUIHelpers.DANGER;
                }
            }

            EditorFrontendSkin.Rule(_detailScrollContent);
            BuildObjectiveRows(def, isActive);
            EditorFrontendSkin.Rule(_detailScrollContent);
            BuildActionRow(def, isActive, isDone);
        }

        /// <summary>
        /// One row per objective. Live counters and the three ways to move them while the
        /// quest is active; the AUTHORED description alone when it is not, because an
        /// objective only has a counter once the quest is running.
        /// </summary>
        private void BuildObjectiveRows(QuestDefinition def, bool isActive)
        {
            var mgr = Manager;
            var quest = isActive && mgr != null ? mgr.GetActiveQuest(def.questId) : null;

            if (quest == null)
            {
                if (def.objectives == null || def.objectives.Length == 0)
                {
                    AddInfo("Sin objetivos autorizados.");
                    return;
                }
                for (int i = 0; i < def.objectives.Length; i++)
                    AddInfo("- " + QuestManager.DescribeObjective(def.objectives[i]));
                return;
            }

            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                var obj = quest.Objectives[i];
                if (obj == null) continue;

                var row = EditorUIHelpers.CreateUI("Objective_" + i, _detailScrollContent);
                var le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 24f;
                // preferredHeight alone leaves flexibleHeight at its unset -1, so the value
                // used comes from the layout group on the parent, which reports 1 while
                // childForceExpandHeight is on.
                le.flexibleHeight = 0f;

                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;

                var textGo = EditorUIHelpers.CreateUI("Text", row.transform);
                var textLe = textGo.AddComponent<LayoutElement>();
                textLe.flexibleWidth = 1f;
                var text = textGo.AddComponent<TextMeshProUGUI>();
                text.text = $"{(obj.IsComplete ? "[x]" : "[ ]")} {obj.Description}  {obj.Current}/{obj.Target}";
                text.fontSize = 11f;
                text.alignment = TextAlignmentOptions.Left;
                text.color = obj.IsComplete ? EditorUIHelpers.SUCCESS : EditorUIHelpers.TEXT_PRIMARY;
                text.raycastTarget = false;

                AddObjectiveBar(row.transform, obj.Target > 0 ? (float)obj.Current / obj.Target : 0f);

                if (!(obj is ObjectiveBase ob)) continue;

                int index = i;
                AddStepButton(row.transform, "-1", () => SetObjectiveProgress(index, ob.Current - 1));
                AddStepButton(row.transform, "+1", () => SetObjectiveProgress(index, ob.Current + 1));
                AddStepButton(row.transform, "max", () => SetObjectiveProgress(index, ob.Target));
            }
        }

        private void AddStepButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, onClick, 20f, 10f);
            SkinStepButton(btn);
            var le = btn.gameObject.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 34f;
            le.flexibleWidth = 0f;
        }

        private void BuildActionRow(QuestDefinition def, bool isActive, bool isDone)
        {
            bool armed = string.Equals(_armedDangerId, def.questId, System.StringComparison.OrdinalIgnoreCase);

            if (isActive)
            {
                var complete = EditorUIHelpers.MakeButton(_detailScrollContent, "Completar (paga recompensas)",
                    CompleteSelected, 26f, 11f);
                complete.gameObject.name = "QuestCompleteButton";
                EditorFrontendSkin.SkinButton(complete, EditorFrontendSkin.Gold, UITheme.SUCCESS);

                var drop = EditorUIHelpers.MakeDangerButton(_detailScrollContent,
                    armed ? "Seguro? Soltar" : "Soltar mision", DropSelected, 26f);
                drop.gameObject.name = "QuestDropButton";
                EditorFrontendSkin.SkinButton(drop, armed ? UITheme.DANGER : UITheme.DANGER_IDLE, UITheme.DANGER);
            }
            else if (isDone)
            {
                var forget = EditorUIHelpers.MakeDangerButton(_detailScrollContent,
                    armed ? "Seguro? Olvidar" : "Olvidar (volver a ofrecerla)", ForgetSelected, 26f);
                forget.gameObject.name = "QuestForgetButton";
                EditorFrontendSkin.SkinButton(forget, armed ? UITheme.DANGER : UITheme.DANGER_IDLE, UITheme.DANGER);
            }
            else
            {
                var accept = EditorUIHelpers.MakeButton(_detailScrollContent,
                    "Aceptar (sin hablar con nadie)", AcceptSelected, 26f, 11f);
                accept.gameObject.name = "QuestAcceptButton";
                EditorFrontendSkin.SkinButton(accept, EditorFrontendSkin.Gold, UITheme.SUCCESS);
            }
        }

        private void AddInfo(string text)
        {
            var label = EditorUIHelpers.AddLabel(_detailScrollContent, text, 11f);
            label.color = EditorUIHelpers.TEXT_SECONDARY;
        }

        internal void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        /// <summary>
        /// Empties a rebuilt list. <c>Object.Destroy</c> is an outright ERROR in Edit Mode and
        /// this editor is reachable from EditMode fixtures — the trap seven ControlsEditorTests
        /// went red on, with every assertion passing.
        /// </summary>
        private static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
