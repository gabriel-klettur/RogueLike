using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// What the window does when something HAPPENS. Every effect answers an event — a pickup, an
    /// equip, a potion, gold, a refusal — and dies inside a second; nothing here runs at rest.
    ///
    /// <para><b>Pickups are found by DIFFERENCE, not by listening.</b> Items reach the bag from a
    /// dozen places (the world, a vendor, crafting, a quest reward, a save restore), and only some
    /// raise an event. The window keeps a picture of every slot and compares on each
    /// <see cref="Inventory.OnInventoryChanged"/>: a slot that GAINED something the player did not
    /// just move there is a pickup. The window's own actions are bracketed by
    /// <see cref="BeginAction"/>/<see cref="EndAction"/> so a drag is never announced as a find.</para>
    /// </summary>
    public partial class InventoryUI
    {
        private ItemDefinition[] _snapItem = new ItemDefinition[0];
        private int[] _snapQty = new int[0];
        private bool[] _newFlag = new bool[0];
        private int _actionDepth;
        private float _pickupSoundCooldown;

        private float _shakeLeft;
        private float _shakeSign = 1f;
        private float _statFloatArmed;
        private readonly float[] _statBefore = new float[4];

        // ── Frame ─────────────────────────────────────────────────────────

        /// <summary>Advances every widget. Public so an EditMode test can drive the window.</summary>
        public void Tick(float dt)
        {
            if (!_built) return;
            TickOpen(dt);
            if (_pickupSoundCooldown > 0f) _pickupSoundCooldown -= dt;
            if (_statFloatArmed > 0f) _statFloatArmed -= dt;
            if (!_visible && _openT <= 0f) return;

            for (int i = 0; i < _bagViews.Length; i++) _bagViews[i].Tick(dt);
            for (int i = 0; i < _equipViews.Length; i++) _equipViews[i].Tick(dt);
            _motes.Tick(dt);
            _floats.Tick(dt);
            _medallion.Tick(dt, _gridStyle);
            _xpBar.Tick(dt, _gridStyle);
            TickStats(dt);
            TickGold(dt);
            TickHover(dt);
            TickShake(dt);
            TickFigureFlash(dt);
        }

        // ── Snapshot and pickups ───────────────────────────────────────────

        private void TakeSnapshot()
        {
            if (_playerInventory == null) return;
            int bag = _playerInventory.Capacity, eq = Inventory.EquipmentCapacity;
            if (_snapItem.Length != bag + eq)
            {
                _snapItem = new ItemDefinition[bag + eq];
                _snapQty = new int[bag + eq];
            }
            if (_newFlag.Length != bag) _newFlag = new bool[bag];
            for (int i = 0; i < bag + eq; i++)
            {
                var s = _playerInventory.GetSlotByIndex(i);
                _snapItem[i] = s.IsEmpty ? null : s.Item;
                _snapQty[i] = s.IsEmpty ? 0 : s.Quantity;
            }
        }

        /// <summary>Brackets one of the window's own changes so it is not announced as a pickup.</summary>
        private void BeginAction() => _actionDepth++;

        private void EndAction()
        {
            _actionDepth = Mathf.Max(0, _actionDepth - 1);
            if (_actionDepth == 0) TakeSnapshot();
        }

        private void OnInventoryChanged()
        {
            if (_playerInventory == null) return;
            // A bag that goes completely empty is Initialize: a save restore or the starting kit
            // follows in the same frame, slot by slot, and none of it was just FOUND. Without this,
            // loading a save marked the whole bag new and lit the tray badge with its item count.
            if (IsCompletelyEmpty()) _quietFrame = Time.frameCount;
            bool quiet = _quietFrame == Time.frameCount;
            if (_actionDepth == 0 && !quiet && _snapItem.Length == _playerInventory.Capacity + Inventory.EquipmentCapacity)
            {
                int bag = _playerInventory.Capacity;
                for (int i = 0; i < bag; i++)
                {
                    var s = _playerInventory.GetSlotByIndex(i);
                    int qty = s.IsEmpty ? 0 : s.Quantity;
                    bool gained = !s.IsEmpty && (s.Item != _snapItem[i] ? true : qty > _snapQty[i]);
                    if (!gained) continue;
                    int n = s.Item != _snapItem[i] ? qty : qty - _snapQty[i];
                    // An item that merely moved from another slot of the bag is not a find.
                    if (MovedFromElsewhere(s.Item, i)) continue;
                    if (i < _newFlag.Length) _newFlag[i] = true;
                    if (_visible) AnnouncePickup(i, s.Item, n);
                }
            }
            TakeSnapshot();
            if (_visible) RefreshAll(announce: false);
            RefreshTrayBadge();
        }

        private int _quietFrame = -1;

        private bool IsCompletelyEmpty()
        {
            int total = _playerInventory.Capacity + Inventory.EquipmentCapacity;
            for (int i = 0; i < total; i++)
                if (!_playerInventory.GetSlotByIndex(i).IsEmpty) return false;
            return true;
        }

        /// <summary>True when the bag holds no more of this item than the snapshot did overall.</summary>
        private bool MovedFromElsewhere(ItemDefinition item, int slot)
        {
            int before = 0, now = 0;
            for (int i = 0; i < _snapItem.Length; i++) if (_snapItem[i] == item) before += _snapQty[i];
            int total = _playerInventory.Capacity + Inventory.EquipmentCapacity;
            for (int i = 0; i < total; i++)
            {
                var s = _playerInventory.GetSlotByIndex(i);
                if (!s.IsEmpty && s.Item == item) now += s.Quantity;
            }
            return now <= before;
        }

        private void AnnouncePickup(int unified, ItemDefinition item, int quantity)
        {
            var view = ViewFor(unified);
            if (view == null || item == null) return;
            var rarity = _theme.RarityColour(item.rarity);
            view.Flash(Color.Lerp(rarity, Color.white, 0.35f), _style.slotFlashSeconds);
            _floats.Spawn("+" + Mathf.Max(1, quantity), view.Centre + new Vector2(0f, view.Size * 0.5f), rarity);

            bool rare = item.rarity >= ItemRarity.Rare;
            int motes = item.rarity == ItemRarity.Legendary ? _style.motesOnLegendary
                      : rare ? _style.motesOnRare : _style.motesOnPickup;
            Burst(view.Centre, rarity, motes, rare ? HudMoteShape.Star : HudMoteShape.Dot, spread: rare ? 1.4f : 1f);
            if (rare) Ring(view.Centre, rarity, item.rarity == ItemRarity.Legendary ? 18 : 10, view.Size * 0.5f);
            if (_pickupSoundCooldown <= 0f)
            {
                InventoryAudio.Play(rare ? InventorySound.PickupRare : InventorySound.Pickup, _style);
                _pickupSoundCooldown = 0.08f;
            }
        }

        private bool IsNewFlag(int i) => i >= 0 && i < _newFlag.Length && _newFlag[i];

        private void ClearNewFlag(int unified)
        {
            if (unified >= 0 && unified < _newFlag.Length) _newFlag[unified] = false;
            RefreshTrayBadge();
        }

        /// <summary>The tray button counts what was picked up and not yet looked at.</summary>
        private void RefreshTrayBadge()
        {
            var bar = Valkur.UIKit.HUDIconBar.Instance;
            if (bar == null || _playerInventory == null) return;
            int n = 0;
            var bag = _playerInventory.Slots;
            for (int i = 0; i < _newFlag.Length && i < bag.Count; i++)
            {
                if (bag[i].IsEmpty) _newFlag[i] = false;
                else if (_newFlag[i]) n++;
            }
            bar.SetBadge("inventory", _visible ? 0 : n);
        }

        // ── The window's own actions ───────────────────────────────────────

        private void OnEquipped(int fromUnified, int toUnified, ItemDefinition item)
        {
            var from = ViewFor(fromUnified);
            var to = ViewFor(toUnified);
            var rarity = item != null ? _theme.RarityColour(item.rarity) : _theme.gold;
            if (to != null)
            {
                to.Flash(Color.Lerp(rarity, Color.white, 0.4f), _style.slotFlashSeconds * 1.3f);
                if (from != null) Trail(from.Centre, to.Centre, rarity, _style.motesOnEquip);
                Burst(to.Centre, _theme.gold, _style.motesOnEquip / 2, HudMoteShape.Plus, 0.8f);
            }
            FlashFigure(Color.Lerp(rarity, Color.white, 0.5f));
            ArmStatFloats();
            InventoryAudio.Play(InventorySound.Equip, _style);
            SelectSlot(toUnified);
        }

        private void OnUnequipped(int fromUnified, int toUnified, ItemDefinition item)
        {
            var from = ViewFor(fromUnified);
            var to = ViewFor(toUnified);
            if (from != null && to != null) Trail(from.Centre, to.Centre, _theme.textDim, _style.motesOnUnequip);
            to?.Flash(new Color(1f, 1f, 1f, 0.45f), _style.slotFlashSeconds);
            ArmStatFloats();
            InventoryAudio.Play(InventorySound.Unequip, _style);
        }

        private void OnConsumed(int unified, ItemDefinition item)
        {
            var view = ViewFor(unified);
            if (view == null) return;
            Color c = item.healing > 0f
                ? (WorldBarStyle.Active != null ? WorldBarStyle.Active.HealthFor(Valkur.Core.UI.WorldBarRank.Player) : _theme.success)
                : item.mana > 0f ? (WorldBarStyle.Active != null ? WorldBarStyle.Active.mana : _theme.info)
                : _theme.gold;
            view.Flash(Color.Lerp(c, Color.white, 0.3f), _style.slotFlashSeconds);
            for (int i = 0; i < _style.motesOnConsume; i++)
            {
                var p = view.Centre + new Vector2(Random.Range(-view.Size * 0.35f, view.Size * 0.35f), -view.Size * 0.2f);
                var v = new Vector2(Random.Range(-4f, 4f), Random.Range(0.6f, 1f) * _style.moteSpeedTexels);
                _motes.Emit(p, v, c, _style.moteLifeSeconds * 1.2f, HudMoteShape.Plus, -_style.moteGravityTexels * 0.3f, 1.5f);
            }
            InventoryAudio.Play(InventorySound.Consume, _style);
        }

        private void OnSettled(int unified)
        {
            var view = ViewFor(unified);
            if (view == null) return;
            view.Flash(new Color(1f, 1f, 1f, 0.35f), _style.slotFlashSeconds * 0.8f);
            for (int i = 0; i < _style.motesOnSettle; i++)
            {
                var p = view.Centre + new Vector2(Random.Range(-view.Size * 0.4f, view.Size * 0.4f), -view.Size * 0.42f);
                _motes.Emit(p, new Vector2(Random.Range(-10f, 10f), Random.Range(4f, 10f)),
                            new Color(1f, 0.95f, 0.85f, 0.7f), 0.3f, HudMoteShape.Dot, _style.moteGravityTexels, 2f);
            }
            InventoryAudio.Play(InventorySound.Settle, _style);
        }

        private void OnDropped(int unified)
        {
            var view = ViewFor(unified);
            if (view != null)
                for (int i = 0; i < _style.motesOnSettle; i++)
                    _motes.Emit(view.Centre, Random.insideUnitCircle * 14f, new Color(0.8f, 0.8f, 0.85f, 0.7f),
                                0.3f, HudMoteShape.Dot, 0f, 3f);
            InventoryAudio.Play(InventorySound.Drop, _style);
        }

        private void OnSorted()
        {
            for (int i = 0; i < _bagViews.Length; i++)
                if (_bagViews[i].Item != null)
                    _bagViews[i].Flash(new Color(1f, 1f, 1f, 0.22f + 0.1f * (i % 3)), _style.slotFlashSeconds);
            InventoryAudio.Play(InventorySound.Sort, _style);
        }

        /// <summary>
        /// A refusal is SAID, never silent: the slot flashes red, the window is knocked a texel,
        /// a short line says why, and a low knock plays. An error is not celebrated, so no motes.
        /// </summary>
        private void Refuse(int unified, EquipRefusal why, ItemDefinition item)
        {
            var view = ViewFor(unified);
            if (view != null) view.Flash(_theme.danger, _style.refusalSeconds);
            _shakeLeft = _style.refusalSeconds;
            _shakeSign = -_shakeSign;
            string line = RefusalLine(why, item);
            if (view != null && !string.IsNullOrEmpty(line))
                _floats.Spawn(line, view.Centre + new Vector2(0f, view.Size * 0.5f), _theme.danger);
            InventoryAudio.Play(InventorySound.Refuse, _style);
        }

        /// <summary>The refusal line, in the bitmap face (capitals, no accents).</summary>
        internal static string RefusalLine(EquipRefusal why, ItemDefinition item)
        {
            switch (why)
            {
                case EquipRefusal.LevelTooLow:
                    return "NIVEL " + EquipmentLayout.RequiredLevel(item);
                case EquipRefusal.WrongSlot:
                    int home = EquipmentLayout.IndexFor(item);
                    return home >= 0 ? "VA EN " + SlotShortName((EquipmentSlotKind)home) : "NO VA AHI";
                case EquipRefusal.NotWearable:
                    return "NO SE EQUIPA";
                case EquipRefusal.BagFull:
                    return "MOCHILA LLENA";
                default:
                    return "NO CABE";
            }
        }

        private static string SlotShortName(EquipmentSlotKind k)
        {
            switch (k)
            {
                case EquipmentSlotKind.Head: return "CABEZA";
                case EquipmentSlotKind.Amulet: return "CUELLO";
                case EquipmentSlotKind.Weapon: return "ARMA";
                case EquipmentSlotKind.Offhand: return "MANO IZQ.";
                case EquipmentSlotKind.Chest: return "TORSO";
                case EquipmentSlotKind.Ring: return "ANILLO";
                case EquipmentSlotKind.Boots: return "PIES";
                default: return "ABALORIO";
            }
        }

        private void TickShake(float dt)
        {
            if (_shakeLeft <= 0f) return;
            _shakeLeft -= dt;
            ApplyContentOffset();
        }

        private float ShakeOffset()
        {
            if (_shakeLeft <= 0f || _style.refusalShakeTexels <= 0) return 0f;
            int phase = Mathf.FloorToInt(_shakeLeft * 36f) & 1;
            return (phase == 0 ? 1 : -1) * _style.refusalShakeTexels * _shakeSign;
        }

        // ── Stats counting after an equip ──────────────────────────────────

        private void ArmStatFloats()
        {
            _statFloatArmed = 0.6f;
            for (int i = 0; i < 4; i++) _statBefore[i] = _statTarget[i];
        }

        private void OnStatsChanged()
        {
            if (!_visible || !_built) { _statsSeeded = false; return; }
            UpdateStats(instant: false);
            if (_statFloatArmed <= 0f) return;
            for (int i = 0; i < 4; i++)
            {
                float d = _statTarget[i] - _statBefore[i];
                int di = Mathf.RoundToInt(d);
                if (di == 0) continue;
                var rt = _statText[i].rectTransform;
                var at = rt.anchoredPosition + new Vector2(12f, 9f);
                _floats.Spawn((di > 0 ? "+" : "") + di, at, di > 0 ? _theme.success : _theme.danger);
                _statBefore[i] = _statTarget[i];
            }
        }

        // ── Gold ───────────────────────────────────────────────────────────

        private void OnCoinsChanged(int delta)
        {
            if (!_visible || !_built) { _goldSeeded = false; return; }
            UpdateFooter(instantGold: false);
            if (delta == 0 || _coin == null) return;
            var centre = _coin.rectTransform.anchoredPosition + _coin.rectTransform.sizeDelta * 0.5f;
            if (delta > 0)
            {
                int motes = Mathf.Abs(delta) >= _style.coinBurstThreshold ? _style.motesOnCoins : 2;
                Burst(centre, Color.Lerp(_theme.gold, Color.white, 0.3f), motes, HudMoteShape.Star, 0.7f);
                InventoryAudio.Play(InventorySound.Coin, _style, Mathf.Abs(delta) >= _style.coinBurstThreshold ? 1f : 0.6f);
            }
            _floats.Spawn((delta > 0 ? "+" : "") + delta, centre + new Vector2(-8f, 6f),
                          delta > 0 ? _theme.gold : _theme.danger);
        }

        // ── The paper doll lighting up ─────────────────────────────────────

        private float _figureFlash;
        private Color _figureFlashColour;

        private void FlashFigure(Color c)
        {
            _figureFlash = 0.35f;
            _figureFlashColour = c;
        }

        private void TickFigureFlash(float dt)
        {
            if (_figure == null) return;
            if (_figureFlash <= 0f)
            {
                if (_figure.color != Color.white) _figure.color = Color.white;
                return;
            }
            _figureFlash -= dt;
            float t = Mathf.Clamp01(_figureFlash / 0.35f);
            // Toward the flash colour and back: the figure glints, it does not change colour.
            _figure.color = Color.Lerp(Color.white, _figureFlashColour, t * 0.6f);
        }

        // ── Mote shapes ────────────────────────────────────────────────────

        private void Burst(Vector2 at, Color c, int count, HudMoteShape shape, float spread)
        {
            if (_motes == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.value * 0.6f) / Mathf.Max(1, count) * Mathf.PI * 2f;
                float speed = _style.moteSpeedTexels * Random.Range(0.55f, 1f) * spread;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed + new Vector2(0f, 8f);
                _motes.Emit(at, v, c, _style.moteLifeSeconds * Random.Range(0.8f, 1.15f), shape,
                            _style.moteGravityTexels, 1.2f, twinkle: shape == HudMoteShape.Star);
            }
        }

        private void Ring(Vector2 at, Color c, int count, float radius)
        {
            if (_motes == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(at + dir * radius, dir * (_style.moteSpeedTexels * 0.9f), c, _style.moteLifeSeconds * 0.8f,
                            HudMoteShape.Dot, 0f, 2.2f);
            }
        }

        private void Trail(Vector2 from, Vector2 to, Color c, int count)
        {
            if (_motes == null) return;
            var d = to - from;
            float travel = 0.32f;
            for (int i = 0; i < count; i++)
            {
                float k = i / (float)Mathf.Max(1, count);
                var p = from + d * (k * 0.25f) + Random.insideUnitCircle * 2f;
                var v = d / travel * (1f - k * 0.4f);
                _motes.Emit(p, v, c, travel * (1f - k * 0.3f), i % 3 == 0 ? HudMoteShape.Star : HudMoteShape.Dot,
                            0f, 0f, twinkle: i % 3 == 0);
            }
        }
    }
}
