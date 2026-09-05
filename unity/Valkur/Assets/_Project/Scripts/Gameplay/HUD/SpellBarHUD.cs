using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.Spells.UI;
using Valkur.UIKit;

namespace Valkur.Gameplay.UI
{
    /// <summary>
    /// World-of-Warcraft style action bar HUD.
    /// Displays a fixed grid (rows × cols) of spell slots populated from the
    /// player's <see cref="SpellCaster"/> (slots first, then spell-book entries).
    /// Each slot shows: icon, radial cooldown overlay, hotkey label, mana cost.
    /// Click a slot to cast the spell in the direction of the mouse.
    /// </summary>
    public partial class SpellBarHUD : SingletonMonoBehaviour<SpellBarHUD>
    {
        [Header("Grid")]
        [SerializeField, Tooltip("Number of action-bar rows (each row holds <cols> spells).")]
        private int rows = 2;
        [SerializeField, Tooltip("Number of slots per row.")]
        private int cols = 12;

        [Header("Layout")]
        [SerializeField] private float slotSize  = 40f;
        [SerializeField] private float slotGap   = 3f;
        [SerializeField] private float bottomPad = 14f;

        // ── Runtime UI ──
        private Canvas _canvas;
        private RectTransform _root;
        private CanvasGroup _rootCg;
        private SlotView[] _slotViews;

        // ── Player refs ──
        private SpellCaster _caster;
        private GameObject  _playerGo;
        private Camera      _mainCam;

        private float _refreshAccumulator;
        private const float REFRESH_PERIOD = 0.05f;

        private struct SlotView
        {
            public GameObject       Root;
            public Image            Bg;
            public Image            Icon;
            public Image            CdOverlay;
            public TextMeshProUGUI  CdText;
            public TextMeshProUGUI  HotkeyText;
            public TextMeshProUGUI  ManaText;
            public string           SpellKey;     // null = empty slot
            public int              SlotIndex;    // -1 = book-only
        }

        protected override void OnSingletonAwake()
        {
            BuildUI();
        }

        private void Start()
        {
            ResolvePlayer();
            Populate();
            RegisterTrayButton();
            // Start minimized: the action bar is opt-in via the HUD tray icon.
            // Authors who want it visible right away can click the spell-bar tray
            // button — the registered toggle (RegisterTrayButton above) flips it back.
            SetVisible(false);
        }

        private void RegisterTrayButton()
        {
            var bar = HUDIconBar.Instance;
            if (bar == null) return;
            var sprite = LoadHUDSprite("Assets/_Project/Art/UI/hud/spells_hud_button.png");
            bar.Register("spellbar", sprite, ToggleVisible, order: 1);
        }

        private void ToggleVisible()
        {
            if (_rootCg == null) return;
            SetVisible(_rootCg.alpha < 0.5f);
        }

        private void Update()
        {
            // Re-resolve player if it (re)spawned.
            if (_caster == null) { ResolvePlayer(); Populate(); return; }

            _refreshAccumulator += Time.unscaledDeltaTime;
            if (_refreshAccumulator >= REFRESH_PERIOD)
            {
                _refreshAccumulator = 0f;
                RefreshDynamic();
            }
        }

        public void SetVisible(bool visible)
        {
            if (_rootCg == null) return;
            _rootCg.alpha          = visible ? 1f : 0f;
            _rootCg.blocksRaycasts = visible;
            _rootCg.interactable   = visible;
        }

        private void MinimizeToTray()
        {
            SetVisible(false);
        }

        private static Sprite LoadHUDSprite(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
#else
            return null;
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Population & refresh
        // ─────────────────────────────────────────────────────────────────────

        private void Populate()
        {
            if (_slotViews == null) return;
            int total = _slotViews.Length;

            // 1) Fill the first row from spell slots (LMB / hotkeys).
            int rowSize = cols;
            int slotCount = _caster != null ? _caster.SlotCount : 0;
            for (int i = 0; i < rowSize && i < total; i++)
            {
                var def = (i < slotCount) ? _caster.GetSpellAtSlot(i) : null;
                AssignSlot(i, def, slotIndex: i);
            }

            // 2) Fill remaining cells from the spell book in insertion order,
            //    skipping spells that already appear in row 0.
            int cursor = rowSize;
            if (_caster != null && cursor < total)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < rowSize && i < slotCount; i++)
                {
                    var d = _caster.GetSpellAtSlot(i);
                    if (d != null && !string.IsNullOrEmpty(d.spellKey)) seen.Add(d.spellKey);
                }
                foreach (var kv in _caster.GetAllRegisteredSpells())
                {
                    if (cursor >= total) break;
                    if (kv.Value == null || string.IsNullOrEmpty(kv.Key)) continue;
                    if (seen.Contains(kv.Key)) continue;
                    AssignSlot(cursor, kv.Value, slotIndex: -1);
                    cursor++;
                }
            }

