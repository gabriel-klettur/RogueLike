using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.Gameplay.UIKit
{
    /// <summary>
    /// Persistent toolbar of HUD-toggle buttons.
    ///
    /// Design (deliberately minimal):
    ///   • The container is parented under the SAME canvas as MusicPlayerHUD
    ///     (`MusicHUDCanvas`) and is a SIBLING of the music widget. Same
    ///     canvas ⇒ same scaler ⇒ no size-conversion math.
    ///   • Buttons sit immediately to the LEFT of the music pill, sharing
    ///     the same bottom Y, stacked horizontally.
    ///   • Each button is a SINGLE Image whose sprite IS the entire button
    ///     graphic (the asset already contains its stone frame + icon).
    ///     There is no inner "icon over frame" composition — the sprite is
    ///     the button.
    ///   • Buttons are PERSISTENT. Clicking just invokes `onToggle`; the
    ///     owner HUD decides whether to show or hide itself.
    /// </summary>
    public class MinimizedHUDTray : SingletonMonoBehaviour<MinimizedHUDTray>
    {
        // ── Visual constants ────────────────────────────────────────────────
        private const float TRAY_GAP    = 8f;   // gap to the music pill
        private const float BUTTON_SIZE = 36f;  // matches MusicPlayerHUD MinimizedW/H
        private const float BUTTON_GAP  = 4f;   // spacing between buttons

        // ── Refs ────────────────────────────────────────────────────────────
        private RectTransform _containerRt;
        private RectTransform _musicRt;
        private float         _resolveTimer;
        private const float   RESOLVE_RETRY = 1.0f;

        // ── Entries ─────────────────────────────────────────────────────────
        private class Entry
        {
            public string         Id;
            public Sprite         Sprite;
            public System.Action  OnToggle;
            public GameObject     Go;
        }
        private readonly List<Entry>               _order   = new List<Entry>();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        protected override void OnSingletonAwake() { /* lazy: built when music widget appears */ }

        // ─────────────────────────────────────────────────────────────────────
        //  Per-frame: resolve music widget, build container, pin position.
        // ─────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_musicRt == null)
            {
                _resolveTimer -= Time.unscaledDeltaTime;
                if (_resolveTimer <= 0f)
                {
                    TryResolveMusicRT();
                    _resolveTimer = RESOLVE_RETRY;
                }
                if (_musicRt == null) return;
            }

            EnsureContainerBuilt();
            MaterialisePendingButtons();

            if (_containerRt != null && _containerRt.gameObject.activeSelf)
                PinContainerToMusicLeft();
        }

        private void TryResolveMusicRT()
        {
            var canvasGo = GameObject.Find("MusicHUDCanvas");
            if (canvasGo != null && canvasGo.transform.childCount > 0)
            {
                _musicRt = canvasGo.transform.GetChild(0) as RectTransform;
                if (_musicRt != null) return;
            }

            // Fallback: scene-mounted MusicPlayerHUD by type-name
            // (avoids a Gameplay → UI assembly reference).
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                if (mb.GetType().Name == "MusicPlayerHUD")
                {
                    _musicRt = mb.GetComponent<RectTransform>();
                    return;
                }
            }
        }

        private void EnsureContainerBuilt()
        {
            if (_containerRt != null) return;
            if (_musicRt == null) return;

            var parent = _musicRt.parent as RectTransform;
            if (parent == null) return;

            var go = new GameObject("MinimizedHUDTrayContainer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _containerRt = (RectTransform)go.transform;
            _containerRt.anchorMin = _musicRt.anchorMin;
            _containerRt.anchorMax = _musicRt.anchorMax;
            _containerRt.pivot     = new Vector2(1f, 0f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset(0, 0, 0, 0);
            hlg.spacing                = BUTTON_GAP;
            hlg.childAlignment         = TextAnchor.LowerRight;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            UpdateContainerVisibility();
        }

        private void MaterialisePendingButtons()
        {
            if (_containerRt == null) return;
            bool changed = false;
            foreach (var e in _order)
            {
                if (e.Go != null) continue;
                e.Go = BuildButton(e);
                changed = true;
            }
            if (changed) UpdateContainerVisibility();
        }

        private void PinContainerToMusicLeft()
        {
            // Both RTs live in the same parent ⇒ same local space.
            // Music has pivot=(1,0); the tray's right edge sits TRAY_GAP px
            // to the left of music's left edge, sharing the bottom Y.
            Vector2 musicAnchored = _musicRt.anchoredPosition;
            float   musicWidth    = _musicRt.rect.width;
            float   leftOfMusicX  = musicAnchored.x - musicWidth - TRAY_GAP;
            _containerRt.anchoredPosition = new Vector2(leftOfMusicX, musicAnchored.y);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Register (or update) a persistent toolbar button. The button stays
        /// in the tray for the rest of the session; clicks invoke
        /// <paramref name="onToggle"/> so the owner HUD can show/hide itself.
        /// </summary>
        public void Register(string id, Sprite sprite, System.Action onToggle)
        {
            if (string.IsNullOrEmpty(id)) return;

            if (_entries.TryGetValue(id, out var existing))
            {
                // Update sprite + callback in place — do NOT re-create.
                existing.Sprite   = sprite;
                existing.OnToggle = onToggle;
                if (existing.Go != null)
                {
                    var img = existing.Go.GetComponent<Image>();
                    if (img != null) img.sprite = sprite;
                    var btn = existing.Go.GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        var cb = onToggle;
                        btn.onClick.AddListener(() => cb?.Invoke());
                    }
                }
                return;
            }

            var entry = new Entry { Id = id, Sprite = sprite, OnToggle = onToggle };
            _order.Add(entry);
            _entries[id] = entry;

            if (_containerRt != null)
            {
                entry.Go = BuildButton(entry);
                UpdateContainerVisibility();
            }
            // else: deferred — built next LateUpdate when container is ready.
        }

        public void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_entries.TryGetValue(id, out var entry)) return;
            if (entry.Go != null) SafeDestroy(entry.Go);
            _entries.Remove(id);
            _order.Remove(entry);
            UpdateContainerVisibility();
        }

        public bool IsRegistered(string id)
            => !string.IsNullOrEmpty(id) && _entries.ContainsKey(id);

        public int Count => _order.Count;

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateContainerVisibility()
        {
            if (_containerRt == null) return;
            _containerRt.gameObject.SetActive(_order.Count > 0);
        }

        private static void SafeDestroy(Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(obj); return; }
