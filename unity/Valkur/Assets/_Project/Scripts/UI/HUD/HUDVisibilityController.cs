using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Hides every gameplay HUD element whenever any runtime editor opens, and
    /// restores them when all editors close. Drives off
    /// <see cref="GameEditorManager.OnEditorStateChanged"/> so it stays in sync
    /// with the same exclusivity manager that already coordinates editor open/close.
    ///
    /// Must live OUTSIDE <c>[UI]</c> (it's hosted on <c>[Systems]/HUDBootstrap</c>)
    /// so this component itself doesn't get disabled when the HUD container is
    /// hidden — otherwise OnDisable would unsubscribe and nothing would restore
    /// the HUD on editor close.
    /// </summary>
    public sealed class HUDVisibilityController : MonoBehaviour
    {
        // Canvases that the music / toast systems sometimes self-parent to at
        // scene root (because their owning GameObject couldn't find a Canvas in
        // its parent chain). Listed by name so we can hide them alongside [UI].
        private static readonly string[] RootHUDCanvasNames =
        {
            "MusicHUDCanvas",
            "ToastCanvas",
        };

        // Tracks every GameObject we toggled off so Show() can restore exactly
        // those — never more, never less. Skipping objects that were already
        // inactive avoids re-enabling something the gameplay layer had hidden
        // for its own reasons.
        //
        // WHAT THIS MUST NOT HIDE: the diagnostic overlays. They exist to be read WHILE a
        // runtime editor is open, which is precisely when this controller fires — so the
        // Debug HUD lives under [Diagnostics], SaveTelemetryHUD at scene root, and
        // CombatRangeVisualizer draws in world space. Anything parented under [UI] goes dark
        // with the rest of the HUD, and the failure is silent: the toggle flips, nothing
        // appears, and the button even lights up because IsVisible went true.
        private readonly List<GameObject> _hiddenByThisController = new List<GameObject>();
        private bool _hudHidden;

        private void OnEnable()
        {
            GameEditorManager.OnEditorStateChanged += HandleEditorStateChanged;
            // If a runtime editor opened before this controller existed (race
            // during scene load), align the HUD with the current state.
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive)
                Hide();
        }

        private void OnDisable()
        {
            GameEditorManager.OnEditorStateChanged -= HandleEditorStateChanged;
            // Defensive: if we were destroyed while the HUD was hidden, restore
            // it so a fresh play session doesn't inherit an invisible HUD.
            if (_hudHidden) Show();
        }

        private void HandleEditorStateChanged(bool editorOpen)
        {
            if (editorOpen) Hide();
            else Show();
        }

        private void Hide()
        {
            if (_hudHidden) return;
            _hudHidden = true;
            _hiddenByThisController.Clear();

            var uiContainer = GameObject.Find("[UI]");
            if (uiContainer != null && uiContainer.activeSelf)
            {
                uiContainer.SetActive(false);
                _hiddenByThisController.Add(uiContainer);
            }

            foreach (var rootName in RootHUDCanvasNames)
            {
                var go = GameObject.Find(rootName);
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    _hiddenByThisController.Add(go);
                }
            }
        }

        private void Show()
        {
            if (!_hudHidden) return;
            _hudHidden = false;
            foreach (var go in _hiddenByThisController)
            {
                if (go != null) go.SetActive(true);
            }
            _hiddenByThisController.Clear();
        }
    }
}
