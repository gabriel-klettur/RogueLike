using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// "Visualizar mapa": walk INTO the previewed world as a floating camera, and come back with Escape
    /// to the game exactly where the player was.
    ///
    /// <para><b>It is the real world, not a picture of it.</b> The view builds the settings into one
    /// reserved live slot (<see cref="SeedWorldLauncher.ViewerSlot"/>) through the same
    /// <see cref="SeedWorldLauncher.BuildAndLoad"/> the "Construir" button runs, so what the author
    /// flies over is what a player would walk. That is also why it obeys the lab switch.</para>
    ///
    /// <para><b>Leaving is a load back, never a teleport.</b> Pepitoria is left through the return
    /// ticket (<see cref="WorldExcursion"/>), any other map through its own file, which records where
    /// the player stood — so "where we were" has one owner each way.</para>
    ///
    /// <para><b>Escape is CLAIMED from the click to the return</b> (<see cref="EscapeOwnership"/>):
    /// the General Editor reads the same press in the same frame and would otherwise close this
    /// editor and open the launcher over a player stranded in the view slot. The load itself runs a
    /// frame after the request, so the overlay can say what is happening before the frame freezes.</para>
    ///
    /// <para><b>The player is borrowed, not owned.</b> They stand at the world's spawn while the
    /// camera flies; spawners near it may fire, so they are made invincible and the flag is restored
    /// exactly as found — <c>SetInvincible</c> has other owners.</para>
    /// </summary>
    public partial class SeedWorldRuntimeEditor
    {
        private const string MapSeedWorld = InputActionCatalog.MapSeedWorldEditor;

        private enum ViewStep { None, Entering, Viewing, Leaving }

        private ViewStep _view = ViewStep.None;
        private int _viewStepFrame;
        private string _viewReturnSlot;
        private float _viewSavedOrtho;

        private Health _viewShieldedHealth;
        private bool _viewHealthWasInvincible;

        private readonly EditorCameraPanController _viewPan = new EditorCameraPanController();
        private readonly EditorCameraZoomController _viewZoom = new EditorCameraZoomController();

        private GameObject _viewOverlay;
        private TextMeshProUGUI _viewTitle;
        private TextMeshProUGUI _viewHelp;
        private Button _viewButton;

        internal bool IsViewing => _view != ViewStep.None;

        // ── Entry ──────────────────────────────────────────────────────────────

        /// <summary>The reason the view cannot open now, or null when it can.</summary>
        internal string ViewRefusal()
        {
            if (!Application.isPlaying) return "Visualizar solo funciona en Play Mode.";
            if (!SeedWorldLab.Enabled) return SeedWorldLab.OffMessage;
            if (MapEditorManager.Instance == null) return "No hay MapEditorManager en esta escena.";
            if (CameraSetup.Instance == null) return "No hay camara de juego.";
            var player = EntityRegistry.Player;
            if (player == null) return "No hay jugador en la escena.";
            var health = player.GetComponent<Health>();
            var spirit = player.GetComponent<PlayerSpiritState>();
            if ((health != null && health.IsDead) || (spirit != null && spirit.IsSpirit))
                return "No se puede visualizar el mapa con el jugador muerto.";
            if (WorldTransitionService.IsBaseWorldContentSuspended)
                return "Sal del interior antes de visualizar el mapa.";
            return null;
        }

        internal void BeginViewing()
        {
            if (IsViewing) return;
            string refusal = ViewRefusal();
            if (refusal != null) { SetStatus(refusal); return; }

            EscapeOwnership.Claim(this);
            _view = ViewStep.Entering;
            _viewStepFrame = Time.frameCount;
            if (_root != null) _root.SetActive(false);
            ShowViewOverlay("Construyendo la vista del mapa...", string.Empty);
        }

        private void EnterViewingNow()
        {
            string refusal = ViewRefusal();
            if (refusal != null) { AbortViewing(refusal); return; }

            var mgr = MapEditorManager.Instance;
            var cam = CameraSetup.Instance;
            string active = mgr.ActiveMapSlot;
            _viewReturnSlot = string.Equals(active, SeedWorldLauncher.ViewerSlot, System.StringComparison.OrdinalIgnoreCase)
                ? MapEditorMapSlots.DEFAULT_SLOT
                : active;
            _viewSavedOrtho = cam.GetCurrentOrthographicSize();

            var outcome = SeedWorldLauncher.BuildAndLoad(_settings, SeedWorldLauncher.ViewerSlot, live: true);
            if (!outcome.Succeeded || !outcome.Loaded)
            {
                // BuildAndLoad may already have left the map (it leaves the slot it rebuilds).
                if (!string.Equals(mgr.ActiveMapSlot, _viewReturnSlot, System.StringComparison.OrdinalIgnoreCase))
                    SeedWorldLauncher.ReturnTo(_viewReturnSlot);
                AbortViewing(outcome.Succeeded ? "No se pudo cargar la vista del mapa." : SeedWorldLauncher.Describe(outcome));
                return;
            }

            // LoadMapSlot re-attaches the camera when it places the player, so detach AFTER it.
            cam.DetachFollow();
            _viewPan.Reset();
            ClaimViewInvulnerability();
            _view = ViewStep.Viewing;
            ShowViewOverlay(ViewTitle(), ViewHelpText);
            Debug.Log($"[SeedWorldEditor] Visualizando semilla {_settings.seed} (volvera a '{_viewReturnSlot}').");
        }

        // ── While viewing ──────────────────────────────────────────────────────

        /// <summary>Drives the view; true while it owns the frame (the editor's own verbs stay off).</summary>
        private bool TickViewer()
        {
            switch (_view)
            {
                case ViewStep.None:
                    return false;
                case ViewStep.Entering:
                    if (Time.frameCount > _viewStepFrame) EnterViewingNow();
                    return true;
                case ViewStep.Leaving:
                    if (Time.frameCount > _viewStepFrame) LeaveViewingNow();
                    return true;
            }

            var mgr = MapEditorManager.Instance;
            var cam = CameraSetup.Instance;
            if (mgr == null || cam == null) { AbortViewing("La vista termino: falta el mundo o la camara."); return true; }

            // Something else loaded another map under the view (the console, a portal): there is no
            // longer anything to return from, only the camera and the flag to hand back.
            if (!string.Equals(mgr.ActiveMapSlot, SeedWorldLauncher.ViewerSlot, System.StringComparison.OrdinalIgnoreCase))
            {
                AbortViewing("La vista termino: se cargo otro mapa.");
                return true;
            }

            if (EditorInput.ClosePressed())
            {
                _view = ViewStep.Leaving;
                _viewStepFrame = Time.frameCount;
                ShowViewOverlay("Volviendo...", string.Empty);
                return true;
            }

            cam.DetachFollow();
            _viewPan.Tick();
            _viewZoom.Tick();
            if (cam.GetCurrentOrthographicSize() > SeedWorldViewerFlight.MaxOrthoSize)
                cam.SetEditorZoom(SeedWorldViewerFlight.MaxOrthoSize);

            var t = cam.GetDetachedTransform();
            if (t == null) return true;

            var dir = SeedWorldViewerFlight.Direction(
                EditorInput.ToolHeld(MapSeedWorld, "FlyUp"),
                EditorInput.ToolHeld(MapSeedWorld, "FlyDown"),
                EditorInput.ToolHeld(MapSeedWorld, "FlyLeft"),
                EditorInput.ToolHeld(MapSeedWorld, "FlyRight"));
            bool fast = EditorInput.ToolHeld(MapSeedWorld, "FlyFast");

            Vector2 p = t.position;
            p = SeedWorldViewerFlight.Step(p, dir, cam.GetCurrentOrthographicSize(), fast, Time.unscaledDeltaTime);
            p = SeedWorldViewerFlight.ClampToWorld(p, ViewWorldRect());
            t.position = new Vector3(p.x, p.y, t.position.z);

            if (_viewTitle != null) _viewTitle.text = ViewTitle(p);
            return true;
        }

        private static Rect ViewWorldRect()
        {
            var world = SeedWorldLiveStreamer.Instance != null ? SeedWorldLiveStreamer.Instance.World : null;
            if (world == null) return default;
            var plan = world.Plan;
            return new Rect(plan.Origin.x, plan.Origin.y, plan.BuiltTilesW, plan.BuiltTilesH);
        }

        // ── Exit ───────────────────────────────────────────────────────────────

        private void LeaveViewingNow()
        {
            string message = SeedWorldLauncher.ReturnTo(_viewReturnSlot);
            FinishViewing();
            Debug.Log("[SeedWorldEditor] " + message);
            SetStatus(message);
            // Back to the GAME where we were, not to the editor: the editor remembers its settings.
            Deactivate();
        }

        /// <summary>End the view without loading anything, and show the editor again with why.</summary>
        private void AbortViewing(string reason)
        {
            FinishViewing();
            if (_active && _root != null) _root.SetActive(true);
            SetStatus(reason);
        }

        /// <summary>Everything the view borrowed, handed back. Safe to call in any state.</summary>
        private void FinishViewing()
        {
            bool wasOpen = _view != ViewStep.None;
            _view = ViewStep.None;
            _viewPan.Reset();
            ReleaseViewInvulnerability();
            if (_viewOverlay != null) _viewOverlay.SetActive(false);
            if (!wasOpen) return;

            var cam = CameraSetup.Instance;
            if (cam != null)
            {
                cam.ReattachFollow();
                if (_viewSavedOrtho > 0f) cam.SetEditorZoom(_viewSavedOrtho);
            }
            EscapeOwnership.Release(this);
        }

        /// <summary>
        /// The editor is being closed by somebody else while the view is up: take the player home
        /// first, so nothing leaves them standing in the view slot.
        /// </summary>
        private void CloseViewForDeactivate()
        {
            if (!IsViewing) return;
            bool entered = _view == ViewStep.Viewing || _view == ViewStep.Leaving;
            if (entered && Application.isPlaying) SeedWorldLauncher.ReturnTo(_viewReturnSlot);
            FinishViewing();
        }

        // ── Invincibility (borrowed) ───────────────────────────────────────────

        private void ClaimViewInvulnerability()
        {
            var player = EntityRegistry.Player;
            var health = player != null ? player.GetComponent<Health>() : null;
            if (health == null) return;
            _viewShieldedHealth = health;
            _viewHealthWasInvincible = health.IsInvincible;
            health.SetInvincible(true);
        }

        private void ReleaseViewInvulnerability()
        {
            if (_viewShieldedHealth != null) _viewShieldedHealth.SetInvincible(_viewHealthWasInvincible);
            _viewShieldedHealth = null;
            _viewHealthWasInvincible = false;
        }

        // ── UI ─────────────────────────────────────────────────────────────────

        internal const string ViewHelpText =
            "WASD / flechas: mover  |  Shift: rapido  |  rueda: zoom  |  boton central: arrastrar  |  Esc: volver al juego";

        private string ViewTitle() => $"VISUALIZANDO MAPA  -  semilla {_settings.seed}";

        private string ViewTitle(Vector2 at)
            => $"VISUALIZANDO MAPA  -  semilla {_settings.seed}  -  ({Mathf.RoundToInt(at.x)}, {Mathf.RoundToInt(at.y)})";

        private void BuildViewRow(Transform parent)
        {
            var row = EditorUIHelpers.CreateUI("ViewRow", parent);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            le.minHeight = 28f;
            le.flexibleHeight = 0f;

            _viewButton = EditorUIHelpers.MakeButton(row.transform, "Visualizar mapa", BeginViewing, 28f, 12f);
            _viewButton.gameObject.name = "ViewMapButton";
            var buttonLe = _viewButton.GetComponent<LayoutElement>() ?? _viewButton.gameObject.AddComponent<LayoutElement>();
            buttonLe.preferredWidth = 170f;
            buttonLe.flexibleWidth = 0f;

            var hint = EditorUIHelpers.AddLabel(row.transform, "Recorre este mundo con camara libre. Esc vuelve al juego.", 10f);
            hint.color = EditorUIHelpers.TEXT_MUTED;
            hint.raycastTarget = false;
            var hintLe = hint.gameObject.GetComponent<LayoutElement>() ?? hint.gameObject.AddComponent<LayoutElement>();
            hintLe.flexibleWidth = 1f;
            RefreshViewButton();
        }

        /// <summary>The button follows the lab switch, which lives in the other panel.</summary>
        private void RefreshViewButton()
        {
            if (_viewButton == null) return;
            bool lab = SeedWorldLab.Enabled;
            _viewButton.interactable = lab;
            UIButton.SetTint(_viewButton, lab ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL);
        }

        private void ShowViewOverlay(string title, string help)
        {
            if (_canvas == null) return;
            if (_viewOverlay == null) BuildViewOverlay();
            _viewOverlay.SetActive(true);
            _viewOverlay.transform.SetAsLastSibling();
            _viewTitle.text = title;
            _viewHelp.text = help;
        }

        private void BuildViewOverlay()
        {
            _viewOverlay = EditorUIHelpers.CreateUI("ViewOverlay", _canvas.transform);
            var rt = _viewOverlay.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -16f);
            rt.sizeDelta = new Vector2(820f, 52f);

            // Nothing on it is a raycast target: the wheel and the middle button must work over it.
            var bg = _viewOverlay.AddComponent<Image>();
            bg.color = EditorUIHelpers.BG_PANEL;
            bg.raycastTarget = false;

            _viewTitle = MakeOverlayLine("Title", 13f, EditorUIHelpers.TEXT_PRIMARY, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
            _viewHelp = MakeOverlayLine("Help", 10f, EditorUIHelpers.TEXT_SECONDARY, new Vector2(0f, 0f), new Vector2(1f, 0.5f));
        }

        private TextMeshProUGUI MakeOverlayLine(string name, float size, Color color, Vector2 min, Vector2 max)
        {
            var go = EditorUIHelpers.CreateUI(name, _viewOverlay.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-10f, 0f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
