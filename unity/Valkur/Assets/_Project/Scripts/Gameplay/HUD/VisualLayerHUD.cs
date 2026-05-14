using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Tiny top-left HUD readout for the per-entity visual layer system. Shows two
    /// independent lines:
    ///   • "Logical: 0 — Ground"     ← from the player's <see cref="VisualLayerOccupant"/>.
    ///                                  This is the source of truth M2 will use
    ///                                  for per-layer collision filtering.
    ///   • "Underfoot: 0,5"          ← from <see cref="VisualLayerProbe.Sample"/>:
    ///                                  every visual layer that currently has a
    ///                                  non-empty tile at the player's position.
    ///
    /// The two lines surface the layered-world model so a designer can see at a
    /// glance whether the player's logical layer matches what the world has under
    /// them (mismatch = bug or stale layer assignment).
    ///
    /// Bootstraps itself via <see cref="RuntimeInitializeOnLoadMethod"/> on
    /// <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/> so no scene wiring
    /// is required — drop the assembly in and a corner readout appears.
    /// </summary>
    public sealed class VisualLayerHUD : SingletonMonoBehaviour<VisualLayerHUD>
    {
        protected override bool Persist => false;

        // Polling cadence — the underfoot probe samples 9 tilemaps so we don't run
        // it every frame at 60 Hz. Half a second is plenty for a debug readout.
        private const float PollInterval = 0.5f;

        private VisualLayerOccupant _player;
        private WorldGridBuilder _grid;
        private Canvas _canvas;
        private GameObject _root;
        private Text _textLabel;
        private float _nextPollTime;

        // Reused buffer for the underfoot probe so the per-tick allocation is zero.
        private readonly bool[] _underfootScratch = new bool[9];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterSceneLoad()
        {
            // No-op when the HUD is already in the scene (avoids the duplicate-
            // singleton destroy log). Otherwise spawn a host GameObject so the
            // readout appears in any gameplay scene without manual wiring.
            if (HasInstance) return;
            var go = new GameObject(nameof(VisualLayerHUD));
            go.AddComponent<VisualLayerHUD>();
        }

        private void Start()
        {
            EnsureBuilt();
            // Rebind every poll cycle so the HUD survives scene transitions /
            // player respawn without manual wiring.
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + PollInterval;

            ResolveRefs();
            Refresh();
        }

        private void ResolveRefs()
        {
            // Cheap re-resolve: FindObjectOfType is O(N) over MonoBehaviours but
            // we only run it twice per second and only when the cached ref is
            // null (e.g. between Player spawns).
            if (_player == null)
                _player = FindObjectOfType<VisualLayerOccupant>();
            if (_grid == null)
                _grid = FindObjectOfType<WorldGridBuilder>();
        }

        private void Refresh()
        {
            if (_textLabel == null) return;

            if (_player == null)
            {
                _textLabel.text = "Layer: (no player)";
                return;
            }

            var sb = new StringBuilder(64);
            sb.Append("Layer: ").Append(_player.CurrentVisualLayer)
              .Append(" — ").Append(_player.LayerName);

            if (_grid != null)
            {
                int populated = VisualLayerProbe.Sample(_player.transform.position, _grid, _underfootScratch);
                sb.Append('\n').Append("Underfoot: ");
                if (populated == 0)
                {
                    sb.Append("(none)");
                }
                else
                {
                    bool first = true;
                    for (int i = 0; i < _underfootScratch.Length; i++)
                    {
                        if (!_underfootScratch[i]) continue;
                        if (!first) sb.Append(", ");
                        sb.Append(i);
                        first = false;
                    }
                }
            }

            _textLabel.text = sb.ToString();
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;

            var canvasGo = new GameObject("VisualLayerHUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 250; // below modal panels (300+) but above world UI.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta = new Vector2(260f, 40f);

            // Translucent dark background so white text stays legible over varied terrain.
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;

            var labelGo = new GameObject("Lbl");
            labelGo.transform.SetParent(_root.transform, false);
            var lblRt = labelGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(6f, 4f);
            lblRt.offsetMax = new Vector2(-6f, -4f);
            _textLabel = labelGo.AddComponent<Text>();
            _textLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textLabel.fontSize = 14;
            _textLabel.alignment = TextAnchor.UpperLeft;
            _textLabel.color = Color.white;
            _textLabel.raycastTarget = false;
            _textLabel.text = "Layer: …";
        }
    }
}
