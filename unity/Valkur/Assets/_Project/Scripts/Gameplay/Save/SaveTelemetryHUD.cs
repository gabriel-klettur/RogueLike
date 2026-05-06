using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Floating diagnostics panel that lists the last <see cref="SaveTelemetry.Capacity"/>
    /// save events. Spawned on demand from the General Editor (ESC →
    /// Diagnostics → Save Log). Refreshes itself when new entries arrive
    /// via <see cref="SaveTelemetry.OnEntryRecorded"/> rather than polling
    /// every frame.
    /// </summary>
    public class SaveTelemetryHUD : MonoBehaviour
    {
        public static SaveTelemetryHUD Instance { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            // Clear stale delegates that survived a domain-reload-OFF second Play.
            SaveTelemetry.ClearEntryRecordedListeners();
        }

        // Match the General Editor's Diagnostics palette.
        private static readonly Color PanelBg     = new Color(22f/255f, 24f/255f, 28f/255f, 235f/255f);
        private static readonly Color OverlayBg   = new Color(0f, 0f, 0f, 180f/255f);
        private static readonly Color RowBg       = new Color(0.13f, 0.14f, 0.18f, 1f);
        private static readonly Color RowBgFail   = new Color(0.30f, 0.10f, 0.10f, 1f);
        private static readonly Color BtnNormal   = new Color(0.22f, 0.24f, 0.30f, 1f);
        private static readonly Color BtnHover    = new Color(0.30f, 0.32f, 0.40f, 1f);
        private static readonly Color TextPrimary = new Color(230f/255f, 233f/255f, 240f/255f, 1f);
        private static readonly Color TextDim     = new Color(0.60f, 0.65f, 0.72f, 1f);
        private static readonly Color Accent      = new Color(255f/255f, 200f/255f,   0f/255f, 1f);

        // ── Static toggle API ────────────────────────────────────────────────────

        public static void Toggle()
        {
            if (Instance == null) Open();
            else                  Instance.Close();
        }

        public static SaveTelemetryHUD Open()
        {
            if (Instance != null) { Instance.Show(); return Instance; }
            var go = new GameObject(nameof(SaveTelemetryHUD));
            DontDestroyOnLoad(go);
            var hud = go.AddComponent<SaveTelemetryHUD>();
            hud.Show();
            return hud;
        }

        // ── Build & lifecycle ────────────────────────────────────────────────────

        private GameObject     _root;
        private RectTransform  _content;
        private TextMeshProUGUI _summary;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            SaveTelemetry.OnEntryRecorded += HandleEntryRecorded;
            _root.SetActive(false);
        }

        private void OnDestroy()
        {
            SaveTelemetry.OnEntryRecorded -= HandleEntryRecorded;
            if (Instance == this) Instance = null;
        }

        private void Show()
        {
            _root.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;
            // ESC closes — sits on top of the General Editor so it owns input.
            if (KeyboardInputManager.WasEscapePressedThisFrame()) Close();
        }

        // ── Refresh ──────────────────────────────────────────────────────────────

        private void HandleEntryRecorded(SaveTelemetryEntry _)
        {
            // Recorded entries can arrive on a thread-pool thread (async writes).
            // Defer the UI refresh to the main thread by checking activeInHierarchy
            // on the next Update tick instead. Cheap fallback: just re-snapshot
            // from main-thread events too.
            if (this == null) return;
            if (_root != null && _root.activeInHierarchy)
                _needsRefresh = true;
        }

        private bool _needsRefresh;

        private void LateUpdate()
        {
            if (!_needsRefresh) return;
            _needsRefresh = false;
            Refresh();
        }

        private void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            var entries = SaveTelemetry.Snapshot();
            int total   = SaveTelemetry.TotalRecorded;
            if (_summary != null)
                _summary.text = $"Last {entries.Count} saves shown — total recorded since launch: {total}";

            // Newest first.
            for (int i = entries.Count - 1; i >= 0; i--)
                AddRow(entries[i]);
        }

        private void AddRow(SaveTelemetryEntry e)
        {
            var rowGo = new GameObject($"Row_{e.Timestamp:HHmmss}_{e.Kind}", typeof(RectTransform));
            rowGo.transform.SetParent(_content, false);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 38f;
            var bg = rowGo.AddComponent<Image>();
            bg.color = e.Success ? RowBg : RowBgFail;

            var text = AddText(rowGo.transform, BuildRowText(e), 11f,
                e.Success ? TextPrimary : new Color(1f, 0.65f, 0.65f, 1f),
                TextAlignmentOptions.Left, FontStyles.Normal);
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 4f);
            rt.offsetMax = new Vector2(-8f, -4f);
        }

        private static string BuildRowText(SaveTelemetryEntry e)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(e.Timestamp.ToString("HH:mm:ss")).Append(']');
            sb.Append("  <b>").Append(e.Kind).Append("</b>");
            sb.Append(e.Success ? "  <color=#86d068>OK</color>" : "  <color=#ff8080>FAIL</color>");
            sb.Append(e.WasAsync ? "  async" : "  sync");
            sb.Append("  ").Append(FormatBytes(e.SizeBytes));
            sb.Append("  ").Append(e.DurationMs.ToString("0.0")).Append(" ms");
            if (!string.IsNullOrEmpty(e.Reason))
                sb.Append("\n<size=10><color=#90a0b8>").Append(e.Reason).Append("</color></size>");
            return sb.ToString();
        }

        private static string FormatBytes(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
            return $"{b / (1024.0 * 1024):F1} MB";
        }

        // ── Build ────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220; // above pause/general-editor
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 800f);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = MakeStretch("Root", canvasGo.transform);
            _root.AddComponent<Image>().color = OverlayBg;

            var panel = MakeRect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(820f, 540f);
            panel.AddComponent<Image>().color = PanelBg;
            var ol = panel.AddComponent<Outline>();
            ol.effectColor    = Accent;
            ol.effectDistance = new Vector2(2f, 2f);

            // Header
            var header = MakeRect("Header", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f));
            var hRt = header.GetComponent<RectTransform>();
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = Vector2.zero;
            hRt.sizeDelta = new Vector2(0f, 48f);
            header.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f, 1f);

            var title = AddText(header.transform, "SAVE LOG", 18f, Accent,
                                TextAlignmentOptions.Left, FontStyles.Bold);
            var tRt = title.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(0f, 1f);
            tRt.pivot = new Vector2(0f, 0.5f);
            tRt.anchoredPosition = new Vector2(16f, 0f);
            tRt.sizeDelta = new Vector2(200f, 0f);

            _summary = AddText(header.transform, "—", 11f, TextDim,
                               TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            var sRt = _summary.rectTransform;
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(180f, 0f);
            sRt.offsetMax = new Vector2(-120f, 0f);

            var closeBtn = AddButton(header.transform, "Close", 90f, 32f, BtnNormal, BtnHover, Close);
            var cRt = closeBtn.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(1f, 0.5f); cRt.anchorMax = new Vector2(1f, 0.5f);
            cRt.pivot = new Vector2(1f, 0.5f);
            cRt.anchoredPosition = new Vector2(-12f, 0f);

            // Body — scroll
            var scrollGo = MakeRect("Scroll", panel.transform, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var scRt = scrollGo.GetComponent<RectTransform>();
            scRt.offsetMin = new Vector2(8f, 8f);
            scRt.offsetMax = new Vector2(-8f, -52f);
            scrollGo.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 1f);

            var viewport = MakeRect("Viewport", scrollGo.transform, Vector2.zero, Vector2.one);
            viewport.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;
            _content = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 4, 4);
            v.spacing = 2f;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.viewport   = viewport.GetComponent<RectTransform>();
            scroll.content    = contentRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
        }

        // ── Tiny UI helpers ──────────────────────────────────────────────────────

        private static GameObject MakeStretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static Button AddButton(Transform parent, string label, float w, float h,
            Color normal, Color hover, Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = normal;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = normal; c.highlightedColor = hover; c.pressedColor = hover;
            btn.colors = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var lbl = AddText(go.transform, label, 13f, TextPrimary,
                              TextAlignmentOptions.Center, FontStyles.Bold);
            var lRt = lbl.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
