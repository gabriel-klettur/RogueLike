using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    public partial class LightingRuntimeEditor : SingletonMonoBehaviour<LightingRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void BuildMainPanel()
        {
            var panel = EditorUIHelpers.MakeSidebar("MainPanel", _root.transform, 260f);
            EditorUIHelpers.AddVLG(panel, 6, 4f);
            EditorUIHelpers.MakeTitleBar(panel.transform, "LIGHTING EDITOR");

            // Global Toggles
            EditorUIHelpers.BuildSectionHeader(panel.transform, "GLOBAL TOGGLES");
            MakeToggle(panel.transform, "Ambient", _ambientEnabled, v => { _ambientEnabled = v; _statusTmp.text = $"Ambient: {(v ? "ON" : "OFF")}"; });
            MakeToggle(panel.transform, "Point Lights", _pointLightsEnabled, v => { _pointLightsEnabled = v; _statusTmp.text = $"Point Lights: {(v ? "ON" : "OFF")}"; });
            MakeToggle(panel.transform, "Shadows", _shadowsEnabled, v => { _shadowsEnabled = v; _statusTmp.text = $"Shadows: {(v ? "ON" : "OFF")}"; });

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Quality Steppers
            EditorUIHelpers.BuildSectionHeader(panel.transform, "QUALITY");
            MakeStepper(panel.transform, "Max Lights", _maxLights, 1, 64, v => _maxLights = v);
            MakeStepper(panel.transform, "Max Radius", _maxRadius, 16, 1024, v => _maxRadius = v);
            MakeStepper(panel.transform, "Shadow Rays", _shadowRays, 8, 256, v => _shadowRays = v);

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Overlay controls
            EditorUIHelpers.BuildSectionHeader(panel.transform, "OVERLAY");
            MakeToggle(panel.transform, "Overlay", _overlayVisible, v => _overlayVisible = v);
            MakeToggle(panel.transform, "Labels", _labelsVisible, v => _labelsVisible = v);

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Mode toolbar
            EditorUIHelpers.BuildSectionHeader(panel.transform, "TOOLS");
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", panel.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var sel = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = sel.GetComponent<Image>();
            var spn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spn.GetComponent<Image>();
            var del = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = del.GetComponent<Image>();

            var utilRow = EditorUIHelpers.CreateUI("UtilRow", panel.transform);
            utilRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var uhlg = utilRow.AddComponent<HorizontalLayoutGroup>();
            uhlg.spacing = 4f; uhlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(utilRow.transform, "Save", () => SaveInstances(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            _statusTmp = EditorUIHelpers.MakeStatusText(panel.transform);
        }

        // ── Panel 2: Day/Night Controls (centre) ──

        private void BuildDayTimePanel()
        {
            // Centre panel
            var panel = new GameObject("DayTimePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(264f, 4f);
            rt.offsetMax = new Vector2(-304f, -4f);
            panel.GetComponent<Image>().color = EditorUIHelpers.BG_PANEL;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            EditorUIHelpers.BuildSectionHeader(panel.transform, "DAY / NIGHT CYCLE");

            // Time display
            _dayTimeTmp = EditorUIHelpers.AddLabel(panel.transform, "12:00 — Day", 14f);
            _dayTimeTmp.alignment = TextAlignmentOptions.Center;
            _dayTimeTmp.color = EditorUIHelpers.ACCENT;

            // Quick time buttons
            var timeRow = EditorUIHelpers.CreateUI("TimeRow", panel.transform);
            timeRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var trHlg = timeRow.AddComponent<HorizontalLayoutGroup>();
            trHlg.spacing = 4f; trHlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeButton(timeRow.transform, "-30m", () => AdjustTime(-30), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "-5m", () => AdjustTime(-5), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "+5m", () => AdjustTime(5), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "+30m", () => AdjustTime(30), 24f, 10f);

            // Jump buttons
            var jumpRow = EditorUIHelpers.CreateUI("JumpRow", panel.transform);
            jumpRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var jHlg = jumpRow.AddComponent<HorizontalLayoutGroup>();
            jHlg.spacing = 4f; jHlg.childForceExpandWidth = true;

            int[] jumpTimes = { 0, 300, 420, 720, 1140, 1260 };
            string[] jumpLabels = { "00:00", "05:00", "07:00", "12:00", "19:00", "21:00" };
            for (int i = 0; i < jumpTimes.Length; i++)
            {
                int mins = jumpTimes[i];
                EditorUIHelpers.MakeButton(jumpRow.transform, jumpLabels[i], () => JumpToTime(mins), 22f, 9f);
            }

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Time scale stepper
            MakeStepper(panel.transform, "Time Scale", Mathf.RoundToInt(_timeScale * 10f), 0, 100,
                v => _timeScale = v / 10f, "min/s × 0.1");

            // Min intensity stepper
            MakeStepper(panel.transform, "Min Intensity", Mathf.RoundToInt(_minIntensity * 100f), 0, 100,
                v => _minIntensity = v / 100f, "%");

            EditorUIHelpers.BuildSeparator(panel.transform);
            EditorUIHelpers.BuildSectionHeader(panel.transform, "KEYFRAME INTENSITIES");

            // Keyframe editors
            string[] kfLabels = { "00:00", "05:00", "07:00", "12:00", "19:00", "21:00" };
            float[] kfDefaults = { 0f, 0f, 1f, 1f, 1f, 0f };
            for (int i = 0; i < kfLabels.Length; i++)
            {
                int idx = i;
                var kfRow = EditorUIHelpers.CreateUI($"KF_{kfLabels[i]}", panel.transform);
                kfRow.AddComponent<LayoutElement>().preferredHeight = 22f;
                var khlg = kfRow.AddComponent<HorizontalLayoutGroup>();
                khlg.spacing = 4f; khlg.childForceExpandWidth = true;

                EditorUIHelpers.AddLabel(kfRow.transform, kfLabels[i], 10f);
                EditorUIHelpers.AddLabel(kfRow.transform, $"{kfDefaults[idx]:F2}", 10f);
            }
        }

        // ── Panel 3: Light Presets (right) ──

        private void BuildPresetsPanel()
        {
            var right = EditorUIHelpers.MakeRightPanel("PresetsPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(right, 6, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "LIGHT PRESETS");

            _searchBox = SearchBox.Create(right.transform, "Search presets\u2026",
                v => { _searchFilter = v ?? ""; RefreshPresetButtons(); });

            var listGo = EditorUIHelpers.CreateUI("PresetButtons", right.transform);
            var lvlg = listGo.AddComponent<VerticalLayoutGroup>();
            lvlg.spacing = 2f; lvlg.childForceExpandWidth = true; lvlg.childForceExpandHeight = false;
            _presetButtonsParent = listGo.GetComponent<RectTransform>();
            RefreshPresetButtons();

            EditorUIHelpers.BuildSeparator(right.transform);
            EditorUIHelpers.BuildSectionHeader(right.transform, "PRESET PROPERTIES");

            var (scroll, content) = EditorUIHelpers.MakeScrollView(right.transform, "PresetPropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(content, "Select a preset to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
            _propsTmp.richText = true;

            EditorUIHelpers.BuildSeparator(right.transform);

            // Single-shot toggle
            MakeToggle(right.transform, "Single-Shot", _singleShot, v => _singleShot = v);
        }

        // ── Preset Selection ──

    }
}