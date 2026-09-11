using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Diagnostics;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    public partial class DebugHUD
    {
        private float _alpha;
        private float _rebuildTimer;
        private float _lastPushedStamp = -1f;
        private float _lastChipStamp = -1f;

        private GameObject _playerGo;
        private Health _health;
        private Mana _mana;
        private DashAbility _dash;
        private SpellCaster _caster;
        private PlayerController _controller;
        private Rigidbody2D _body;
        private StatusEffectManager _statuses;

        private readonly List<DebugNearbyEntry> _nearby = new List<DebugNearbyEntry>(8);
        private readonly List<StatusEffect> _statusBuffer = new List<StatusEffect>(8);
        private readonly StringBuilder _sb = new StringBuilder(128);

        // -- Level change ------------------------------------------------------------

        private void OnLevelChanged(int previous, int level)
        {
            if (_canvas == null) return;
            bool visible = level > LevelHidden;
            if (visible && !_canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(true);
                _alpha = 0f;
            }
            _chip.gameObject.SetActive(level == LevelChip);
            _panel.gameObject.SetActive(level >= LevelPanel);
            _group.blocksRaycasts = visible;

            if (level >= LevelPanel) _counters.Start();
            else _counters.Stop();

            if (visible)
            {
                _hint.SetText(DebugHudText.LevelHint(ToggleKeyLabel(), level, MaxLevel));
                var perf = PerformanceMonitor.Instance;
                var history = perf != null ? perf.History : null;
                // Open on the last seconds rather than on an empty strip.
                _graph.Repaint(history, _style, PerformanceMonitor.HitchFloorMs);
                _chipGraph.Repaint(history, _style, PerformanceMonitor.HitchFloorMs);
                _lastPushedStamp = _lastChipStamp = history != null && history.Count > 0 ? history.StampAt(0) : -1f;
                SyncEventBaselines();
                Layout();
                RefreshReadouts();
            }
            if (_motes != null) _motes.Clear();
        }

        // -- Frame -------------------------------------------------------------------

        /// <summary>Advances the overlay. Public so an EditMode test can drive it without a clock.</summary>
        public void Tick(float dt)
        {
            if (_canvas == null) return;
            bool visible = _level > LevelHidden;

            float fade = _style.fadeSeconds;
            float target = visible ? 1f : 0f;
            _alpha = fade <= 0f ? target : Mathf.MoveTowards(_alpha, target, dt / fade);
            if (_group.alpha != _alpha) _group.alpha = _alpha;
            if (!visible)
            {
                if (_alpha <= 0f && _canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                return;
            }

            Refit();
            SyncGraphs();
            PollEvents(dt);
            if (_motes != null) _motes.Tick(dt);
            TickCopyFeedback(dt);

            _rebuildTimer -= dt;
            if (_rebuildTimer > 0f) return;
            _rebuildTimer = _style.rebuildSeconds;
            RefreshReadouts();
        }

        /// <summary>Pushes every frame the monitor recorded since the last tick, oldest first.</summary>
        private void SyncGraphs()
        {
            var perf = PerformanceMonitor.Instance;
            if (perf == null) return;
            var history = perf.History;
            float typical = perf.Stats.P50Ms;
            if (_level >= LevelPanel) _lastPushedStamp = PushNew(_graph, history, _lastPushedStamp, typical);
            else _lastChipStamp = PushNew(_chipGraph, history, _lastChipStamp, typical);
        }

        private float PushNew(DebugFrameGraph graph, FrameTimeHistory history, float lastStamp, float typical)
        {
            int fresh = 0;
            while (fresh < history.Count && history.StampAt(fresh) > lastStamp) fresh++;
            fresh = Mathf.Min(fresh, graph.Width);
            for (int age = fresh - 1; age >= 0; age--)
            {
                float ms = history.MsAt(age);
                bool hitch = FrameHitchLog.IsHitch(ms, typical, PerformanceMonitor.HitchFactor, PerformanceMonitor.HitchFloorMs);
                graph.Push(ms, hitch, _style);
            }
            return history.Count > 0 ? history.StampAt(0) : lastStamp;
        }

        // -- Readouts -------------------------------------------------------------------

        /// <summary>Refills every visible row now. Public so a test can read a panel without waiting.</summary>
        public void RefreshReadouts()
        {
            if (_panel == null) return;
            _style.ResolveSurfaces(out _, out _, out _, out _, out var text, out var dim);
            var perf = PerformanceMonitor.Instance;
            var stats = perf != null ? perf.Stats : default;

            FillBigNumbers(_chipNumbers, stats);
            if (_level < LevelPanel) return;

            FillBigNumbers(_bigNumbers, stats);
            _bigLabels.Set(2, stats.IsEmpty ? "" : DebugHudText.Grade(_style.GradeOf(stats.AvgMs)), _style.ForFrame(stats.AvgMs));
            _perf.SetSummary("HUD " + DebugHudText.SmallMs(_selfCostMs) + "MS", dim);

            FillStats(perf, stats, text, dim);
            bool layoutDirty = false;

            ResolvePlayer();
            layoutDirty |= FillPlayer(text, dim);
            FillCombat(text, dim);
            layoutDirty |= FillNearby(text, dim);

            if (layoutDirty) Layout();
        }

        private void FillBigNumbers(DebugHudRow row, FrameStats stats)
        {
            if (stats.IsEmpty)
            {
                row.Set(0, "-", _style.textDim);
                row.Set(1, "-", _style.textDim);
                return;
            }
            var c = _style.ForFrame(stats.AvgMs);
            row.Set(0, Mathf.RoundToInt(Mathf.Min(999f, stats.Fps)).ToString(), c);
            row.Set(1, DebugHudText.Ms(stats.AvgMs), c);
        }

        private void FillStats(PerformanceMonitor perf, FrameStats s, Color text, Color dim)
        {
            _stats0.Set(1, DebugHudText.Ms(s.P50Ms), _style.ForFrame(s.P50Ms));
            _stats0.Set(3, DebugHudText.Ms(s.P95Ms), _style.ForFrame(s.P95Ms));
            _stats0.Set(5, DebugHudText.Ms(s.P99Ms), _style.ForFrame(s.P99Ms));
            _stats0.Set(7, DebugHudText.Ms(s.MaxMs), _style.ForFrame(s.MaxMs));

            float cpu = _counters.MainThreadMs, gpu = _counters.GpuMs;
            _stats1.Set(1, cpu < 0f ? DebugHudText.Unavailable : DebugHudText.Ms(cpu), cpu < 0f ? dim : text);
            _stats1.Set(3, gpu < 0f ? DebugHudText.Unavailable : DebugHudText.Ms(gpu), gpu < 0f ? dim : text);
            SetCount(_stats1, 5, _counters.Batches, text, dim);
            SetCount(_stats1, 7, _counters.SetPassCalls, text, dim);

            float gcRate = perf != null ? perf.GcPerMinute : 0f;
            _stats2.Set(1, gcRate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                        gcRate > 6f ? _style.warn : text);
            long alloc = _counters.GcAllocatedInFrame;
            _stats2.Set(3, alloc < 0 ? DebugHudText.Unavailable : DebugHudText.Bytes(alloc),
                        alloc < 0 ? dim : alloc > 4096 ? _style.warn : text);
            long mem = _counters.UsedMemory;
            _stats2.Set(5, mem < 0 ? DebugHudText.Unavailable : DebugHudText.Bytes(mem), mem < 0 ? dim : text);
            int errors = perf != null ? perf.Console.Errors : 0;
            _stats2.Set(7, errors.ToString(), errors > 0 ? _style.bad : _style.good);

            FillHitch(perf, text, dim);

            bool wantError = errors > 0 && perf != null && !string.IsNullOrEmpty(perf.Console.LastError);
            if (wantError)
                _errorRow.Set(0, DebugHudText.Fold(perf.Console.LastError, 34), _style.bad);
            if (wantError != _showErrorRow)
            {
                _showErrorRow = wantError;
                Layout();
            }
        }

        private void FillHitch(PerformanceMonitor perf, Color text, Color dim)
        {
            var log = perf != null ? perf.Hitches : null;
            if (log == null || log.Count == 0)
            {
                _hitchRow.Set(1, DebugHudText.NoHitches, dim);
                return;
            }
            var h = log.Get(0);
            _sb.Clear();
            _sb.Append(DebugHudText.Ms(h.Ms)).Append("MS HACE ")
               .Append(DebugHudText.Clock(Time.realtimeSinceStartup - h.Time));
            if (h.DuringGc) _sb.Append(" GC");
            if (log.TotalRecorded > 1) _sb.Append(" (").Append(log.TotalRecorded).Append(')');
            _hitchRow.Set(1, _sb.ToString(), _style.ForFrame(h.Ms));
        }

        private static void SetCount(DebugHudRow row, int cell, long value, Color text, Color dim) =>
            row.Set(cell, value < 0 ? DebugHudText.Unavailable : value.ToString(), value < 0 ? dim : text);

        // -- Player ----------------------------------------------------------------------

        private void ResolvePlayer()
        {
            var p = EntityRegistry.Player;
            if (p == _playerGo) return;
            _playerGo = p;
            _health = p != null ? p.GetComponent<Health>() : null;
            _mana = p != null ? p.GetComponent<Mana>() : null;
            _dash = p != null ? p.GetComponent<DashAbility>() : null;
            _caster = p != null ? p.GetComponent<SpellCaster>() : null;
            _controller = p != null ? p.GetComponent<PlayerController>() : null;
            _body = p != null ? p.GetComponent<Rigidbody2D>() : null;
            _statuses = p != null ? p.GetComponent<StatusEffectManager>() : null;
        }

        private Valkur.Gameplay.World.ZoneManager _zones;
        private float _nextZoneLookup;

        /// <summary>
        /// The zone manager is NOT in the ServiceLocator (measured live: the locator answered
        /// null while one was in the scene, and ZONA read "-" in the Lobby), so this falls back to
        /// a scene search the way the quest locator does — cached, and retried at most once a
        /// second so a scene without one does not pay a search five times a second.
        /// </summary>
        private Valkur.Gameplay.World.ZoneManager ResolveZones()
        {
            if (_zones != null) return _zones;
            _zones = ServiceLocator.Get<Valkur.Gameplay.World.ZoneManager>();
            if (_zones != null || Time.unscaledTime < _nextZoneLookup) return _zones;
            _nextZoneLookup = Time.unscaledTime + 1f;
            _zones = FindObjectOfType<Valkur.Gameplay.World.ZoneManager>();
            return _zones;
        }

        /// <summary>Returns true when the status row appeared or went, which changes the layout.</summary>
        private bool FillPlayer(Color text, Color dim)
        {
            var rows = _playerRows;
            if (_playerGo == null)
            {
                _player.SetSummary(DebugHudText.Waiting, dim);
                for (int i = 0; i < rows.Length; i++) { rows[i].Set(1, "-", dim); rows[i].Set(3, "", dim); }
                return SetStatusRow(false);
            }

            _player.SetSummary(DebugHudText.Fold(PlayerSelectionState.SelectedPlayerKey, 16), dim);

            Vector3 pos = _playerGo.transform.position;
            var zones = ResolveZones();
            string zone = zones != null ? zones.CurrentZone : null;
            rows[0].Set(1, string.IsNullOrEmpty(zone) ? "-" : DebugHudText.Fold(zone, 12), text);
            rows[0].Set(3, DebugHudText.Coord(pos.x) + " " + DebugHudText.Coord(pos.y), text);

            float speed = _body != null ? _body.velocity.magnitude : 0f;
            rows[1].Set(1, DebugHudText.Coord(speed), text);
            rows[1].Set(2, "", dim);
            rows[1].Set(3, DebugHudText.Stance(PlayerStance.IsPeace), text);

            rows[2].Set(1, _health != null ? _health.CurrentHp + "/" + _health.MaxHp : "-", text);
            rows[2].Set(3, _mana != null ? Mathf.RoundToInt(_mana.CurrentMana) + "/" + Mathf.RoundToInt(_mana.MaxMana) : "-", text);

            _sb.Clear();
            if (_health != null && _health.IsInvincible) _sb.Append(DebugHudText.Invincible);
            if (_statuses != null)
            {
                _statuses.CopyActiveTo(_statusBuffer);
                float now = Time.time;
                for (int i = 0; i < _statusBuffer.Count && _sb.Length < 30; i++)
                {
                    var e = _statusBuffer[i];
                    if (e == null) continue;
                    if (_sb.Length > 0) _sb.Append("  ");
                    _sb.Append(DebugHudText.Status(e.Kind)).Append(' ')
                       .Append(Mathf.Max(0f, e.EndTime - now).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            bool any = _sb.Length > 0;
            if (any) rows[3].Set(0, _sb.ToString(), _style.warn);
            return SetStatusRow(any);
        }

        private bool SetStatusRow(bool show)
        {
            if (show == _showStatusRow) return false;
            _showStatusRow = show;
            return true;
        }

        // -- Combat -----------------------------------------------------------------------

        private void FillCombat(Color text, Color dim)
        {
            _combat.SetSummary(_caster != null ? DebugHudText.Phase((int)_caster.CurrentPhase) : "", dim);
            if (_controller == null || _caster == null)
            {
                for (int i = 0; i < 3; i++) { _combatRows[i].Set(1, DebugHudText.Empty, dim); _combatRows[i].Set(2, "", dim); }
            }
            else
            {
                FillSpellRow(_combatRows[0], _controller.PrimarySpellKeyNow, text, dim);
                FillSpellRow(_combatRows[1], PlayerController.SecondarySpellKey, text, dim);
                FillSpellRow(_combatRows[2], PlayerController.MiddleSpellKey, text, dim);
            }

            var dashRow = _combatRows[3];
            if (_dash == null)
            {
                dashRow.Set(1, DebugHudText.Empty, dim);
                dashRow.Set(2, "", dim);
                return;
            }
            // The label already says DASH; the name cell carries its full recharge instead.
            dashRow.Set(1, "RECARGA " + _dash.CooldownTotal.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "S", dim);
            if (_dash.IsDashing) dashRow.Set(2, DebugHudText.Dashing, _style.warn);
            else if (_dash.CanDash) dashRow.Set(2, DebugHudText.Ready, _style.good);
            else dashRow.Set(2, _dash.CooldownRemaining.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "S",
                             _style.warn);
        }

        /// <summary>
        /// One mouse button: the spell it casts and the state the PLAYER PANEL would show for it —
        /// not learned, cooling, short of mana, or ready — read from the same seams
        /// (<c>KnowsSpell</c>, the BOOK cooldown, <c>ResolveManaCost</c>). The old rows read the
        /// caster's internal slots, where 1-3 are empty for the player and read "ready".
        /// </summary>
        private void FillSpellRow(DebugHudRow row, string key, Color text, Color dim)
        {
            if (string.IsNullOrEmpty(key))
            {
                row.Set(1, DebugHudText.Empty, dim);
                row.Set(2, "", dim);
                return;
            }
            var spell = _caster.GetSpellByKey(key);
            string name = spell != null && !string.IsNullOrEmpty(spell.displayName) ? spell.displayName : key;
            row.Set(1, DebugHudText.Fold(name, 18), text);

            if (!_caster.KnowsSpell(key)) { row.Set(2, DebugHudText.Locked, dim); return; }
            float cd = _caster.GetBookCooldownRemaining(key);
            if (cd > 0.01f)
            {
                row.Set(2, cd.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "S", _style.warn);
                return;
            }
            if (spell != null && _mana != null && _caster.ResolveManaCost(spell) > _mana.CurrentMana)
            {
                row.Set(2, DebugHudText.NoMana, _style.bad);
                return;
            }
            row.Set(2, DebugHudText.Ready, _style.good);
        }

        // -- Nearby -----------------------------------------------------------------------

        private bool FillNearby(Color text, Color dim)
        {
            int hostiles = 0, neutrals = 0, allies = 0;
            if (_playerGo != null)
            {
                DebugHudNearby.Collect(EntityRegistry.Monsters, _playerGo.transform.position, _style.nearbyRadius,
                                       _nearRows.Length, EntityFaction.SideOf, _nearby,
                                       out hostiles, out neutrals, out allies);
            }
            else _nearby.Clear();

            _near.SetSummary("H" + hostiles + " N" + neutrals + " A" + allies, dim);

            int shown = Mathf.Max(1, _nearby.Count);
            for (int i = 0; i < _nearRows.Length; i++)
            {
                var row = _nearRows[i];
                if (i >= _nearby.Count)
                {
                    if (i == 0)
                    {
                        _nearGlyphs[0].enabled = false;
                        row.Set(0, DebugHudText.NobodyNear, dim);
                        row.ClearFrom(1);
                    }
                    continue;
                }
                var e = _nearby[i];
                var sideColour = e.Side == FactionSide.Hostile ? _style.hostile
                               : e.Side == FactionSide.Neutral ? _style.neutral : _style.ally;
                var glyph = _nearGlyphs[i];
                glyph.enabled = true;
                glyph.sprite = e.Side == FactionSide.Hostile ? _dart.Hostile
                             : e.Side == FactionSide.Neutral ? _dart.Neutral : _dart.Ally;
                glyph.color = sideColour;

                row.Set(0, DebugHudText.Fold(e.Go.name, 12), sideColour);
                var hp = e.Go.GetComponent<Health>();
                row.Set(1, hp != null ? hp.CurrentHp + "/" + hp.MaxHp : "?", text);
                var brain = e.Go.GetComponent<FSMMonsterBrain>();
                row.Set(2, brain != null ? DebugHudText.FsmState(brain.CurrentStateName) : "-", dim);
                row.Set(3, DebugHudText.Coord(e.Distance), text);
            }

            if (shown == _nearShown) return false;
            _nearShown = shown;
            return true;
        }
    }
}