            // 3) Empty the rest.
            for (; cursor < total; cursor++) AssignSlot(cursor, null, -1);
        }

        private void AssignSlot(int index, SpellDefinition def, int slotIndex)
        {
            ref var v = ref _slotViews[index];
            v.SpellKey  = def != null ? def.spellKey : null;
            v.SlotIndex = slotIndex;

            // Prefer the dedicated HUD icon; fall back to the in-world sprite for legacy spells
            // that still pack the icon into SpellDefinition.sprite.
            Sprite icon = IceLanceArt.ResolveIcon(def);
            if (icon != null)
            {
                v.Icon.sprite  = icon;
                v.Icon.color   = Color.white;
                v.Icon.enabled = true;
            }
            else
            {
                v.Icon.enabled = false;
            }

            if (def != null && def.manaCost > 0)
                v.ManaText.text = Mathf.RoundToInt(def.manaCost).ToString();
            else
                v.ManaText.text = "";

            v.CdOverlay.fillAmount = 0f;
            v.CdText.text          = "";
        }

        private void RefreshDynamic()
        {
            if (_caster == null || _slotViews == null) return;

            for (int i = 0; i < _slotViews.Length; i++)
            {
                var v = _slotViews[i];
                if (string.IsNullOrEmpty(v.SpellKey)) continue;

                float remain;
                float duration;
                if (v.SlotIndex >= 0)
                {
                    remain   = _caster.GetCooldownRemaining(v.SlotIndex);
                    var def  = _caster.GetSpellAtSlot(v.SlotIndex);
                    duration = def != null ? def.cooldownDuration : 0f;
                }
                else
                {
                    remain   = _caster.GetBookCooldownRemaining(v.SpellKey);
                    var def  = _caster.GetSpellByKey(v.SpellKey);
                    duration = def != null ? def.cooldownDuration : 0f;
                }

                if (remain > 0.05f && duration > 0f)
                {
                    v.CdOverlay.fillAmount = Mathf.Clamp01(remain / duration);
                    v.CdText.text = remain >= 1f
                        ? Mathf.CeilToInt(remain).ToString()
                        : remain.ToString("F1");
                }
                else
                {
                    v.CdOverlay.fillAmount = 0f;
                    v.CdText.text          = "";
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

        private Vector2 ResolveCastDirection()
        {
            if (_playerGo == null) return Vector2.right;
            if (_mainCam == null) _mainCam = Camera.main;
            if (_mainCam == null) return Vector2.right;

            Vector3 mouseWorld = _mainCam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = (Vector2)(mouseWorld - _playerGo.transform.position);
            if (dir.sqrMagnitude < 0.0001f) return Vector2.right;
            return dir.normalized;
        }

        private void ResolvePlayer()
        {
            var p = EntityRegistry.Player;
            if (p == null) return;
            if (p == _playerGo && _caster != null) return;

            _playerGo = p;
            _caster   = p.GetComponent<SpellCaster>();
            _mainCam  = Camera.main;
        }

        private static string HotkeyForIndex(int index)
        {
            // Row 0: 1..9, 0, -, =     Row 1: prefix S- (shift)
            int row = index / 12;
            int col = index % 12;
            string key;
            switch (col)
            {
                case 9:  key = "0"; break;
                case 10: key = "-"; break;
                case 11: key = "="; break;
                default: key = (col + 1).ToString(); break;
            }
            return row == 0 ? key : $"S+{key}";
        }
    }

    /// <summary>Per-slot click forwarder for the spell bar.</summary>
    internal class SpellSlotButton : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private SpellBarHUD _owner;
        private int _index;

        public void Bind(SpellBarHUD owner, int index) { _owner = owner; _index = index; }

        public void OnPointerClick(PointerEventData ev)
        {
            if (_owner == null) return;
            if (ev.button != PointerEventData.InputButton.Left) return;
            _owner.OnSlotClicked(_index);
        }

        public void OnBeginDrag(PointerEventData ev)
        {
            if (_owner == null) return;
            _owner.BeginSlotDrag(_index, ev);
        }

        public void OnDrag(PointerEventData ev)
        {
            if (_owner == null) return;
            _owner.UpdateSlotDrag(ev);
        }

        public void OnEndDrag(PointerEventData ev)
        {
            if (_owner == null) return;
            _owner.EndSlotDrag(_index, ev);
        }
    }
}
