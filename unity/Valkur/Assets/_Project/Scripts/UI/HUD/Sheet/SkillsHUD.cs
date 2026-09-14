using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Skills;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet's SKILLS tab: every skill the player can learn by doing — woodcutting,
    /// mining and fishing, cooking, blacksmithing and crafting — as one table of 0-100 % numbers,
    /// and the selected skill's detail beside it.
    ///
    /// <para><b>SKILLS ARE NOT TALENTS, AND THE TWO TABS SAY SO.</b> A talent is bought with points
    /// earned by levelling and lives in the TALENTOS tab; a skill is raised only by practising it
    /// and lives here. They used to share the word "skills", and the tab of that name showed the
    /// talent tree while the trades hid behind a tab called OFICIOS.</para>
    ///
    /// <para><b>A SKILL THE GAME CANNOT TRAIN YET IS SHOWN LOCKED, NOT HIDDEN.</b> Mining and
    /// fishing have no nodes yet. A table that dropped them would be a table that grows rows out
    /// of nowhere; a greyed "pronto" row says the skill exists and is not reachable today.
    /// Whether a row is locked is DERIVED (<see cref="SkillAvailability"/>), so the day fishing
    /// ships its row lights up with no edit here.</para>
    ///
    /// <para>The table is built from the catalog, grouped by <see cref="SkillCategory"/>, so a new
    /// skill is an asset and a catalog row. Read-only; polled at a slow tick while open.</para>
    /// </summary>
    public sealed partial class SkillsHUD : SingletonMonoBehaviour<SkillsHUD>
    {
        private const int TableWidth = 150;
        private const int Gap = 6;
        private const int HeaderRowTexels = 10;
        private const int GroupRowTexels = 11;
        private const int SkillRowTexels = 16;
        private const float RefreshSeconds = 0.5f;

        /// <summary>One row of the table and the skill behind it.</summary>
        private sealed class Row
        {
            public SkillDefinition Skill;
            public bool Trainable;
            public Image Background;
            public Image Marker;
            public Image Fill;
            public int FillWidth;
            public HudPixelText Name;
            public HudPixelText Value;
        }

        private SheetPanelChrome _chrome;
        private bool _built;
        private float _nextRefresh;

        private SkillCatalog _catalog;
        private RecipeCatalog _recipes;
        private RectTransform _body;
        private RectTransform _detail;
        private Action _refreshDetail;

        private readonly List<Row> _rows = new List<Row>();
        private string _selectedKey;

        public bool IsOpen { get; private set; }
        public SheetPanelChrome Chrome => _chrome;

        /// <summary>Keys of every row, in table order. For tests and the console.</summary>
        public IReadOnlyList<string> RowKeys
        {
            get
            {
                var keys = new List<string>(_rows.Count);
                for (int i = 0; i < _rows.Count; i++) keys.Add(_rows[i].Skill.skillKey);
                return keys;
            }
        }

        /// <summary>The skill whose detail is on screen.</summary>
        public string SelectedKey => _selectedKey;

        /// <summary>Which detail view is built: "gathering", "crafting", "locked" or empty.</summary>
        public string DetailKind { get; private set; } = string.Empty;

        protected override bool Persist => false;

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            if (_chrome != null) _chrome.Dispose();
            base.OnDestroy();
        }

        public void Open()
        {
            EnsureBuilt();
            IsOpen = true;
            _chrome.Show();
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_chrome != null) _chrome.Hide();
        }

        private void Update()
        {
            if (!_built) return;
            _chrome.Tick(Time.unscaledDeltaTime);
            if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        /// <summary>Whether the row for <paramref name="key"/> can be trained today.</summary>
        public bool IsRowTrainable(string key)
        {
            var row = FindRow(key);
            return row != null && row.Trainable;
        }

        /// <summary>Show <paramref name="key"/>'s detail. Locked rows can be selected too.</summary>
        public void Select(string key)
        {
            EnsureBuilt();
            var row = FindRow(key);
            if (row == null) return;
            _selectedKey = row.Skill.skillKey;
            BuildDetail(row);
            PaintRows();
            RefreshDetailNow();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds the window once. Public because Edit Mode never calls Awake.</summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            Build(SkillCatalog.Shared, Resources.Load<RecipeCatalog>(RecipeCatalog.ResourcePath));
        }

        /// <summary>
        /// Builds against catalogs a test made instead of the shipped ones. Must be the first
        /// build: a panel already built from the shipped catalogs is refused rather than mixed.
        /// </summary>
        internal void BuildForTests(SkillCatalog skills, RecipeCatalog recipes)
        {
            if (_built) throw new InvalidOperationException("SkillsHUD is already built.");
            Build(skills, recipes);
        }

        private void Build(SkillCatalog skills, RecipeCatalog recipes)
        {
            _built = true;
            _chrome = new SheetPanelChrome(transform, "SkillsHUD");
            _catalog = skills;
            _recipes = recipes;

            _body = _chrome.MakeBody("Body");
            BuildTable(_body);
            _detail = HudRect.Make("Detail", _body, TableWidth + Gap, 0,
                                   _chrome.BodyWidth - TableWidth - Gap, _chrome.BodyHeight);

            // Opens on the first skill the player can actually raise: a locked row is a fine
            // thing to read about and a poor thing to be greeted by.
            Row first = null;
            foreach (var row in _rows)
                if (row.Trainable) { first = row; break; }
            if (first == null && _rows.Count > 0) first = _rows[0];
            if (first != null) Select(first.Skill.skillKey);
        }

        private void BuildTable(Transform body)
        {
            var theme = _chrome.Theme;
            int h = _chrome.BodyHeight;

            SheetPanelChrome.Tinted("TableRecess", body, _chrome.Art.BarFrame, 0, 0, TableWidth, h,
                                    theme.recess, Image.Type.Sliced);

            int y = h - HeaderRowTexels;
            Text(body, "HeadSkill", 6, y + 2, 80, HudFontFace.Small, HudTextAlign.Left, theme.textDisabled).SetText("SKILL");
            Text(body, "HeadValue", TableWidth - 46, y + 2, 40, HudFontFace.Small, HudTextAlign.Right, theme.textDisabled).SetText("%");
            SheetPanelChrome.Tinted("HeadRule", body, _chrome.Art.White, 4, y, TableWidth - 8, 1, theme.stoneLight);

            if (_catalog == null) return;

            y = BuildGroup(body, SkillCategory.Gathering, "Recolección", y);
            y = BuildGroup(body, SkillCategory.Crafting, "Fabricación", y - 2);
            BuildGroup(body, SkillCategory.Physical, "Físico", y - 2);
        }

        private int BuildGroup(Transform body, SkillCategory category, string title, int y)
        {
            var skills = _catalog.InCategory(category);
            if (skills.Count == 0) return y;

            var theme = _chrome.Theme;
            y -= GroupRowTexels;
            Text(body, "Group_" + category, 6, y + 3, TableWidth - 12, HudFontFace.Small, HudTextAlign.Left, theme.gold)
                .SetText(title);

            foreach (var skill in skills)
            {
                y -= SkillRowTexels;
                _rows.Add(BuildRow(body, skill, y));
            }
            return y;
        }

        private Row BuildRow(Transform body, SkillDefinition skill, int y)
        {
            var theme = _chrome.Theme;
            var row = new Row { Skill = skill, Trainable = SkillAvailability.IsTrainable(skill, _recipes) };

            int x = 3, w = TableWidth - 6, h = SkillRowTexels - 1;
            row.Background = SheetPanelChrome.Tinted("Row_" + skill.skillKey, body, _chrome.Art.Slot,
                                                     x, y, w, h, theme.stoneDark, Image.Type.Sliced);
            row.Background.raycastTarget = true;
            var button = row.Background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            string key = skill.skillKey;
            button.onClick.AddListener(() => Select(key));

            row.Marker = SheetPanelChrome.Tinted("Marker", row.Background.transform, _chrome.Art.White, 0, 2, 1, h - 4, theme.gold);

            row.Name = Text(row.Background.transform, "Name", 5, 7, w - 50, HudFontFace.Small, HudTextAlign.Left, theme.text);
            row.Name.SetText(string.IsNullOrEmpty(skill.displayName) ? skill.skillKey : skill.displayName);
            row.Value = Text(row.Background.transform, "Value", w - 46, 7, 41, HudFontFace.Small, HudTextAlign.Right, theme.textDim);

            row.FillWidth = w - 12;
            SheetPanelChrome.Tinted("Track", row.Background.transform, _chrome.Art.White, 5, 3, row.FillWidth, 2, theme.recess);
            row.Fill = SheetPanelChrome.Tinted("Fill", row.Background.transform, _chrome.Art.White, 5, 3, 1, 2, skill.accentColor);
            return row;
        }

        private HudPixelText Text(Transform parent, string name, int x, int y, int w, HudFontFace face,
            HudTextAlign align, Color colour)
        {
            var t = HudPixelText.Create(parent, name, _chrome.Art, face, align, x, y, w, HudPixelFont.HeightOf(face) + 2);
            t.color = colour;
            return t;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (!_built) return;
            _chrome.SetTitle("Skills");

            var skills = PlayerSkillsNow();
            int trainable = 0;
            float total = 0f;

            foreach (var row in _rows)
            {
                int tenths = skills != null ? skills.GetTenths(row.Skill.skillKey) : 0;
                if (row.Trainable) { trainable++; total += SkillDefinition.ToPercent(tenths); }

                row.Value.SetText(row.Trainable ? SkillDefinition.FormatPercent(tenths) : "pronto");
                int fill = Mathf.RoundToInt(tenths / (float)SkillDefinition.MaxTenths * row.FillWidth);
                row.Fill.enabled = row.Trainable && fill > 0;
                row.Fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(1, fill), 2);
            }

            PaintRows();
            RefreshDetailNow();

            _chrome.SetFooter(_rows.Count == 0
                ? "Sin skills"
                : $"{trainable} de {_rows.Count} skills disponibles  -  total {total:0.0}%");
        }

        private void RefreshDetailNow() => _refreshDetail?.Invoke();

        private void PaintRows()
        {
            var theme = _chrome.Theme;
            foreach (var row in _rows)
            {
                bool selected = string.Equals(row.Skill.skillKey, _selectedKey, StringComparison.OrdinalIgnoreCase);
                row.Background.color = selected ? theme.stoneLight : theme.stoneDark;
                row.Marker.enabled = selected;
                row.Name.color = row.Trainable ? theme.text : theme.textDisabled;
                row.Value.color = !row.Trainable ? theme.textDisabled : selected ? theme.gold : theme.textDim;
            }
        }

        private Row FindRow(string key)
        {
            foreach (var row in _rows)
                if (string.Equals(row.Skill.skillKey, key, StringComparison.OrdinalIgnoreCase)) return row;
            return null;
        }

        private static PlayerSkills PlayerSkillsNow()
        {
            var player = EntityRegistry.PlayerTransform;
            return player != null ? PlayerSkills.Peek(player.gameObject) : null;
        }
    }
}
