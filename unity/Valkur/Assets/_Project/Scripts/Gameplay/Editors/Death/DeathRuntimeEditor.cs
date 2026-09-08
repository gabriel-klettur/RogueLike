using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.Editors.Death
{
    /// <summary>
    /// Runtime Death Editor, opened from the General Editor (ESC → Muerte).
    ///
    /// <para><b>WHAT IT IS FOR.</b> The death flow's decisions were spread across four kinds of
    /// home and none of them was reachable: a hard-coded <c>templateId 249</c> written by hand in
    /// two files that had to agree, three <c>[SerializeField]</c> timings on a component
    /// <c>GameplaySceneSetup</c> <c>AddComponent</c>s onto a bare GameObject — so no inspector
    /// anywhere in the project could open them — a layer mask built in code, and an XP penalty on
    /// a third component. The 2026-09-07 audit measured what that cost: zero altars placed in a
    /// 301-building world, the binder binding nothing, the trail drawing nothing, and
    /// <c>Revive()</c> never called by anybody. Nothing failed. The player was simply stuck.</para>
    ///
    /// <para><b>Every number here is a judgement that can only be settled by DYING</b> — is 90
    /// seconds as a ghost a walk or a punishment, does a 35 % rescue read as mercy or as an
    /// insult, is losing the whole purse the thing that makes a player stop playing. The
    /// stop-edit-recompile-die-again loop destroys the only evidence that could answer them, which
    /// is the same argument the Camera editor makes about feel and the Economy editor about
    /// prices.</para>
    ///
    /// <para><b>WHAT IT EDITS, AND WHERE THAT LANDS.</b> One asset,
    /// <see cref="DeathTuning"/> under <c>Resources/</c>, design-time and shared by every save;
    /// SAVE writes it to disk. The Altars tab additionally BINDS live components in the scene,
    /// which is session state and says so.</para>
    ///
    /// <para><b>NO HOTKEY</b>, like the Camera, Controls, Skills and Economy editors: the F-row is
    /// retired project-wide and the General Editor is the only way in, which is what
    /// <c>EditorReachabilityTests</c> pins. <b>It declares no input actions of its own</b>, so
    /// there is no <c>OwnerEditor</c> anywhere in this folder — undo, redo, save and close all live
    /// in the shared <c>EditorShared</c> map and are read through <see cref="EditorInput"/>.</para>
    /// </summary>
    public sealed partial class DeathRuntimeEditor : SingletonMonoBehaviour<DeathRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        /// <summary>
        /// How many edits the history keeps. Matches the Tile, Skills and Economy editors — deep
        /// enough that a tuning pass is fully reversible, bounded so a long session cannot grow it
        /// without limit.
        /// </summary>
        private const int HISTORY_DEPTH = 50;

        /// <summary>The tabs down the left panel, in the order a death actually happens.</summary>
        internal enum Tab
        {
            Flow = 0,
            Spirit = 1,
            Altars = 2,
            Path = 3,
            Cost = 4,
            Audio = 5,
        }

        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;

        private DeathTuning _tuning;
        private Tab _tab = Tab.Flow;
        private bool _applyingHistory;

        private readonly struct EditStep
        {
            public readonly string Label;
            public readonly Action Undo;
            public readonly Action Redo;

            public EditStep(string label, Action undo, Action redo)
            {
                Label = label;
                Undo = undo;
                Redo = redo;
            }
        }

        private readonly List<EditStep> _undo = new List<EditStep>(HISTORY_DEPTH);
        private readonly List<EditStep> _redo = new List<EditStep>(HISTORY_DEPTH);

        /// <summary>
        /// The EXACT string the General Editor opens this editor by, and the string inside the
        /// input context id. It is not the folder name and it is not the map slug — a mismatch
        /// there once silently killed all 35 tools of another editor.
        /// </summary>
        public string EditorName => "Muerte";

        public bool IsActive => _active;

        internal bool CanUndo => _undo.Count > 0;
        internal bool CanRedo => _redo.Count > 0;
        internal Tab ActiveTab => _tab;
        internal DeathTuning Tuning => _tuning;

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
            _tuning = DeathTuning.Active;

            if (!_uiBuilt)
            {
                // Wrapped because a BuildUI that throws half way leaves an editor registered,
                // inactive and impossible to open again, with the exception the only clue. The
                // Camera, Skills and Economy editors carry the same guard for the same reason.
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[DeathEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            if (_root != null) _root.SetActive(true);
            ForcePanelsOpen();
            RefreshAll();
            SetStatus("Editando en vivo. Ctrl+Z deshace, GUARDAR escribe el asset.");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        /// <summary>
        /// The shared editor verbs, read through <see cref="EditorInput"/> rather than raw keys so
        /// a rebind moves them and this editor cannot drift onto a different Undo from the other
        /// eighteen. The live readout ticks after them because it must not swallow a keystroke.
        /// </summary>
        private void Update()
        {
            if (!_active) return;

            if (EditorInput.UndoPressed()) UndoLast();
            else if (EditorInput.RedoPressed()) RedoLast();
            else if (EditorInput.SavePressed()) SaveAsset();

            RefreshLiveReadout();
        }

        // ── History ─────────────────────────────────────────────────────────

        /// <summary>
        /// Record one reversible edit and apply it.
        ///
        /// <para>Applying THROUGH the history rather than beside it is what stops the two
        /// disagreeing: there is no path that changes a value without a matching inverse, because
        /// the change itself is the <paramref name="redo"/> closure. Copied from the Skills and
        /// Economy editors deliberately — a second history implementation is a second set of
        /// off-by-one bugs.</para>
        /// </summary>
        private void Commit(string label, Action undo, Action redo)
        {
            redo();

            if (_applyingHistory) return;

            _undo.Add(new EditStep(label, undo, redo));
            if (_undo.Count > HISTORY_DEPTH) _undo.RemoveAt(0);
            _redo.Clear();

            MarkTuningDirty();
            RefreshAfterEdit();
            SetStatus(label);
        }

        internal void UndoLast()
        {
            if (_undo.Count == 0) { SetStatus("Nada que deshacer."); return; }

            var step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _applyingHistory = true;
            try { step.Undo(); }
            finally { _applyingHistory = false; }

            _redo.Add(step);
            MarkTuningDirty();
            RefreshAll();
            SetStatus($"Deshecho: {step.Label}");
        }

        internal void RedoLast()
        {
            if (_redo.Count == 0) { SetStatus("Nada que rehacer."); return; }

            var step = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _applyingHistory = true;
            try { step.Redo(); }
            finally { _applyingHistory = false; }

            _undo.Add(step);
            MarkTuningDirty();
            RefreshAll();
            SetStatus($"Rehecho: {step.Label}");
        }

        /// <summary>
        /// Mark the asset dirty. The CACHE is deliberately NOT dropped: <c>DeathTuning.Active</c>
        /// hands back this very object, so invalidating would reload from disk and discard the
        /// unsaved edit the author is looking at. Every runtime reader already goes through
        /// <c>Active</c> on every read, which is what makes a change visible in the same frame.
        /// </summary>
        private void MarkTuningDirty()
        {
            if (_tuning == null) return;
#if UNITY_EDITOR
            // Marked dirty for EVERY commit, including the Altars tab's scene-only actions. That
            // is deliberate over-marking: SetDirty on an unchanged asset costs a redundant write at
            // SAVE and nothing else, while tracking which control belongs to which asset would be a
            // second model of the panel's layout that could disagree with the first.
            UnityEditor.EditorUtility.SetDirty(_tuning);
#endif
        }

        // ── Save ────────────────────────────────────────────────────────────

        /// <summary>
        /// Write the tuning to disk.
        ///
        /// <para>In a build there is no asset database, and the status line SAYS SO rather than a
        /// button silently lying — the same contract the Skills and Economy editors state.
        /// Everything the author changed is still live in memory for the session; it just will not
        /// survive a restart, which is the truth and is worth being told.</para>
        /// </summary>
        internal void SaveAsset()
        {
#if UNITY_EDITOR
            if (_tuning == null) { SetStatus("No hay asset que guardar."); return; }

            if (TuningIsUnbacked)
            {
                // Creating it here rather than refusing: an author who has just spent ten minutes
                // tuning should not be told to go and run a menu item first and lose the lot.
                const string dir = "Assets/_Project/Resources/Death";
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                UnityEditor.AssetDatabase.CreateAsset(_tuning, dir + "/DeathTuning.asset");
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();

                // The instance IS the asset now, so the cache still points at the right object and
                // must not be dropped — but the "unbacked" warning has to stop being appended.
                SetStatus("Creado Resources/Death/DeathTuning.asset y guardado.");
                RefreshAll();
                return;
            }

            UnityEditor.EditorUtility.SetDirty(_tuning);
            UnityEditor.AssetDatabase.SaveAssets();
            SetStatus("Guardado en disco.");
#else
            SetStatus("En una build no hay AssetDatabase: los cambios viven solo esta sesion.");
#endif
        }

        /// <summary>
        /// True when the tuning is a throwaway instance rather than a shipped asset — i.e. nobody
        /// has ever pressed SAVE here. Surfaced on the status line so an author does not tune for
        /// ten minutes into nothing.
        /// </summary>
        internal bool TuningIsUnbacked
        {
            get
            {
#if UNITY_EDITOR
                return _tuning == null ||
                       string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(_tuning));
#else
                return _tuning == null;
#endif
            }
        }

        /// <summary>
        /// Reset every value to the shipped defaults.
        ///
        /// <para>Committed as ONE history step over a snapshot of the whole object rather than as
        /// one step per field: an author who resets by accident wants a single Ctrl+Z, not thirty.
        /// </para>
        /// </summary>
        internal void ResetToDefaults()
        {
            if (_tuning == null) return;

            var before = ScriptableObject.CreateInstance<DeathTuning>();
            before.hideFlags = HideFlags.HideAndDontSave;
            CopyTuning(_tuning, before);

            var defaults = ScriptableObject.CreateInstance<DeathTuning>();
            defaults.hideFlags = HideFlags.HideAndDontSave;

            Commit("Restaurados los valores por defecto",
                () => CopyTuning(before, _tuning),
                () => CopyTuning(defaults, _tuning));

            RefreshAll();
        }

        /// <summary>
        /// Field-by-field copy between two tunings.
        ///
        /// <para>Deliberately explicit rather than reflective: a reflective copy would silently
        /// keep working while quietly missing whatever <c>[NonSerialized]</c> or static state a
        /// future field brings, and an undo that restores MOST of a reset is worse than one that
        /// does not compile. <c>DeathTuningFieldCoverageTests</c> fails when a field is added and
        /// not listed here.</para>
        /// </summary>
        private static void CopyTuning(DeathTuning from, DeathTuning to)
        {
            if (from == null || to == null) return;

            to.dyingFlashDuration = from.dyingFlashDuration;
            to.grayscaleFadeIn = from.grayscaleFadeIn;
            to.grayscaleFadeOut = from.grayscaleFadeOut;

            to.spiritSpeedMultiplier = from.spiritSpeedMultiplier;
            to.spiritPassesThroughWalls = from.spiritPassesThroughWalls;
            to.spiritTimeLimitSeconds = from.spiritTimeLimitSeconds;
            to.spiritIsTargetable = from.spiritIsTargetable;

            to.altarTemplateIds = from.altarTemplateIds == null
                ? null : (int[])from.altarTemplateIds.Clone();
            to.altarSearchTimeout = from.altarSearchTimeout;
            to.altarActivationPadding = from.altarActivationPadding;

            to.rescueMode = from.rescueMode;
            to.rescueDelayWithoutAltar = from.rescueDelayWithoutAltar;
            to.rescueHpFraction = from.rescueHpFraction;

            to.pathMode = from.pathMode;
            to.pathUpdateInterval = from.pathUpdateInterval;
            to.pathTint = from.pathTint;
            to.pathMaxMarkers = from.pathMaxMarkers;
            to.showCorpseCompass = from.showCorpseCompass;
            to.corpseTint = from.corpseTint;

            to.dropInventory = from.dropInventory;
            to.dropCoins = from.dropCoins;
            to.coinLossFraction = from.coinLossFraction;
            to.corpseLingerSeconds = from.corpseLingerSeconds;
            to.cleanupPreviousDrops = from.cleanupPreviousDrops;

            to.xpLossFraction = from.xpLossFraction;
            to.xpLossCanDelevel = from.xpLossCanDelevel;
            to.cheatRevivePaysCost = from.cheatRevivePaysCost;

            to.persistDeathState = from.persistDeathState;

            to.sfxDeath = from.sfxDeath;
            to.sfxSpiritEnter = from.sfxSpiritEnter;
            to.sfxRevive = from.sfxRevive;
        }
    }
}
