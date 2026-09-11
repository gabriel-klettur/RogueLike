using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.Gameplay.Crafting
{
    public partial class CraftingPanelUI
    {
        private partial void BuildUI()
        {
            var canvasGo = new GameObject("CraftingPanelCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the inventory and below the vendor shop (220). The panel opens from the HUD
            // tray while the inventory may be up, so it has to draw over it; it is never open
            // at the same time as a shop. Picking a number below one keeps the ordering a
            // declaration rather than a race between bootstrap steps — two canvases at the same
            // sortingOrder leave the winner to hierarchy order, the bug the vendor shop's own
            // comment records.
            _canvas.sortingOrder = 210;

            // ScaleWithScreenSize at the project's 1600x800 reference, matching ChatUI and the
            // shop. ConstantPixelSize pins the window to physical pixels, so a 600-wide panel
            // does not fit a small window and is a postage stamp on a large one.
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 800f);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = MakePanel(canvasGo.transform, "CraftingRoot",
                new Vector2(PANEL_W, PANEL_H), UITheme.BG_PANEL);
            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;

            BuildTitleBar(rootRect);
            BuildTabs(rootRect);
            BuildLevelBar(rootRect);
            BuildList(rootRect);
            BuildStatusBar(rootRect);
        }

        private void BuildTitleBar(RectTransform root)
        {
            var bar = MakePanel(root, "TitleBar", new Vector2(PANEL_W, TITLE_H), UITheme.BG_HEADER);
            var rect = bar.GetComponent<RectTransform>();

            // Anchor BEFORE position. anchoredPosition is measured from the anchor, so placing
            // the bar and only then moving the anchor to the top edge puts it a full half-panel
            // off screen — which is exactly how the vendor shop lost both its title and its
            // gold count for the life of that window.
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;

            var title = MakeLabel(bar.transform, "Fabricacion", 15, UITheme.ACCENT,
                TextAlignmentOptions.Left);
            var tr = title.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 0f);
            tr.offsetMax = new Vector2(-170f, 0f);

            // Says whether this trade's harder recipes are unlocked, in the one place the
            // player is already looking. Without it a refused recipe is indistinguishable from
            // a bug: the row explains itself, but only after they try it.
            _stationText = MakeLabel(bar.transform, "", 11, UITheme.TEXT_MUTED,
                TextAlignmentOptions.Right);
            var sr = _stationText.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(PANEL_W - 200f, 0f);
            sr.offsetMax = new Vector2(-38f, 0f);

            // Escape closes it too, but a window with no visible way out reads as stuck.
            var close = MakeButton(bar.transform, "X", 13, UITheme.DANGER_IDLE, 30f);
            var cr = close.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(1f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.sizeDelta = new Vector2(30f, 0f);
            cr.anchoredPosition = new Vector2(-4f, 0f);
            close.onClick.AddListener(() => SetVisible(false));
        }

        private void BuildTabs(RectTransform root)
        {
            var strip = MakePanel(root, "Tabs", new Vector2(PANEL_W, TABS_H), UITheme.BG_SURFACE);
            var rect = strip.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -TITLE_H);

            var layout = strip.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(4, 4, 3, 3);
            layout.spacing = 3f;

            _tabStrip = rect;
        }

        /// <summary>
        /// Realise one tab per profession the catalog carries.
        ///
        /// <para>Driven by the CATALOG rather than a hardcoded list, which is the whole point of
        /// professions being assets: adding a trade is a file and a catalog row, and this strip
        /// grows without a code change. Rebuilt only when the profession list actually changes,
        /// because <c>Destroy</c> on every open would churn widgets for a strip that is stable
        /// for the life of a session.</para>
        /// </summary>
        private partial void RebuildTabsIfNeeded()
        {
            if (_catalog == null || _tabStrip == null) return;

            bool same = _tabs.Count == _catalog.Professions.Count;
            if (same)
                for (int i = 0; i < _tabs.Count; i++)
                    if (_tabs[i] != _catalog.Professions[i]) { same = false; break; }
            if (same && _tabButtons.Count == _tabs.Count) return;

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                if (_tabButtons[i] == null) continue;
                var go = _tabButtons[i].gameObject;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _tabButtons.Clear();
            _tabs.Clear();

            for (int i = 0; i < _catalog.Professions.Count; i++)
            {
                var profession = _catalog.Professions[i];
                if (profession == null) continue;
                _tabs.Add(profession);

                int index = _tabs.Count - 1;
                var label = string.IsNullOrEmpty(profession.displayName)
                    ? profession.professionKey
                    : profession.displayName;
                var btn = MakeButton(_tabStrip, label, 11, UITheme.BTN_NORMAL, 0f);
                btn.onClick.AddListener(() =>
                {
                    _activeTab = index;
                    RefreshRows();
                });
                _tabButtons.Add(btn);
            }

            if (_activeTab >= _tabs.Count) _activeTab = 0;
        }

        /// <summary>
        /// The active trade's level and progress bar. It is the only place professions are
        /// visible to the player today, so it doubles as the progression readout the skills
        /// screen will later mirror.
        /// </summary>
        private void BuildLevelBar(RectTransform root)
        {
            var bar = MakePanel(root, "LevelBar", new Vector2(PANEL_W, LEVEL_H), UITheme.BG_ELEVATED);
            var rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -(TITLE_H + TABS_H));

            // The fill sits BEHIND the label rather than beside it, so a long trade name never
            // squeezes the bar and the bar never truncates the name.
            var track = MakePanel(bar.transform, "Track", Vector2.zero, UITheme.SCROLL_TRACK);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.offsetMin = new Vector2(8f, 5f);
            trackRect.offsetMax = new Vector2(-8f, -5f);

            var fill = MakePanel(track.transform, "Fill", Vector2.zero, UITheme.ACCENT_BG);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _levelFill = fill.GetComponent<Image>();

            _levelText = MakeLabel(track.transform, "", 11, UITheme.TEXT_SECONDARY,
                TextAlignmentOptions.Center);
            var lr = _levelText.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f);
            lr.offsetMax = new Vector2(-8f, 0f);
        }

        private void BuildList(RectTransform root)
        {
            float listH = PANEL_H - TITLE_H - TABS_H - LEVEL_H - STATUS_H;

            var holder = MakePanel(root, "ListHolder", new Vector2(PANEL_W, listH), UITheme.BG_SURFACE);
            var rect = holder.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -(TITLE_H + TABS_H + LEVEL_H));

            // MakeScrollView's content carries a VerticalLayoutGroup AND a ContentSizeFitter.
            // That is wrong for hand-placed children and exactly right here — this IS a list of
            // stacked rows, the case the helper was written for.
            var (scroll, content) = UIFactory.MakeScrollView(holder.transform, "RecipeScroll");
            var scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(6f, 6f);
            scrollRect.offsetMax = new Vector2(-6f, -6f);
            UIFactory.AddVerticalScrollbar(scroll);

            _rowsParent = content;
            var group = content.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                group.spacing = 4f;
                group.padding = new RectOffset(4, 4, 4, 4);
                group.childControlHeight = true;
                group.childForceExpandHeight = false;
            }
        }

        private void BuildStatusBar(RectTransform root)
        {
            var bar = MakePanel(root, "StatusBar", new Vector2(PANEL_W, STATUS_H), UITheme.BG_HEADER);
            var rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;

            _statusText = MakeLabel(bar.transform, "", 11, UITheme.TEXT_SECONDARY,
                TextAlignmentOptions.Left);
            var sr = _statusText.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(10f, 0f);
            sr.offsetMax = new Vector2(-10f, 0f);
        }

        // ── Small factories ──────────────────────────────────────────────────────
        //
        // Local rather than pushed into UIFactory: these are three shapes this one panel needs,
        // and the kit already carries a scroll view and a theme. Growing the shared factory for
        // a single caller is how a kit accumulates methods nobody else can use.

        private static GameObject MakePanel(Transform parent, string name, Vector2 size, Color color)
        {
            var go = UIFactory.CreateUI(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            go.GetComponent<RectTransform>().sizeDelta = size;
            return go;
        }

        /// <summary>
        /// A label always gets its OWN GameObject. An Image and a TMP component on one object
        /// throw a NullReferenceException in this project — the first entry in CLAUDE.md's
        /// gotcha list — so every caller here passes a bare parent.
        /// </summary>
        private static TextMeshProUGUI MakeLabel(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align)
        {
            var go = UIFactory.CreateUI("Label", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// Image + Button on the parent, TMP on a child — see <see cref="MakeLabel"/>.
        ///
        /// <para><paramref name="width"/> is not optional decoration. A button inside a
        /// HorizontalLayoutGroup with childControlWidth and no childForceExpandWidth is laid
        /// out at its MINIMUM unless it declares one, which collapses it to a single character
        /// column — the defect that printed the Controls editor's two buttons one letter per
        /// line down the same column. Pass 0 only when the parent force-expands.</para>
        /// </summary>
        private static Button MakeButton(Transform parent, string text, float size,
            Color color, float width)
        {
            var go = UIFactory.CreateUI("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            if (width > 0f)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.minWidth = width;
                le.flexibleWidth = 0f;
            }

            var label = MakeLabel(go.transform, text, size, UITheme.TEXT_PRIMARY,
                TextAlignmentOptions.Center);
            var lr = label.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
