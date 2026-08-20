using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// Runtime Camera Editor, opened from the General Editor (ESC → Camera).
    ///
    /// Camera feel is the one subsystem that genuinely cannot be tuned from the Inspector:
    /// every value is a judgement about motion, and the loop of stop, edit, play, walk
    /// around, stop again destroys the only thing that matters — whether it feels right in
    /// your hands. Every slider here writes the live profile immediately, and the cue panel
    /// fires the beat you are editing on demand, so the loop is one frame long.
    ///
    /// It carries no F-key deliberately. Twelve of the thirteen function keys are already
    /// taken, and a tuning surface used during a tuning pass does not need a shortcut the
    /// player might hit by accident.
    /// </summary>
    public sealed partial class CameraRuntimeEditor : SingletonMonoBehaviour<CameraRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        private bool _active;
        private bool _uiBuilt;
        private bool _syncing;

        private Canvas _canvas;
        private GameObject _root;
        private CameraEditorUIBuilder.UIRefs _ui;
        private CameraFeelProfile _profile;
        private CameraFeelCue _selectedCue = CameraFeelCue.AttackConnect;

        public string EditorName => "Camera";
        public bool IsActive => _active;

        /// <summary>The profile being edited. Null until the feel director has loaded one.</summary>
        internal CameraFeelProfile Profile => _profile;

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        public void Activate()
        {
            if (!ResolveProfile())
            {
                Debug.LogWarning("[CameraEditor] No CameraFeelProfile available — is the " +
                                 "CameraFeelDirector in the scene?");
                return;
            }

            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[CameraEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            _root.SetActive(true);
            OpenDefaultPanels();
            SyncFromProfile();
            SelectCue(_selectedCue);
            SetStatus("Editing live — every change applies immediately. SAVE writes the asset. " +
                      "Press ? for how the model works.");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        private void Update()
        {
            if (!_active) return;
            RefreshReadout();
        }

        private bool ResolveProfile()
        {
            if (_profile != null) return true;
            _profile = Resources.Load<CameraFeelProfile>("CameraFeelProfile");
            return _profile != null;
        }

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("CameraEditorCanvas", 113);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = CameraEditorUIBuilder.BuildAll(_root.transform,
                new CameraEditorUIBuilder.Callbacks
                {
                    OnTunable = OnTunableChanged,
                    OnCueField = OnCueFieldChanged,
                    OnCueSelected = SelectCue,
                    OnCueTest = TestCue,
                    CurrentCue = () => _selectedCue,
                    OnPreset = ApplyPreset,
                    OnTogglePanel = TogglePanel,
                    OnTutorial = ToggleTutorial,
                    OnSave = SaveToAsset,
                    OnReset = ResetToDefaults,
                    OnUndo = Undo,
                    OnRedo = Redo,
                });

            BuildTutorial();
        }

        private void SetStatus(string message)
        {
            if (_ui?.Status != null) _ui.Status.text = message;
        }
    }
}
