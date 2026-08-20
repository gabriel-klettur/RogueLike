using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data.Feel;
using Valkur.Gameplay.Editors.CameraFeelEditor;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Builds the Camera Editor's UI for real and checks it came out wired.
    ///
    /// This fixture exists because a whole set of source-level contract tests passed while
    /// the editor was completely broken on screen. <c>MakeDropPanel</c> already puts a
    /// <c>VerticalLayoutGroup</c> on its content area, and <c>LayoutGroup</c> carries
    /// <c>[DisallowMultipleComponent]</c> — so adding a second returns null instead of
    /// throwing, the next line dereferenced it, and the resulting NullReferenceException
    /// aborted construction on the first of seven panels. What shipped was one empty header.
    ///
    /// Grepping the source can prove a method is called. Only constructing the thing can
    /// prove it produced anything.
    /// </summary>
    [TestFixture]
    public class CameraEditorBuildTests
    {
        private GameObject _canvasGo;
        private CameraEditorUIBuilder.UIRefs _refs;

        private readonly List<(CameraFeelTunable id, float value)> _tunableCalls =
            new List<(CameraFeelTunable, float)>();
        private readonly List<(string field, float value)> _cueFieldCalls =
            new List<(string, float)>();
        private readonly List<CameraFeelCue> _cueSelections = new List<CameraFeelCue>();
        private readonly List<CameraFeelCue> _cueTests = new List<CameraFeelCue>();
        private readonly List<CameraFeelPreset> _presets = new List<CameraFeelPreset>();
        private readonly List<string> _panelToggles = new List<string>();
        private int _tutorialCalls, _saveCalls, _resetCalls, _undoCalls, _redoCalls;

        [SetUp]
        public void SetUp()
        {
            // TMP object creation logs in EditMode without a full font pipeline; the project
            // convention is to ignore those rather than fail on them.
            LogAssert.ignoreFailingMessages = true;

            _canvasGo = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)_canvasGo.transform;
            rt.sizeDelta = new Vector2(1920f, 1080f);

            _tunableCalls.Clear();
            _cueFieldCalls.Clear();
            _cueSelections.Clear();
            _cueTests.Clear();
            _presets.Clear();
            _panelToggles.Clear();
            _tutorialCalls = _saveCalls = _resetCalls = _undoCalls = _redoCalls = 0;

            _refs = CameraEditorUIBuilder.BuildAll(_canvasGo.transform,
                new CameraEditorUIBuilder.Callbacks
                {
                    OnTunable = (id, v) => _tunableCalls.Add((id, v)),
                    OnCueField = (f, v) => _cueFieldCalls.Add((f, v)),
                    OnCueSelected = c => _cueSelections.Add(c),
                    OnCueTest = c => _cueTests.Add(c),
                    CurrentCue = () => CameraFeelCue.Hurt,
                    OnPreset = p => _presets.Add(p),
                    OnTogglePanel = id => _panelToggles.Add(id),
                    OnTutorial = () => _tutorialCalls++,
                    OnSave = () => _saveCalls++,
                    OnReset = () => _resetCalls++,
                    OnUndo = () => _undoCalls++,
                    OnRedo = () => _redoCalls++,
                });
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) UnityEngine.Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Existence ───────────────────────────────────────────────────────

        [Test]
        public void BuildAll_Succeeds()
        {
            Assert.IsNotNull(_refs, "BuildAll returned nothing.");
        }

        [Test]
        public void EveryPanelIsBuilt()
        {
            foreach (string id in CameraEditorUIBuilder.AllPanels)
            {
                Assert.IsTrue(_refs.Panels.ContainsKey(id), $"Panel '{id}' was never created.");
                Assert.IsNotNull(_refs.Panels[id], $"Panel '{id}' is null.");
            }

            Assert.AreEqual(CameraEditorUIBuilder.AllPanels.Length, _refs.Panels.Count,
                "A panel exists that AllPanels does not know about, or vice versa.");
        }

        [Test]
        public void NoPanelIsEmpty()
        {
            // The exact symptom of the bug this fixture was written for: a panel that renders
            // its header and holds nothing.
            var empty = new List<string>();

            foreach (var pair in _refs.Panels)
            {
                Transform content = FindContent(pair.Value.transform);
                Assert.IsNotNull(content, $"Panel '{pair.Key}' has no Content child.");
                if (content.childCount == 0) empty.Add(pair.Key);
            }

            Assert.IsEmpty(empty,
                "These panels were built with no contents at all: " + string.Join(", ", empty));
        }

        [Test]
        public void EveryTunableGotARow()
        {
            var built = _refs.Rows.Select(r => r.Id).ToList();

            var missing = CameraFeelProfile.Tunables
                .Select(t => t.Id)
                .Where(id => !built.Contains(id))
                .ToList();

            Assert.IsEmpty(missing,
                "These tunables have no slider, so they can be read in the profile and never " +
                "changed: " + string.Join(", ", missing));

            Assert.AreEqual(CameraFeelProfile.Tunables.Count, _refs.Rows.Count,
                "Row count does not match the tunable table — something is built twice.");
        }

        [Test]
        public void EveryRowIsFullyFormed()
        {
            foreach (var row in _refs.Rows)
            {
                Assert.IsNotNull(row.Slider, $"{row.Id} has no slider.");
                Assert.IsNotNull(row.Value, $"{row.Id} has no value label.");

                var info = CameraFeelProfile.GetInfo(row.Id);
                Assert.AreEqual(info.Min, row.Slider.minValue, 1e-4f,
                    $"{row.Id}'s slider does not use its declared minimum.");
                Assert.AreEqual(info.Max, row.Slider.maxValue, 1e-4f,
                    $"{row.Id}'s slider does not use its declared maximum.");
            }
        }

        [Test]
        public void EveryRowLivesInThePanelForItsGroup()
        {
            foreach (var row in _refs.Rows)
            {
                var info = CameraFeelProfile.GetInfo(row.Id);
                string expected = PanelIdFor(info.Group);
                Assert.IsTrue(_refs.Panels.ContainsKey(expected),
                    $"No panel for group {info.Group}.");

                Assert.IsTrue(row.Slider.transform.IsChildOf(_refs.Panels[expected].transform),
                    $"{row.Id} belongs to {info.Group} but was built somewhere else — closing " +
                    $"the {expected} panel would not hide it.");
            }
        }

        [Test]
        public void TheCuePanelIsComplete()
        {
            Assert.AreEqual(9, _refs.CueRows.Count,
                "A FeelCue has nine fields; every one needs a row or it cannot be tuned here.");

            foreach (var row in _refs.CueRows)
            {
                Assert.IsNotNull(row.Slider, $"Cue field '{row.Field}' has no slider.");
                Assert.IsNotNull(row.Value, $"Cue field '{row.Field}' has no value label.");
                Assert.Less(row.Slider.minValue, row.Slider.maxValue,
                    $"Cue field '{row.Field}' has an empty slider range.");
            }

            int cueCount = Enum.GetValues(typeof(CameraFeelCue)).Length;
            Assert.AreEqual(cueCount, _refs.CueButtons.Count,
                "Every cue needs a button or it cannot be selected.");
            foreach (var pair in _refs.CueButtons)
                Assert.IsNotNull(pair.Value, $"Cue button for {pair.Key} is null.");
        }

        [Test]
        public void TheDefaultLayoutIsASubsetThatFitsOnScreen()
        {
            foreach (string id in CameraEditorUIBuilder.DefaultPanels)
                Assert.Contains(id, CameraEditorUIBuilder.AllPanels,
                    $"DefaultPanels names '{id}', which is not a panel that exists.");

            Assert.IsNotEmpty(CameraEditorUIBuilder.DefaultPanels,
                "Opening the editor to a blank screen teaches nobody that it has panels.");

            // Every panel opened by default must fit inside the game view, or the editor
            // greets you with panels stacked on top of each other. Measured against the
            // shortest viewport the project targets.
            const float viewportHeight = 780f;
            var perColumn = new Dictionary<float, float>();

            foreach (string id in CameraEditorUIBuilder.DefaultPanels)
            {
                var rt = (RectTransform)_refs.Panels[id].transform;
                float column = Mathf.Round(rt.anchorMin.x);   // 0 = left edge, 1 = right edge
                perColumn.TryGetValue(column, out float used);
                perColumn[column] = used + rt.sizeDelta.y + 8f;
            }

            foreach (var pair in perColumn)
                Assert.LessOrEqual(pair.Value, viewportHeight,
                    $"The default panels on the {(pair.Key < 0.5f ? "left" : "right")} column " +
                    $"need {pair.Value:0} px of a {viewportHeight:0} px view, so they would " +
                    "open overlapping each other.");
        }

        [Test]
        public void EveryPanelHasAMenuButton()
        {
            foreach (string id in CameraEditorUIBuilder.AllPanels)
            {
                Assert.IsTrue(_refs.MenuButtons.ContainsKey(id) && _refs.MenuButtons[id] != null,
                    $"Panel '{id}' has no menu button, so once closed it cannot be reopened.");
                Assert.IsTrue(_refs.MenuLabels.ContainsKey(id) && _refs.MenuLabels[id] != null,
                    $"Panel '{id}' has no menu label to highlight.");
            }
        }

        [Test]
        public void EveryReadoutSurfaceExists()
        {
            Assert.IsNotNull(_refs.Status, "No status line.");
            Assert.IsNotNull(_refs.Readout, "No derived readout — the net lead would be invisible.");
            Assert.IsNotNull(_refs.Diagnostics, "No live solver readout.");
            Assert.IsNotNull(_refs.Help, "No hover-help surface.");
            Assert.IsNotNull(_refs.CueTitle, "No cue title.");
        }

        // ── Wiring ──────────────────────────────────────────────────────────

        [Test]
        public void MovingASliderReportsTheRightTunable()
        {
            // Existence is not wiring. This is the assertion that a slider actually reaches
            // the field its label claims.
            foreach (var row in _refs.Rows)
            {
                _tunableCalls.Clear();

                var info = CameraFeelProfile.GetInfo(row.Id);
                float target = Mathf.Lerp(info.Min, info.Max, 0.625f);
                row.Slider.value = target;

                Assert.AreEqual(1, _tunableCalls.Count,
                    $"Moving {row.Id}'s slider reported {_tunableCalls.Count} changes.");
                Assert.AreEqual(row.Id, _tunableCalls[0].id,
                    $"{row.Id}'s slider reported {_tunableCalls[0].id} instead.");
                Assert.AreEqual(target, _tunableCalls[0].value, 1e-3f);
            }
        }

        [Test]
        public void MovingACueSliderReportsTheRightField()
        {
            foreach (var row in _refs.CueRows)
            {
                _cueFieldCalls.Clear();

                float target = Mathf.Lerp(row.Slider.minValue, row.Slider.maxValue, 0.4f);
                row.Slider.value = target;

                Assert.AreEqual(1, _cueFieldCalls.Count,
                    $"Cue field '{row.Field}' reported {_cueFieldCalls.Count} changes.");
                Assert.AreEqual(row.Field, _cueFieldCalls[0].field);
                Assert.AreEqual(target, _cueFieldCalls[0].value, 1e-3f);
            }
        }

        [Test]
        public void EveryCueButtonSelectsItsOwnCue()
        {
            foreach (var pair in _refs.CueButtons)
            {
                _cueSelections.Clear();
                Click(pair.Value.gameObject);

                Assert.AreEqual(1, _cueSelections.Count,
                    $"The {pair.Key} button reported {_cueSelections.Count} selections.");
                Assert.AreEqual(pair.Key, _cueSelections[0],
                    $"The {pair.Key} button selected {_cueSelections[0]} instead — the classic " +
                    "captured-loop-variable bug, and one that only a click can catch.");
            }
        }

        [Test]
        public void EveryMenuButtonTogglesItsOwnPanel()
        {
            foreach (string id in CameraEditorUIBuilder.AllPanels)
            {
                _panelToggles.Clear();
                Click(_refs.MenuButtons[id].gameObject);

                Assert.AreEqual(1, _panelToggles.Count, $"'{id}' reported no toggle.");
                Assert.AreEqual(id, _panelToggles[0],
                    $"The '{id}' menu button toggles '{_panelToggles[0]}'.");
            }
        }

        [Test]
        public void EveryActionButtonIsWired()
        {
            var live = _refs.Panels[CameraEditorUIBuilder.PANEL_LIVE];

            ClickByLabel(live, "SAVE TO ASSET");
            Assert.AreEqual(1, _saveCalls, "SAVE is not wired.");

            ClickByLabel(live, "RESET TO DEFAULTS");
            Assert.AreEqual(1, _resetCalls, "RESET is not wired.");

            ClickByLabel(_refs.Panels[CameraEditorUIBuilder.PANEL_CUES], "TEST THIS CUE");
            Assert.AreEqual(1, _cueTests.Count, "TEST THIS CUE is not wired.");
            Assert.AreEqual(CameraFeelCue.Hurt, _cueTests[0],
                "TEST fired something other than the currently selected cue.");
        }

        [Test]
        public void EveryPresetButtonIsWired()
        {
            var live = _refs.Panels[CameraEditorUIBuilder.PANEL_LIVE];

            foreach (CameraFeelPreset preset in Enum.GetValues(typeof(CameraFeelPreset)))
            {
                _presets.Clear();
                ClickByLabel(live, preset.ToString());

                Assert.AreEqual(1, _presets.Count, $"Preset '{preset}' has no working button.");
                Assert.AreEqual(preset, _presets[0],
                    $"The '{preset}' button applied '{_presets[0]}' instead.");
            }
        }

        [Test]
        public void UndoRedoAndTutorialAreWired()
        {
            var bar = FindByName(_canvasGo.transform, "CameraMenuBar");
            Assert.IsNotNull(bar, "No menu bar.");

            ClickByLabel(bar.gameObject, "Undo");
            Assert.AreEqual(1, _undoCalls, "Undo is not wired.");

            ClickByLabel(bar.gameObject, "Redo");
            Assert.AreEqual(1, _redoCalls, "Redo is not wired.");

            ClickByLabel(bar.gameObject, "?");
            Assert.AreEqual(1, _tutorialCalls, "The tutorial button is not wired.");
        }

        [Test]
        public void EverySliderHasTheGraphicsItNeedsToBeDragged()
        {
            // A Slider with no fillRect and no handleRect renders and accepts nothing.
            foreach (var row in _refs.Rows)
            {
                Assert.IsNotNull(row.Slider.fillRect, $"{row.Id}'s slider has no fill.");
                Assert.IsNotNull(row.Slider.handleRect, $"{row.Id}'s slider has no handle.");
                Assert.IsNotNull(row.Slider.targetGraphic,
                    $"{row.Id}'s slider has no target graphic and cannot be clicked.");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string PanelIdFor(CameraFeelGroup group)
        {
            switch (group)
            {
                case CameraFeelGroup.Follow:         return CameraEditorUIBuilder.PANEL_FOLLOW;
                case CameraFeelGroup.Lead:           return CameraEditorUIBuilder.PANEL_LEAD;
                case CameraFeelGroup.Shake:          return CameraEditorUIBuilder.PANEL_SHAKE;
                case CameraFeelGroup.Global:         return CameraEditorUIBuilder.PANEL_GLOBAL;
                case CameraFeelGroup.Classification: return CameraEditorUIBuilder.PANEL_CLASSIFY;
                default:                             return null;
            }
        }

        private static Transform FindContent(Transform panel)
        {
            foreach (Transform child in panel)
                if (child.name == "Content") return child;
            return null;
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                Transform found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void Click(GameObject go)
        {
            var button = go.GetComponent<Button>();
            Assert.IsNotNull(button, $"'{go.name}' has no Button to click.");
            button.onClick.Invoke();
        }

        private static void ClickByLabel(GameObject root, string label)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(includeInactive: true))
            {
                var tmp = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(includeInactive: true);
                if (tmp == null || tmp.text != label) continue;
                button.onClick.Invoke();
                return;
            }
            Assert.Fail($"No button labelled '{label}' under '{root.name}'.");
        }
    }
}
