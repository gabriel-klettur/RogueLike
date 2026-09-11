using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Pushes the model into the widgets. Every push writes only what changed (the views compare
    /// before writing), so a refresh on an unrelated inventory change costs a pass of compares,
    /// not a canvas rebuild.
    /// </summary>
    public partial class InventoryUI
    {
        private static readonly string[] CURRENCY_ITEM_IDS = { "gold", "coins", "coin", "gold_coin" };

        private readonly float[] _statShown = new float[4];
        private readonly float[] _statTarget = new float[4];
        private readonly float[] _statFrom = new float[4];
        private float _statT = 1f;
        private bool _statsSeeded;

        private float _goldShown, _goldFrom, _goldTarget;
        private float _goldT = 1f;
        private bool _goldSeeded;

        private void RefreshAll(bool announce)
        {
            if (!_built) return;
            EnsureBagViews();
            UpdateSlots();
            UpdateHeader();
            UpdateStats(instant: !_statsSeeded);
            UpdateFooter(instantGold: !_goldSeeded);
            UpdateFigure(force: false);
            ApplyTabs();
            ApplyFilter();
            UpdateMarks();
        }

        private void UpdateSlots()
        {
            if (_playerInventory == null) return;
            var bag = _playerInventory.Slots;
            for (int i = 0; i < _bagViews.Length; i++)
            {
                var s = i < bag.Count ? bag[i] : default;
                _bagViews[i].SetContent(s.Item, s.Quantity, _theme, _pixelScale);
                _bagViews[i].SetNew(IsNewFlag(i), _theme);
            }
            var eq = _playerInventory.EquipmentSlots;
            for (int i = 0; i < _equipViews.Length; i++)
            {
                var s = i < eq.Count ? eq[i] : default;
                _equipViews[i].SetContent(s.Item, s.Quantity, _theme, _pixelScale);
            }
        }

        private void UpdateHeader()
        {
            if (!_built) return;
            if (_nameText != null)
            {
                string name = _playerDef != null && !string.IsNullOrEmpty(_playerDef.displayName)
                    ? _playerDef.displayName
                    : (PlayerSelectionState.SelectedPlayerKey ?? "Héroe");
                if (!string.IsNullOrEmpty(name)) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                _nameText.text = name;
            }
            if (_playerXp != null)
            {
                // The model's real level. It starts at 0, and the medallion on the player panel
                // says 0 — two readouts of one number must not disagree.
                _medallion.SetLevel(_playerXp.Level);
                _xpBar.SetRatio(_playerXp.NormalizedProgress, HudBarChange.Heal);
            }
        }

        // ── Stats: the four numbers in the header ──────────────────────────

        private float StatValue(int i)
        {
            if (_playerStats != null) return _playerStats.Get(_statKinds[i]);
            if (_playerGo == null) return 0f;
            switch (_statKinds[i])
            {
                case StatKind.MaxHp: { var h = _playerGo.GetComponent<Health>(); return h != null ? h.MaxHp : 0f; }
                case StatKind.MaxMana: { var m = _playerGo.GetComponent<Mana>(); return m != null ? m.MaxMana : 0f; }
                default: return 0f;
            }
        }

        /// <summary>Reads the stats; animates towards them unless <paramref name="instant"/>.</summary>
        private void UpdateStats(bool instant)
        {
            if (!_built) return;
            for (int i = 0; i < 4; i++)
            {
                float v = StatValue(i);
                if (instant) { _statShown[i] = _statFrom[i] = _statTarget[i] = v; }
                else if (!Mathf.Approximately(v, _statTarget[i]))
                {
                    _statFrom[i] = _statShown[i];
                    _statTarget[i] = v;
                    _statT = 0f;
                }
            }
            _statsSeeded = _statsSeeded || instant || _playerGo != null;
            WriteStats();
        }

        private void TickStats(float dt)
        {
            if (_statT >= 1f) return;
            _statT = Mathf.Min(1f, _statT + dt / Mathf.Max(0.01f, _style.countSeconds));
            float e = 1f - (1f - _statT) * (1f - _statT);
            for (int i = 0; i < 4; i++) _statShown[i] = Mathf.Lerp(_statFrom[i], _statTarget[i], e);
            WriteStats();
        }

        private void WriteStats()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_statText[i] == null) continue;
                _statText[i].SetText(Mathf.RoundToInt(_statShown[i]).ToString());
                bool counting = _statT < 1f && !Mathf.Approximately(_statFrom[i], _statTarget[i]);
                var c = !counting ? _theme.text
                      : _statTarget[i] > _statFrom[i] ? _theme.success : _theme.danger;
                _statText[i].SetColour(c);
            }
            LayoutStats();
        }

        /// <summary>
        /// Flows the four stat groups by their measured ink, so "200" never runs into the drop
        /// that follows it (fixed quarters did, in the first live capture). When the numbers are
        /// long the gaps shrink before anything overlaps.
        /// </summary>
        private void LayoutStats()
        {
            if (_statText[0] == null || _statHit[0] == null) return;
            int pad = _style.paddingTexels, inner = _widthTexels - pad * 2, y = _headerY;
            int content = 0;
            var ink = new int[4];
            for (int i = 0; i < 4; i++)
            {
                ink[i] = Mathf.Max(5, _statText[i].InkWidth);
                content += Mathf.RoundToInt(_statIcon[i].rectTransform.sizeDelta.x) + 2 + ink[i];
            }
            int gap = Mathf.Clamp((inner - content) / 3, 2, 7);
            int x = pad;
            for (int i = 0; i < 4; i++)
            {
                var icon = _statIcon[i].rectTransform;
                int iw = Mathf.RoundToInt(icon.sizeDelta.x), ih = Mathf.RoundToInt(icon.sizeDelta.y);
                PlaceIfMoved(icon, x, y + (9 - ih) / 2, iw, ih);
                PlaceIfMoved(_statText[i].rectTransform, x + iw + 2, y, ink[i] + 2, 9);
                PlaceIfMoved(_statHit[i], x, y, iw + 2 + ink[i] + 1, 9);
                x += iw + 2 + ink[i] + gap;
            }
        }

        /// <summary>The footer, flowed by measured ink: bag and count, weight, and the coin hugging the gold.</summary>
        private void LayoutFooter()
        {
            if (_capacityText == null || _capacityHit == null) return;
            int pad = _style.paddingTexels, y = _footerY, h = _style.footerTexels;
            int bw = Mathf.RoundToInt(_bagGlyph.rectTransform.sizeDelta.x);
            int capInk = Mathf.Max(5, _capacityText.InkWidth);
            int x = pad;
            PlaceIfMoved(_bagGlyph.rectTransform, x, y + (h - bw) / 2, bw, bw);
            PlaceIfMoved(_capacityText.rectTransform, x + bw + 2, y, capInk + 2, h);
            PlaceIfMoved(_capacityHit, x, y, bw + 2 + capInk + 2, h);
            x += bw + 2 + capInk + 7;

            int ww = Mathf.RoundToInt(_weightGlyph.rectTransform.sizeDelta.x);
            int wInk = Mathf.Max(3, _weightText.InkWidth);
            PlaceIfMoved(_weightGlyph.rectTransform, x, y + (h - ww) / 2, ww, ww);
            PlaceIfMoved(_weightText.rectTransform, x + ww + 2, y, wInk + 2, h);
            PlaceIfMoved(_weightHit, x, y, ww + 2 + wInk + 2, h);

            int gInk = Mathf.Max(5, _goldText.InkWidth);
            int cw = Mathf.RoundToInt(_coin.rectTransform.sizeDelta.x);
            int gx = _widthTexels - pad - gInk - 1;
            PlaceIfMoved(_goldText.rectTransform, gx, y, gInk + 1, h);
            PlaceIfMoved(_coin.rectTransform, gx - cw - 2, y + (h - cw) / 2, cw, cw);
        }

        private static void PlaceIfMoved(RectTransform rt, int x, int y, int w, int h)
        {
            if (rt == null) return;
            var p = new Vector2(x, y);
            var s = new Vector2(w, h);
            if (rt.anchoredPosition == p && rt.sizeDelta == s) return;
            HudRect.Place(rt, x, y, w, h);
        }

        // ── Footer: capacity, weight, gold ────────────────────────────────

        private void UpdateFooter(bool instantGold)
        {
            if (!_built) return;
            int used = _playerInventory != null ? _playerInventory.UsedSlots : 0;
            int cap = _playerInventory != null ? _playerInventory.Capacity : _bagViews.Length;
            _capacityText.SetText(used + "/" + cap);
            var capColour = used >= cap ? _theme.danger : used >= cap - 2 ? _theme.warning : _theme.text;
            _capacityText.SetColour(capColour);
            _bagGlyph.color = used >= cap ? _theme.danger : _theme.textDim;

            _weightText.SetText(InventoryItemCard.FormatWeight(TotalWeight()));

            float gold = TotalGold();
            if (instantGold) { _goldShown = _goldFrom = _goldTarget = gold; _goldT = 1f; }
            else if (!Mathf.Approximately(gold, _goldTarget))
            {
                _goldFrom = _goldShown;
                _goldTarget = gold;
                _goldT = 0f;
            }
            _goldSeeded = true;
            _goldText.SetText(Mathf.RoundToInt(_goldShown).ToString());
            LayoutFooter();
        }

        private void TickGold(float dt)
        {
            if (_goldT >= 1f) return;
            _goldT = Mathf.Min(1f, _goldT + dt / Mathf.Max(0.01f, _style.countSeconds));
            float e = 1f - (1f - _goldT) * (1f - _goldT);
            _goldShown = Mathf.Lerp(_goldFrom, _goldTarget, e);
            _goldText.SetText(Mathf.RoundToInt(_goldShown).ToString());
            LayoutFooter();
        }

        private int TotalGold()
        {
            int total = _playerWallet != null ? _playerWallet.Coins : 0;
            // Coin stacks carried as items count too (Python parity).
            if (_playerInventory != null)
            {
                var slots = _playerInventory.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].IsEmpty) continue;
                    string id = slots[i].Item.itemId;
                    for (int k = 0; k < CURRENCY_ITEM_IDS.Length; k++)
                        if (string.Equals(id, CURRENCY_ITEM_IDS[k], System.StringComparison.OrdinalIgnoreCase))
                        {
                            total += slots[i].Quantity;
                            break;
                        }
                }
            }
            return total;
        }

        private float TotalWeight()
        {
            if (_playerInventory == null) return 0f;
            float w = 0f;
            var bag = _playerInventory.Slots;
            for (int i = 0; i < bag.Count; i++)
                if (!bag[i].IsEmpty) w += bag[i].Item.weight * bag[i].Quantity;
            var eq = _playerInventory.EquipmentSlots;
            for (int i = 0; i < eq.Count; i++)
                if (!eq[i].IsEmpty) w += eq[i].Item.weight * eq[i].Quantity;
            return w;
        }

        // ── The figure on the paper doll ───────────────────────────────────

        /// <summary>Re-bakes the standing figure when the character's idle frame changed.</summary>
        private void UpdateFigure(bool force)
        {
            if (_figure == null) return;
            var sprite = ResolveFigureSprite(_playerGo);
            if (!force && sprite == _figureSource && (_figureTex != null || sprite == null)) return;
            _figureSource = sprite;
            HudLifetime.Release(_figureTex);
            _figureTex = null;
            if (sprite == null) { _figure.enabled = false; return; }
            var rt = _figure.rectTransform;
            _figureTex = HudTextureBaker.Figure(sprite, Mathf.RoundToInt(rt.sizeDelta.x), Mathf.RoundToInt(rt.sizeDelta.y),
                                                _theme.outline);
            _figure.texture = _figureTex;
            _figure.enabled = _figureTex != null;
        }

        /// <summary>The first EAST idle frame — facing into the window from the left column.</summary>
        internal static Sprite ResolveFigureSprite(GameObject player)
        {
            if (player == null) return null;
            var anim = player.GetComponentInChildren<DirectionalAnimator>();
            if (anim != null)
            {
                var frames = anim.IdleSprites.GetFrames(DirectionalAnimator.Direction.East);
                if (frames != null)
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null) return frames[i];
            }
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr == null) sr = player.GetComponentInChildren<SpriteRenderer>();
            return sr != null ? sr.sprite : null;
        }

        /// <summary>What the player presses for an action, from its live binding.</summary>
        private static string InputBindingLabel(InputAction action)
            => action != null ? InputBindingResolver.PrimaryLabel(action) : "";
    }
}
