using System.Text;
using UnityEngine;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    public partial class DebugHUD
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Text builder
        // ─────────────────────────────────────────────────────────────────────

        private partial void BuildText()
        {
            _sb.Clear();

            // ── Performance Section ──
            AppendHeader("PERFORMANCE");

            string fpsCol = FpsColorHex(_currentFps);
            _sb.Append(Label("FPS "));
            _sb.Append($"<color={fpsCol}><b>{_currentFps:F0}</b></color>");
            _sb.Append(Dim($"  ({_currentMs:F1}ms)"));

            var perf = PerformanceMonitor.Instance;
            if (perf != null)
            {
                _sb.Append(Label("   p95 "));
                _sb.Append(Val($"{perf.P95FrameTimeMs:F1}ms"));
                _sb.Append(Label("  p99 "));
                _sb.Append(Val($"{perf.P99FrameTimeMs:F1}ms"));
            }
            _sb.AppendLine();

            int entityCount = EntityRegistry.MonsterCount + (EntityRegistry.HasPlayer ? 1 : 0);
            _sb.Append(Label("Entities "));
            _sb.Append(Val($"{entityCount}"));
            _sb.Append(Label("   Time "));
            _sb.Append(Val($"{Time.time:F0}s"));
            if (perf != null)
            {
                _sb.Append(Label("   GC "));
                _sb.Append(Val($"{System.GC.CollectionCount(0)}"));
            }
            _sb.AppendLine();

            AppendSeparator();

            // ── Player Section ──
            if (_player == null)
            {
                AppendHeader("PLAYER");
                _sb.AppendLine(Dim("  Waiting for player..."));
            }
            else
            {
                AppendHeader("PLAYER");

                Vector3 pos = _player.transform.position;
                Vector2 vel = _rb != null ? _rb.velocity : Vector2.zero;
                _sb.Append(Label("Pos "));
                _sb.Append(Val($"{pos.x:F1}, {pos.y:F1}"));
                _sb.Append(Label("   Vel "));
                _sb.Append(Val($"{vel.magnitude:F1}"));
                _sb.Append(Dim($" ({vel.x:F1}, {vel.y:F1})"));
                _sb.AppendLine();

                _sb.Append(Label("Class "));
                _sb.Append(Val(PlayerSelectionState.SelectedPlayerKey));
                _sb.AppendLine();

                if (_health != null)
                {
                    float hpPct = _health.NormalizedHp;
                    string hpHex = hpPct > 0.3f ? ColorHex(COL_HP) : ColorHex(COL_HP_LOW);
                    _sb.Append(Label("HP  "));
                    _sb.Append($"<color={hpHex}>{ProgressBar(hpPct, 12)} {_health.CurrentHp}/{_health.MaxHp}</color>");
                    _sb.AppendLine();
                }

                if (_mana != null)
                {
                    float mpPct = _mana.NormalizedMana;
                    string mpHex = ColorHex(COL_MP);
                    _sb.Append(Label("MP  "));
                    _sb.Append($"<color={mpHex}>{ProgressBar(mpPct, 12)} {_mana.CurrentMana}/{_mana.MaxMana}</color>");
                    _sb.AppendLine();
                }

                AppendSeparator();
                AppendHeader("COMBAT");

                if (_melee != null)
                {
                    bool ready = _melee.CanAttack;
                    string stateCol = ready ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                    string stateTag = ready ? "RDY" : $"{_melee.CooldownRemaining:F1}s";
                    _sb.Append(Label("Melee "));
                    _sb.Append(Val($"dmg={_melee.Damage}"));
                    _sb.Append($"  <color={stateCol}>{stateTag}</color>");
                    _sb.AppendLine();
                }

                if (_dash != null)
                {
                    bool ready = _dash.CanDash;
                    string stateCol = ready ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                    string stateTag = _dash.IsDashing ? "DASH" : ready ? "RDY" : $"{_dash.CooldownRemaining:F1}s";
                    _sb.Append(Label("Dash  "));
                    _sb.Append($"<color={stateCol}>{stateTag}</color>");
                    _sb.AppendLine();
                }

                if (_spellCaster != null)
                {
                    for (int i = 0; i < _spellCaster.SlotCount; i++)
                    {
                        float cd = _spellCaster.GetCooldownRemaining(i);
                        string name = _spellCaster.GetSlotName(i);
                        bool rdy = cd <= 0.01f;
                        string stateCol = rdy ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                        string stateTag = rdy ? "RDY" : $"{cd:F1}s";
                        _sb.Append(Label($"  [{i + 1}] "));
                        _sb.Append(Val($"{name,-14}"));
                        _sb.Append($"<color={stateCol}>{stateTag}</color>");
                        _sb.AppendLine();
                    }
                }

                AppendSeparator();
                AppendHeader("NEARBY");

                var monsters = EntityRegistry.Monsters;
                int shown = 0;
                for (int i = 0; i < monsters.Count; i++)
                {
                    var m = monsters[i];
                    if (m == null) continue;
                    if (shown >= 5) { _sb.AppendLine(Dim("  ...")); break; }

                    float dist = Vector2.Distance(_player.transform.position, m.transform.position);
                    if (dist > 15f) continue;

                    var mHealth = m.GetComponent<Health>();
                    var mBrain = m.GetComponent<FSMMonsterBrain>();

                    string hpStr = mHealth != null ? $"{mHealth.CurrentHp}/{mHealth.MaxHp}" : "?";
                    string stateStr = mBrain != null ? mBrain.CurrentStateName : "?";

                    string monHex = ColorHex(COL_MONSTER);
                    _sb.Append($"  <color={monHex}>{m.name,-14}</color>");
                    _sb.Append(Label(" HP "));
                    _sb.Append(Val($"{hpStr,-7}"));
                    _sb.Append(Label(" "));
                    _sb.Append(Dim($"{stateStr,-8}"));
                    _sb.Append(Label(" d="));
                    _sb.Append(Val($"{dist:F0}"));
                    _sb.AppendLine();
                    shown++;
                }
                if (shown == 0)
                    _sb.AppendLine(Dim("  No enemies nearby"));
            }

            if (_text != null)
                _text.text = _sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Formatting helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string ColorHex(Color c) => $"#{ColorUtility.ToHtmlStringRGB(c)}";

        private static string FpsColorHex(float fps)
        {
            if (fps >= 55f) return ColorHex(COL_FPS_GOOD);
            if (fps >= 30f) return ColorHex(COL_FPS_WARN);
            return ColorHex(COL_FPS_BAD);
        }

        private static string Label(string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_LABEL)}>{s}</color>";
        private static string Val(string s)   => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_VALUE)}>{s}</color>";
        private static string Dim(string s)   => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_DIM)}>{s}</color>";

        private void AppendHeader(string title)
        {
            string hex = ColorHex(COL_HEADER);
            _sb.AppendLine($"<color={hex}><b>{title}</b></color>");
        }

        private void AppendSeparator()
        {
            string hex = ColorHex(COL_SEPARATOR);
            _sb.AppendLine($"<color={hex}>────────────────────────────────</color>");
        }

        private static string ProgressBar(float ratio, int width)
        {
            ratio = Mathf.Clamp01(ratio);
            int filled = Mathf.RoundToInt(ratio * width);
            int empty = width - filled;
            return "\u2588" + new string('\u2588', Mathf.Max(0, filled - 1))
                 + new string('\u2591', empty);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI construction + lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void CreateOverlay()
        {
            var canvasGo = new GameObject("DebugHUDCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var panelGo = new GameObject("DebugPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-12f, -12f);
            panelRect.sizeDelta = new Vector2(380f, 0f);

            _bg = panelGo.AddComponent<UnityEngine.UI.Image>();
            _bg.color = COL_BG;

            var csf = panelGo.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("DebugText", typeof(RectTransform));
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 13;
            _text.color = COL_VALUE;
            _text.font = TMP_Settings.defaultFontAsset;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = false;

            UILayerHelper.SetUILayerRecursive(canvasGo);
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.richText = true;
            _text.lineSpacing = -8f;

            // Start hidden — F9 toggles visibility
            canvasGo.SetActive(false);
        }

        private void OnDisable()
        {
            // Only touch the action's enable state if we OWN it (ad-hoc EditMode
            // fallback). When the action came from the canonical InputService.Editors
            // map it is shared with every other F-key consumer — disabling it here
            // would break their hotkeys (e.g. EntitiesRuntimeEditor F5) and trip
            // EditMode test assertions that check action.enabled across siblings.
            if (_ownsToggleAction) _toggleAction?.Disable();
        }

        private void OnEnable()
        {
            if (_ownsToggleAction) _toggleAction?.Enable();
        }
    }
}