#endif
            Destroy(obj);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Button builder — single Image = the entire button sprite.
        // ─────────────────────────────────────────────────────────────────────

        private GameObject BuildButton(Entry entry)
        {
            var go = new GameObject($"TrayBtn_{entry.Id}", typeof(RectTransform));
            go.transform.SetParent(_containerRt, false);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(BUTTON_SIZE, BUTTON_SIZE);

            // The sprite IS the button — no inner frame, no inner icon.
            var img = go.AddComponent<Image>();
            img.sprite         = entry.Sprite;
            img.preserveAspect = true;
            img.color          = Color.white;
            if (img.sprite == null)
                img.color = new Color(0.18f, 0.18f, 0.22f, 1f); // visible fallback

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor      = Color.white;
            c.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            c.pressedColor     = new Color(0.78f, 0.78f, 0.78f, 1f);
            c.selectedColor    = Color.white;
            btn.colors = c;

            var cb = entry.OnToggle;
            btn.onClick.AddListener(() => cb?.Invoke());

            // If no sprite was supplied, render a fallback letter so the
            // button is identifiable in headless / asset-missing scenarios.
            if (entry.Sprite == null)
            {
                var lbl = new GameObject("Label", typeof(RectTransform));
                lbl.transform.SetParent(rt, false);
                var lrt = (RectTransform)lbl.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                var tmp = lbl.AddComponent<TextMeshProUGUI>();
                tmp.text          = (entry.Id ?? "?").Substring(0, 1).ToUpperInvariant();
                tmp.fontSize      = 18f;
                tmp.fontStyle     = FontStyles.Bold;
                tmp.alignment     = TextAlignmentOptions.Center;
                tmp.color         = new Color(0.90f, 0.76f, 0.38f, 1f);
                tmp.raycastTarget = false;
            }

            return go;
        }
    }
}
