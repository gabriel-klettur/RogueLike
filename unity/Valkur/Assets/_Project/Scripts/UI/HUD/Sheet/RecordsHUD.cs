using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.HUD;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet's RECORDS tab: what this profile has done across every run.
    ///
    /// <para><b>What it replaces, and why the paint was the last problem.</b> The old panel
    /// printed <c>Total runs: 0</c> directly above a list of runs — measured on this machine,
    /// <b>251 rows in the table and a counter that read 0</b> — plus <c>Total playtime: —</c>
    /// with 251 runs on disk, and <c>kills=0</c> on every row while its own kill table held 39.
    /// Its top-five board listed the SHOPKEEPERS by their database key
    /// (<c>vendor_blacksmith_smith</c>), three of seven rows.</para>
    ///
    /// <para><b>The totals are DERIVED, never counted twice.</b> <c>profile.total_runs</c> exists
    /// and is now maintained correctly, and it is still not what this panel reads: it can only
    /// ever describe runs closed since the day the closing was fixed, so on this profile it says
    /// 1 against 252 rows. A number the table already knows must come from the table. Keeping a
    /// parallel counter is how the two came to disagree by 251 in the first place.</para>
    /// </summary>
    public sealed class RecordsHUD : SingletonMonoBehaviour<RecordsHUD>
    {
        [Tooltip("Resolve the profile DB from the ServiceLocator on first Open when unbound.")]
        [SerializeField] private bool autoResolveDb = true;

        private IProfileDb _db;
        private SheetPanelChrome _chrome;
        private bool _built;

        private readonly List<HudPixelText> _cardValues = new List<HudPixelText>();
        private readonly List<HudPixelText> _killRows = new List<HudPixelText>();
        private readonly List<HudPixelText> _runRows = new List<HudPixelText>();
        private HudPixelText _killHeader, _runHeader, _moreLabel;

        public bool IsOpen { get; private set; }
        public IProfileDb Db => _db;
        public SheetPanelChrome Chrome => _chrome;

        protected override bool Persist => false;

        public void BindDb(IProfileDb db)
        {
            _db = db;
            if (IsOpen) Refresh();
        }

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
            GameLanguage.OnChanged += OnLanguageChanged;
        }

        protected override void OnDestroy()
        {
            GameLanguage.OnChanged -= OnLanguageChanged;
            if (_chrome != null) _chrome.Dispose();
            base.OnDestroy();
        }

        private void OnLanguageChanged(string _) { if (_built && IsOpen) Refresh(); }

        public void Open()
        {
            EnsureBuilt();
            if (_db == null && autoResolveDb) ServiceLocator.TryGet(out _db);
            IsOpen = true;
            _chrome.Show();
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_chrome != null) _chrome.Hide();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        private void Update()
        {
            if (!_built) return;
            _chrome.Tick(Time.unscaledDeltaTime);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _chrome = new SheetPanelChrome(transform, "RecordsHUD");
            var style = _chrome.Style;
            var body = _chrome.MakeBody("Body");

            // Four lifetime cards across the top. A card is a number the player is proud of, so
            // it gets its own recess and a value in the large face — the old panel gave them the
            // same 14 pt as a run's timestamp.
            int cards = 4;
            int gap = 4;
            int cardW = (_chrome.BodyWidth - gap * (cards - 1)) / cards;
            int cardY = _chrome.BodyHeight - style.cardTexels;
            string[] captions =
            {
                SheetText.Runs, SheetText.Playtime, SheetText.Deaths, SheetText.Achievements,
            };
            for (int i = 0; i < cards; i++)
            {
                int x = i * (cardW + gap);
                SheetPanelChrome.Tinted("Card" + i, body, _chrome.Art.BarFrame, x, cardY, cardW,
                                        style.cardTexels, _chrome.Theme.recess, Image.Type.Sliced);

                // SMALL face. The large one has digits and punctuation and no letters at all, so
                // a value carrying a unit — "58s", "2h 30m" — lost the unit in silence and the
                // playtime card read "58". Same defect that drew an empty title on the talents
                // board, one face down.
                var value = HudPixelText.Create(body, "CardValue" + i, _chrome.Art, HudFontFace.Small,
                                                HudTextAlign.Centre, x, cardY + 12, cardW, 7);
                value.color = _chrome.Theme.text;
                _cardValues.Add(value);

                var caption = HudPixelText.Create(body, "CardCaption" + i, _chrome.Art,
                                                  HudFontFace.Small, HudTextAlign.Centre,
                                                  x, cardY + 3, cardW, 7);
                caption.color = _chrome.Theme.textDim;
                caption.SetText(captions[i].ToUpperInvariant());
            }

            // Two columns below: what you killed, and what you played.
            int listTop = cardY - 10;
            int colW = (_chrome.BodyWidth - gap) / 2;

            _killHeader = Header(body, "KillHeader", 0, listTop, colW);
            for (int i = 0; i < style.topKillsShown; i++)
                _killRows.Add(Row(body, "Kill" + i, 0, listTop - 10 - i * style.listRowTexels, colW));

            _runHeader = Header(body, "RunHeader", colW + gap, listTop, colW);
            for (int i = 0; i < style.recentRunsShown; i++)
                _runRows.Add(Row(body, "Run" + i, colW + gap,
                                 listTop - 10 - i * style.listRowTexels, colW));

            _moreLabel = Row(body, "More", colW + gap,
                             listTop - 10 - style.recentRunsShown * style.listRowTexels, colW);
            _moreLabel.color = _chrome.Theme.textDisabled;
        }

        private HudPixelText Header(Transform parent, string name, int x, int y, int w)
        {
            var t = HudPixelText.Create(parent, name, _chrome.Art, HudFontFace.Small,
                                        HudTextAlign.Left, x + 2, y, w - 4, 7);
            t.color = _chrome.Theme.gold;
            return t;
        }

        private HudPixelText Row(Transform parent, string name, int x, int y, int w)
        {
            var t = HudPixelText.Create(parent, name, _chrome.Art, HudFontFace.Small,
                                        HudTextAlign.Left, x + 2, y, w - 4, 7);
            t.color = _chrome.Theme.textDim;
            return t;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (!_built) return;
            _chrome.SetTitle(SheetText.Records);

            if (_db == null)
            {
                _chrome.SetFooter(SheetText.NoRuns);
                for (int i = 0; i < _cardValues.Count; i++) _cardValues[i].SetText("—");
                for (int i = 0; i < _killRows.Count; i++) _killRows[i].SetText(string.Empty);
                for (int i = 0; i < _runRows.Count; i++) _runRows[i].SetText(string.Empty);
                _moreLabel.SetText(string.Empty);
                return;
            }

            var runs = _db.Runs.GetAll();

            // DERIVED. See the class note: the parallel counter cannot describe a run it never
            // saw close, and on this profile it reads 1 against 252 rows.
            float playtime = 0f;
            int closed = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].durationSeconds <= 0f) continue;
                playtime += runs[i].durationSeconds;
                closed++;
            }

            _cardValues[0].SetText(runs.Count.ToString());
            _cardValues[1].SetText(SheetText.Duration(playtime));
            _cardValues[2].SetText(_db.Profile.GetInt("deaths_total").ToString());
            _cardValues[3].SetText(_db.Achievements.UnlockedCount().ToString());

            _killHeader.SetText(SheetText.TopKills.ToUpperInvariant());
            var top = _db.KillStats.GetTop(_killRows.Count);
            for (int i = 0; i < _killRows.Count; i++)
            {
                if (i >= top.Count) { _killRows[i].SetText(string.Empty); continue; }
                // The DISPLAY name, never the key. "vendor_banker_abigail" on a player's screen
                // is the defect the quest audit already named once.
                _killRows[i].SetText((ResolveMonsterName(top[i].entityKey) + "   " + top[i].totalKills)
                                     .ToUpperInvariant());
            }
            if (top.Count == 0) _killRows[0].SetText(SheetText.NoKills.ToUpperInvariant());

            _runHeader.SetText(SheetText.RecentRuns.ToUpperInvariant());
            int shown = Mathf.Min(_runRows.Count, runs.Count);
            for (int i = 0; i < _runRows.Count; i++)
            {
                if (i >= shown) { _runRows[i].SetText(string.Empty); continue; }
                // GetAll() already returns NEWEST first — measured, not assumed: first=2026-09-12,
                // last=2026-05-08. Walking it backwards put the OLDEST ten under a header that
                // says "recent", which is a list that is wrong in the one way nobody checks.
                var r = runs[i];
                string when = string.IsNullOrEmpty(r.startedAtIso) ? "?" : r.startedAtIso.Substring(0, 10);
                string outcome = r.durationSeconds <= 0f ? SheetText.InProgress
                               : string.IsNullOrEmpty(r.killedBy) ? SheetText.Survived
                               : r.killedBy;
                _runRows[i].SetText((when + "  " + SheetText.Duration(r.durationSeconds)
                                     + "  " + r.totalKills + "  " + outcome).ToUpperInvariant());
            }
            if (runs.Count == 0) _runRows[0].SetText(SheetText.NoRuns.ToUpperInvariant());

            // A list of ten out of 252 must SAY so. The old panel printed ten and stopped.
            int more = runs.Count - shown;
            _moreLabel.SetText(more > 0 ? SheetText.AndMore(more).ToUpperInvariant() : string.Empty);

            _chrome.SetFooter(SheetText.Lifetime + "   " + closed + " / " + runs.Count);
        }

        /// <summary>
        /// The monster's own display name, falling back to its key. The key is for the disk.
        /// </summary>
        private static string ResolveMonsterName(string entityKey)
        {
            if (string.IsNullOrEmpty(entityKey)) return "?";
            MonsterCatalog catalog;
            if (ServiceLocator.TryGet(out catalog) && catalog != null)
            {
                var def = catalog.GetByKey(entityKey);
                if (def != null && !string.IsNullOrWhiteSpace(def.displayName)) return def.displayName;
            }
            // The key, deliberately, when the catalog is absent — a row that vanished would hide
            // a kill the player earned. It is the least-bad fallback, not the intended state.
            return entityKey;
        }
    }
}
