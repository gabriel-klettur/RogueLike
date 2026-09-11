using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay.Editors.Quests
{
    /// <summary>
    /// Runtime Quests editor, opened from the General Editor (ESC → Misiones).
    ///
    /// <para><b>WHAT IT IS FOR.</b> Ten quests ship and every one of them is reached the same
    /// way: find the character who gives it, hold a conversation, accept, then play the errand
    /// to its end. That is the right experience for a player and a terrible loop for anybody
    /// checking whether a quest WORKS — the 2026-09-08 audit measured two of the ten as flatly
    /// unreachable in play (their giver is a persona no spawner instantiates), and nothing in
    /// the game could have told you: no offer, no badge, no marker, exactly as an errand nobody
    /// has walked up to yet.</para>
    ///
    /// <para>So this is the one screen that lists ALL of them at once — active, finished, on
    /// offer, and locked with the reason — and can move any of them: accept, drop, drive an
    /// objective to any value, or forget a completion so a chain can be watched twice. Every
    /// one of those goes through the SAME seam the game uses, which is the whole point:
    /// completing a quest here pays its rewards through <c>QuestManager</c>'s one completion
    /// path, so what an author sees is what a player would get rather than a simulation of
    /// it.</para>
    ///
    /// <para><b>It edits the RUN, not an asset.</b> Unlike the Death or Economy editors there is
    /// no SAVE button and no <c>.asset</c> written: a quest log is save state, and the shipped
    /// definitions are authored in <c>Data/Catalogs/Quests/</c>. What it changes lands in the
    /// save the next time one is written, which is why every destructive action here confirms
    /// first.</para>
    ///
    /// <para><b>NO HOTKEY</b>, like the Camera, Controls, Skills, Economy and Death editors: the
    /// F-row is retired project-wide and the General Editor is the only way in, which is what
    /// <c>EditorReachabilityTests</c> pins. <b>It declares no input actions of its own</b>, so
    /// there is no <c>OwnerEditor</c> anywhere in this folder — every verb it uses is a button,
    /// and the shared ones come from <c>EditorShared</c>.</para>
    /// </summary>
    public sealed partial class QuestsRuntimeEditor : SingletonMonoBehaviour<QuestsRuntimeEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        /// <summary>Which slice of the catalogue the list is showing.</summary>
        internal enum Tab
        {
            Active = 0,
            Available = 1,
            Locked = 2,
            Completed = 3,
        }

        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;

        private Tab _tab = Tab.Active;
        private string _selectedId;

        /// <summary>
        /// The quest whose destructive button is armed, if any. Two clicks, exclusive, and
        /// cleared whenever the list is rebuilt for another reason — the same contract the
        /// conversation panel and the tracker follow, because this is the surface where a
        /// misclick costs an author the run they were half way through testing.
        /// </summary>
        private string _armedDangerId;

        private readonly List<QuestDefinition> _scratch = new List<QuestDefinition>();

        /// <summary>
        /// The EXACT string the General Editor opens this editor by, and the string inside the
        /// input context id. It is not the folder name and it is not the map slug — a mismatch
        /// there once silently killed all 35 tools of another editor.
        /// </summary>
        public string EditorName => "Misiones";

        public bool IsActive => _active;

        internal Tab ActiveTab => _tab;
        internal string SelectedId => _selectedId;

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
            if (!_uiBuilt)
            {
                // Wrapped because a BuildUI that throws half way leaves an editor registered,
                // inactive and impossible to open again, with the exception the only clue. The
                // Camera, Skills, Economy and Death editors carry the same guard.
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[QuestsEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            _armedDangerId = null;
            if (_root != null) _root.SetActive(true);
            ForcePanelsOpen();
            RefreshAll();
        }

        public void Deactivate()
        {
            _active = false;
            _armedDangerId = null;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // ── Model access ───────────────────────────────────────────────────────

        private static QuestService Service => QuestService.Instance;

        private static QuestManager Manager
        {
            get
            {
                var s = QuestService.Instance;
                return s != null ? s.Manager : UnityEngine.Object.FindObjectOfType<QuestManager>();
            }
        }

        /// <summary>
        /// The quests in the tab being shown.
        ///
        /// <para>Read from the live manager and the shipped catalogue on every call rather than
        /// cached: this editor sits over a running game, and a quest can complete itself while
        /// the panel is open — a polled objective needs nobody to be standing anywhere.</para>
        /// </summary>
        internal List<QuestDefinition> QuestsInTab()
        {
            _scratch.Clear();

            var mgr = Manager;
            var svc = Service;
            if (mgr == null || svc == null) return _scratch;

            int level = QuestService.PlayerLevel();

            foreach (var def in svc.Definitions)
            {
                if (def == null || string.IsNullOrEmpty(def.questId)) continue;

                bool isActive = mgr.IsActive(def.questId);
                bool isDone   = mgr.IsCompleted(def.questId);

                switch (_tab)
                {
                    case Tab.Active:
                        if (isActive) _scratch.Add(def);
                        break;
                    case Tab.Completed:
                        if (isDone) _scratch.Add(def);
                        break;
                    case Tab.Available:
                        if (mgr.IsEligible(def, level)) _scratch.Add(def);
                        break;
                    case Tab.Locked:
                        // Not eligible AND not already taken or finished — i.e. something the
                        // player has yet to earn, which is a different statement from
                        // "already dealt with" and the reason this cannot just be !IsEligible.
                        if (!isActive && !isDone && !mgr.IsEligible(def, level)) _scratch.Add(def);
                        break;
                }
            }

            return _scratch;
        }

        internal QuestDefinition Selected()
        {
            var svc = Service;
            if (svc == null || string.IsNullOrEmpty(_selectedId)) return null;
            foreach (var def in svc.Definitions)
                if (def != null && string.Equals(def.questId, _selectedId, StringComparison.OrdinalIgnoreCase))
                    return def;
            return null;
        }

        // ── Actions ────────────────────────────────────────────────────────────

        internal void SelectQuest(string questId)
        {
            _selectedId = questId;
            _armedDangerId = null;
            RefreshAll();
        }

        internal void SetTab(Tab tab)
        {
            _tab = tab;
            _armedDangerId = null;
            RefreshAll();
        }

        /// <summary>
        /// Accepts a quest, bypassing the conversation.
        ///
        /// <para>Through <c>QuestManager.StartQuest</c> rather than <c>QuestService.TryAccept</c>
        /// deliberately: TryAccept re-checks eligibility, which is right for a button a player
        /// presses and wrong for the one screen whose job is to reach a quest whose giver is
        /// not in the world. The eligibility answer is still SHOWN — it is what the Locked tab
        /// lists and what the detail panel spells out — so forcing one is a decision rather
        /// than an accident.</para>
        /// </summary>
        internal void AcceptSelected()
        {
            var def = Selected();
            var mgr = Manager;
            if (def == null || mgr == null) return;

            if (mgr.StartQuest(def)) SetStatus($"Aceptada '{def.displayName}'.");
            else SetStatus($"No se pudo aceptar '{def.displayName}' (ya activa o completada).");
            RefreshAll();
        }

        /// <summary>Drops the selected quest. Two clicks — see <see cref="_armedDangerId"/>.</summary>
        internal void DropSelected()
        {
            var def = Selected();
            var mgr = Manager;
            if (def == null || mgr == null) return;

            if (!string.Equals(_armedDangerId, def.questId, StringComparison.OrdinalIgnoreCase))
            {
                _armedDangerId = def.questId;
                SetStatus($"Pulsa otra vez para soltar '{def.displayName}'.");
                RefreshAll();
                return;
            }

            _armedDangerId = null;
            mgr.AbandonQuest(def.questId);
            SetStatus($"Soltada '{def.displayName}'.");
            RefreshAll();
        }

        /// <summary>
        /// Takes a quest back out of the completed set so it can be played again. Two clicks.
        /// </summary>
        internal void ForgetSelected()
        {
            var def = Selected();
            var mgr = Manager;
            if (def == null || mgr == null) return;

            if (!string.Equals(_armedDangerId, def.questId, StringComparison.OrdinalIgnoreCase))
            {
                _armedDangerId = def.questId;
                SetStatus($"Pulsa otra vez para olvidar '{def.displayName}'.");
                RefreshAll();
                return;
            }

            _armedDangerId = null;
            if (mgr.ForgetCompleted(def.questId)) SetStatus($"Olvidada '{def.displayName}'. Vuelve a estar disponible.");
            else SetStatus("No estaba completada.");
            RefreshAll();
        }

        /// <summary>
        /// Drives one objective of the selected quest to a value, through the same event the
        /// world raises — so a quest driven to its target here COMPLETES, pays out and leaves
        /// the log exactly as it would have.
        /// </summary>
        internal void SetObjectiveProgress(int index, int value)
        {
            var mgr = Manager;
            var def = Selected();
            if (mgr == null || def == null) return;

            var quest = mgr.GetActiveQuest(def.questId);
            if (quest == null || index < 0 || index >= quest.Objectives.Count) return;

            if (quest.Objectives[index] is ObjectiveBase ob)
            {
                ob.ForceProgress(value);
                SetStatus($"'{ob.Description}' -> {ob.Current}/{ob.Target}.");
            }
            else
            {
                SetStatus("Ese objetivo no es ajustable.");
            }

            RefreshAll();
        }

        /// <summary>Fills every objective, which finishes the quest by the ordinary path.</summary>
        internal void CompleteSelected()
        {
            var mgr = Manager;
            var def = Selected();
            if (mgr == null || def == null) return;

            var quest = mgr.GetActiveQuest(def.questId);
            if (quest == null) return;

            // Snapshot: filling the last objective completes the quest, and completion mutates
            // the manager's active table while this loop is reading the quest that lives in it.
            var objectives = new List<IObjective>(quest.Objectives);
            for (int i = 0; i < objectives.Count; i++)
                if (objectives[i] is ObjectiveBase ob) ob.ForceProgress(ob.Target);

            SetStatus($"Completada '{def.displayName}'.");
            RefreshAll();
        }

        // ── The tracker window ─────────────────────────────────────────────────

        /// <summary>
        /// The Quests editor is the reliable way back to a tracker the player closed, which is
        /// the other half of that window having a close button at all.
        /// </summary>
        internal static QuestLogHUD Tracker => QuestLogHUD.Instance;

        internal void ToggleTracker()
        {
            var hud = Tracker;
            if (hud == null) { SetStatus("No hay seguidor en esta escena."); return; }
            hud.ToggleClosed();
            SetStatus(hud.IsWindowVisible ? "Seguidor visible." : "Seguidor oculto.");
            RefreshAll();
        }

        internal void ToggleTrackerMinimized()
        {
            var hud = Tracker;
            if (hud == null) { SetStatus("No hay seguidor en esta escena."); return; }
            hud.ToggleMinimized();
            SetStatus(hud.IsMinimized ? "Seguidor minimizado." : "Seguidor desplegado.");
            RefreshAll();
        }
    }
}
