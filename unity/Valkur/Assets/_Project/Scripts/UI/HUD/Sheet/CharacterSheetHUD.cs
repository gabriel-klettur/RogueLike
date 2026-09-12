using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet's CHARACTER tab: who the player is on the left, what their numbers are
    /// on the right, and — the part that matters — WHERE each number came from.
    ///
    /// <para><b>What it replaces.</b> One <c>UnityEngine.UI.Text</c> holding the whole panel as a
    /// pre-formatted string, laid out with <c>PadRight(20)</c> and <c>PadLeft(8)</c> — which is
    /// monospace arithmetic — and drawn in <c>LegacyRuntime.ttf</c>, which is proportional Arial.
    /// Measured live across the fourteen stat rows: the value column started at fourteen
    /// different x, spanning <b>87 px</b>. The columns were not misaligned; the mechanism that
    /// would align them did not exist. The block also occupied 316 px of an 883 px body — 64 % of
    /// the panel empty while the table crushed itself into a narrow column.</para>
    ///
    /// <para><b>The breakdown is now a BAR.</b> "14 = 2 base + 4 level + 6 gear" was the best
    /// idea in the old panel and it was buried in the same grey as everything else. A stacked bar
    /// of seven segments, one per <see cref="StatLayer"/>, answers "where does this come from" at
    /// a glance — and makes a layer that has silently stopped contributing a segment that is
    /// missing rather than a line nobody reads. The numbers stay: they move to the row the player
    /// is pointing at, because "how much exactly" is not a question anyone asks of all fourteen
    /// rows at once.</para>
    /// </summary>
    public sealed class CharacterSheetHUD : SingletonMonoBehaviour<CharacterSheetHUD>
    {
        [SerializeField] private PlayerStats stats;

        private SheetPanelChrome _chrome;
        private bool _built;
        private readonly List<CharacterStatRow> _rows = new List<CharacterStatRow>();
        private readonly Dictionary<StatKind, float> _lastValues = new Dictionary<StatKind, float>();

        private HudPortrait _portrait;
        private HudPixelText _levelLabel;
        private HudPixelText _xpLabel;
        private Image _xpFill;
        private int _xpTrackWidth;
        private HudPixelText _skillPoints;
        private HudPixelText _arcanePoints;
        private HudMoteLayer _motes;
        private PlayerHudStyle _grid;
        private RectTransform _identity;
        private RectTransform _table;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<CharacterStatRow> Rows => _rows;
        public SheetPanelChrome Chrome => _chrome;

        protected override bool Persist => false;

        // ── Binding ───────────────────────────────────────────────────────────

        public void Bind(PlayerStats value)
        {
            Unbind();
            stats = value;
            if (stats != null) stats.OnStatsChanged += Refresh;
            if (IsOpen) Refresh();
        }

        private void Unbind()
        {
            if (stats != null) stats.OnStatsChanged -= Refresh;
        }

        private void AutoResolve()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            Bind(player.GetComponent<PlayerStats>());
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
            GameLanguage.OnChanged += OnLanguageChanged;
        }

        protected override void OnDestroy()
        {
            GameLanguage.OnChanged -= OnLanguageChanged;
            Unbind();
            if (_portrait != null) _portrait.Dispose();
            if (_chrome != null) _chrome.Dispose();
            base.OnDestroy();
        }

        private void OnLanguageChanged(string _)
        {
            if (_built && IsOpen) Refresh();
        }

        public void Open()
        {
            EnsureBuilt();
            if (stats == null) AutoResolve();
            IsOpen = true;
            _chrome.Show();
            var playerGo = EntityRegistry.PlayerTransform;
            if (_portrait != null && playerGo != null) _portrait.Bind(playerGo.gameObject);
            _lastValues.Clear();   // a fresh open announces nothing; it is not a change
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_motes != null) _motes.Clear();
            if (_chrome != null) _chrome.Hide();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        private void Update()
        {
            if (!_built) return;
            _chrome.Tick(Time.unscaledDeltaTime);
            if (!IsOpen) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Tick(now);
            if (_motes != null) _motes.Tick(Time.unscaledDeltaTime);

            if (_portrait != null)
            {
                var player = EntityRegistry.PlayerTransform;
                var health = player != null ? player.GetComponent<Health>() : null;
                float ratio = health != null && health.MaxHp > 0 ? health.CurrentHp / (float)health.MaxHp : 1f;
                _portrait.Tick(Time.unscaledDeltaTime, ratio, health != null && health.IsDead);
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds the window once. Public because Edit Mode never calls Awake.</summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _chrome = new SheetPanelChrome(transform, "CharacterSheetHUD");
            _grid = PlayerHudStyle.Active;
            var style = _chrome.Style;

            _identity = HudRect.Make("Identity", _chrome.Pixels, _chrome.BodyLeft, _chrome.BodyBottom,
                                     style.identityWidthTexels, _chrome.BodyHeight);
            int tableX = _chrome.BodyLeft + style.identityWidthTexels + 6;
            _table = HudRect.Make("Table", _chrome.Pixels, tableX, _chrome.BodyBottom,
                                  _chrome.BodyWidth - style.identityWidthTexels - 6, _chrome.BodyHeight);

            BuildIdentity();
            BuildRows();

            _motes = HudMoteLayer.Create(_chrome.Pixels, _chrome.Art, style.moteCapacity, _chrome.Additive);
        }

        private void BuildIdentity()
        {
            var style = _chrome.Style;
            var theme = _chrome.Theme;
            int w = style.identityWidthTexels;
            int y = _chrome.BodyHeight - 16;

            SheetPanelChrome.Tinted("Recess", _identity, _chrome.Art.BarFrame, 0, 0, w, _chrome.BodyHeight,
                                    theme.recess, Image.Type.Sliced);

            // The portrait. Reused wholesale from the player panel rather than re-solved here:
            // it already bakes the EAST idle frame cropped to the head, re-reads the idle set so
            // a loadout swap re-bakes it, and drains its colour as health falls. Without it the
            // identity column was a tall empty recess, which is what the first capture showed.
            int portrait = Mathf.Min(w - 12, 56);
            y -= portrait - 16;
            _portrait = new HudPortrait(_identity, _chrome.Art, _grid, (w - portrait) / 2, y,
                                        portrait, _grid.hudFxShader);
            y -= 24;

            int med = 20;
            SheetPanelChrome.Tinted("Medallion", _identity, _chrome.Art.Medallion,
                                    (w - med) / 2, y, med, med, theme.gold);
            _levelLabel = HudPixelText.Create(_identity, "Level", _chrome.Art, HudFontFace.Large,
                                              HudTextAlign.Centre, (w - med) / 2, y, med, med);
            _levelLabel.color = theme.text;

            y -= 12;
            var caption = HudPixelText.Create(_identity, "LevelCaption", _chrome.Art, HudFontFace.Small,
                                              HudTextAlign.Centre, 4, y, w - 8, 7);
            caption.color = theme.textDim;
            caption.SetText(SheetText.Level.ToUpperInvariant());

            // Experience: a real bar, because "0/100 XP" in a sentence is a number the player has
            // to do arithmetic on to know how close they are.
            y -= 12;
            _xpTrackWidth = w - 16;
            SheetPanelChrome.Tinted("XpTrack", _identity, _chrome.Art.BarFrameThin, 8, y, _xpTrackWidth, 5,
                                    theme.recess, Image.Type.Sliced);
            _xpFill = SheetPanelChrome.Tinted("XpFill", _identity, _chrome.Art.White, 9, y + 1, 1, 3,
                                              theme.gold);
            y -= 10;
            _xpLabel = HudPixelText.Create(_identity, "Xp", _chrome.Art, HudFontFace.Small,
                                           HudTextAlign.Centre, 4, y, w - 8, 7);
            _xpLabel.color = theme.textDim;

            y -= 16;
            _skillPoints = MakeCurrency("Skill", y, theme.gold);
            y -= 14;
            _arcanePoints = MakeCurrency("Arcane", y, theme.info);
        }

        private HudPixelText MakeCurrency(string name, int y, Color tint)
        {
            int w = _chrome.Style.identityWidthTexels;
            var label = HudPixelText.Create(_identity, name, _chrome.Art, HudFontFace.Small,
                                            HudTextAlign.Centre, 4, y, w - 8, 7);
            label.color = tint;
            return label;
        }

        private void BuildRows()
        {
            var style = _chrome.Style;
            int width = _chrome.BodyWidth - style.identityWidthTexels - 6;
            var all = StatCatalog.All;
            int y = _chrome.BodyHeight - style.statRowTexels;

            for (int i = 0; i < all.Length; i++)
            {
                var row = new CharacterStatRow(all[i], _table, _chrome, 0, y, width);
                _rows.Add(row);
                y -= style.statRowTexels;
            }
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (!_built) return;
            _chrome.SetTitle(SheetText.Character);

            if (stats == null)
            {
                _chrome.SetFooter(SheetText.NoCharacter);
                for (int i = 0; i < _rows.Count; i++) _rows[i].SetEmpty();
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            var xp = player != null ? player.GetComponent<Experience>() : null;
            var skills = player != null ? player.GetComponent<LearnedSkills>() : null;
            var grimoire = player != null ? player.GetComponent<KnownSpells>() : null;

            RefreshIdentity(xp, skills, grimoire);

            float now = Time.unscaledTime;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                float value = stats.Get(row.Stat);

                // A number that CHANGED while the tab is open is an event — the player just
                // bought a talent or equipped a sword and the consequence is on this screen.
                // A first read is not: _lastValues is cleared on open.
                float previous;
                if (_lastValues.TryGetValue(row.Stat, out previous) && !Mathf.Approximately(previous, value))
                {
                    row.Knock(now);
                    EmitStatChange(row, value > previous);
                }
                _lastValues[row.Stat] = value;

                row.SetValue(stats, value);
            }

            _chrome.SetFooter(SheetText.Stats + "   " + _rows.Count);
        }

        private void RefreshIdentity(Experience xp, LearnedSkills skills, KnownSpells grimoire)
        {
            var theme = _chrome.Theme;
            _levelLabel.SetText(xp != null ? xp.Level.ToString() : "0");

            if (xp != null)
            {
                int into = xp.XpInCurrentLevel;
                int span = Mathf.Max(1, xp.XpForNextLevel - xp.XpRequiredForLevel(xp.Level));
                float t = Mathf.Clamp01(into / (float)span);
                int w = Mathf.Max(1, Mathf.RoundToInt(t * (_xpTrackWidth - 2)));
                var rt = _xpFill.rectTransform;
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                _xpLabel.SetText(into + " / " + span);
            }
            else
            {
                _xpLabel.SetText("—");
            }

            _skillPoints.SetText(skills != null
                ? (SheetText.SkillPoints + "  " + skills.AvailablePoints).ToUpperInvariant()
                : string.Empty);
            _arcanePoints.SetText(grimoire != null
                ? (SheetText.ArcanePoints + "  " + grimoire.AvailablePoints).ToUpperInvariant()
                : string.Empty);
        }

        /// <summary>
        /// The one event this panel has. A stat going UP is the player's own doing and reads in
        /// the stat's colour; a stat going DOWN (a buff expiring, gear removed) is the same shape
        /// in the danger colour, because "something left" is exactly as worth noticing.
        /// </summary>
        private void EmitStatChange(CharacterStatRow row, bool up)
        {
            if (_motes == null) return;
            Vector2 origin = row.ValueCentre + _table.anchoredPosition;
            Color c = up ? _chrome.Theme.success : _chrome.Theme.danger;
            int n = _chrome.Style.motesPerStatChange;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * (0.25f + 0.5f * i / Mathf.Max(1, n - 1));
                var vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(12f, 24f);
                _motes.Emit(origin, vel, c, _chrome.Style.moteLifeSeconds, HudMoteShape.Dot,
                            gravity: up ? 10f : -10f, drag: 2f);
            }
        }
    }
}
