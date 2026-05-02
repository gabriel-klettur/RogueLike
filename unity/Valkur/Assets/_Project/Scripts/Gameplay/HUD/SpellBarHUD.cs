using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.Spells.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.UI
{
    /// <summary>
    /// World-of-Warcraft style action bar HUD.
    /// Displays a fixed grid (rows × cols) of spell slots populated from the
    /// player's <see cref="SpellCaster"/> (slots first, then spell-book entries).
    /// Each slot shows: icon, radial cooldown overlay, hotkey label, mana cost.
    /// Click a slot to cast the spell in the direction of the mouse.
    /// </summary>
    public class SpellBarHUD : SingletonMonoBehaviour<SpellBarHUD>
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

        // ─────────────────────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("SpellBarCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            int total = rows * cols;
            float gridW = cols * slotSize + (cols - 1) * slotGap;
            float gridH = rows * slotSize + (rows - 1) * slotGap;
            float panelW = gridW + 16f + 24f; // padding + arrow column
            float panelH = gridH + 12f;

            // Root panel
            var rootGo = new GameObject("SpellBar", typeof(RectTransform));
            rootGo.transform.SetParent(_canvas.transform, false);
            _root = rootGo.GetComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0f);
            _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot     = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, bottomPad);
            _root.sizeDelta = new Vector2(panelW, panelH);

            var bg = rootGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.55f);
            bg.raycastTarget = true; // needed so empty space catches the drag

            var ol = rootGo.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);

            _rootCg = rootGo.AddComponent<CanvasGroup>();

            // Window-style drag: clicking-and-dragging any empty space on the
            // panel BG moves the bar. Slot buttons consume their own clicks but
            // do not implement IDrag*, so drag events bubble up to this root.
            var dragger = rootGo.AddComponent<WindowDragHandler>();
            dragger.Target = _root;

            // Grid (right side, leaving 24 px column on the left for arrows)
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(_root, false);
            var grt = (RectTransform)gridGo.transform;
            grt.anchorMin = new Vector2(0f, 0.5f);
            grt.anchorMax = new Vector2(0f, 0.5f);
            grt.pivot     = new Vector2(0f, 0.5f);
            grt.anchoredPosition = new Vector2(28f, 0f);
            grt.sizeDelta = new Vector2(gridW, gridH);

            _slotViews = new SlotView[total];
            for (int i = 0; i < total; i++)
            {
                int r = i / cols;
                int c = i % cols;
                float sx = c * (slotSize + slotGap);
                float sy = -r * (slotSize + slotGap); // row 0 at top
                _slotViews[i] = BuildSlot(grt, i, sx, sy);
            }

            BuildPagerArrows();
            BuildMinimizeButton();
        }

        private void BuildMinimizeButton()
        {
            var go = new GameObject("MinimizeBtn", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-2f, -2f);
            rt.sizeDelta = new Vector2(18f, 18f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            c.highlightedColor = new Color(0.32f, 0.32f, 0.40f, 1f);
            c.pressedColor     = new Color(0.10f, 0.10f, 0.12f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(MinimizeToTray);

            var txtGo = new GameObject("Glyph", typeof(RectTransform));
            txtGo.transform.SetParent(rt, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text       = "_";
            tmp.fontSize   = 14f;
            tmp.fontStyle  = FontStyles.Bold;
            tmp.alignment  = TextAlignmentOptions.Center;
            tmp.color      = ACCENT;
            tmp.raycastTarget = false;
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

        private SlotView BuildSlot(RectTransform parent, int index, float x, float y)
        {
            var go = new GameObject($"Slot_{index}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f, 0.95f);

            var slotOl = go.AddComponent<Outline>();
            slotOl.effectColor    = new Color(0.25f, 0.25f, 0.30f, 1f);
            slotOl.effectDistance = new Vector2(1f, 1f);

            var dropZone = go.AddComponent<DropZoneSpellSlot>();
            dropZone.Bind(this, index);

            // Icon
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(rt, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(2f, 2f);
            irt.offsetMax = new Vector2(-2f, -2f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            icon.enabled        = false;

            // Cooldown radial overlay (dark wedge that empties as cooldown elapses)
            var cdGo = new GameObject("CdOverlay", typeof(RectTransform));
            cdGo.transform.SetParent(rt, false);
            var cdRt = (RectTransform)cdGo.transform;
            cdRt.anchorMin = Vector2.zero;
            cdRt.anchorMax = Vector2.one;
            cdRt.offsetMin = new Vector2(2f, 2f);
            cdRt.offsetMax = new Vector2(-2f, -2f);
            var cdImg = cdGo.AddComponent<Image>();
            cdImg.color           = new Color(0f, 0f, 0f, 0.65f);
            cdImg.raycastTarget   = false;
            cdImg.type            = Image.Type.Filled;
            cdImg.fillMethod      = Image.FillMethod.Radial360;
            cdImg.fillOrigin      = (int)Image.Origin360.Top;
            cdImg.fillClockwise   = false;
            cdImg.fillAmount      = 0f;
            cdImg.sprite          = MakeWhitePixel();

            // Cooldown numeric label
            var cdTxtGo = new GameObject("CdText", typeof(RectTransform));
            cdTxtGo.transform.SetParent(rt, false);
            var ctRt = (RectTransform)cdTxtGo.transform;
            ctRt.anchorMin = Vector2.zero;
            ctRt.anchorMax = Vector2.one;
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;
            var cdTxt = cdTxtGo.AddComponent<TextMeshProUGUI>();
            cdTxt.alignment = TextAlignmentOptions.Center;
            cdTxt.fontSize  = 14f;
            cdTxt.fontStyle = FontStyles.Bold;
            cdTxt.color     = ACCENT;
            cdTxt.raycastTarget = false;
            cdTxt.text = "";

            // Hotkey label (top-left)
            var hkGo = new GameObject("Hotkey", typeof(RectTransform));
            hkGo.transform.SetParent(rt, false);
            var hkRt = (RectTransform)hkGo.transform;
            hkRt.anchorMin = new Vector2(0f, 1f);
            hkRt.anchorMax = new Vector2(0f, 1f);
            hkRt.pivot     = new Vector2(0f, 1f);
            hkRt.anchoredPosition = new Vector2(2f, -1f);
            hkRt.sizeDelta = new Vector2(24f, 12f);
            var hk = hkGo.AddComponent<TextMeshProUGUI>();
            hk.text       = HotkeyForIndex(index);
            hk.fontSize   = 9f;
            hk.fontStyle  = FontStyles.Bold;
            hk.alignment  = TextAlignmentOptions.TopLeft;
            hk.color      = new Color(1f, 1f, 1f, 0.85f);
            hk.outlineWidth = 0.2f;
            hk.outlineColor = Color.black;
            hk.raycastTarget = false;

            // Mana cost label (bottom-right)
            var mcGo = new GameObject("Mana", typeof(RectTransform));
            mcGo.transform.SetParent(rt, false);
            var mcRt = (RectTransform)mcGo.transform;
            mcRt.anchorMin = new Vector2(1f, 0f);
            mcRt.anchorMax = new Vector2(1f, 0f);
            mcRt.pivot     = new Vector2(1f, 0f);
            mcRt.anchoredPosition = new Vector2(-2f, 1f);
            mcRt.sizeDelta = new Vector2(28f, 12f);
            var mc = mcGo.AddComponent<TextMeshProUGUI>();
            mc.text       = "";
            mc.fontSize   = 9f;
            mc.fontStyle  = FontStyles.Bold;
            mc.alignment  = TextAlignmentOptions.BottomRight;
            mc.color      = new Color(0.55f, 0.80f, 1f, 0.95f);
            mc.outlineWidth = 0.2f;
            mc.outlineColor = Color.black;
            mc.raycastTarget = false;

            // Click handler
            var click = go.AddComponent<SpellSlotButton>();
            click.Bind(this, index);

            return new SlotView
            {
                Root = go, Bg = bg, Icon = icon,
                CdOverlay = cdImg, CdText = cdTxt,
                HotkeyText = hk, ManaText = mc,
                SpellKey = null, SlotIndex = -1
            };
        }

        private void BuildPagerArrows()
        {
            // Up / Down arrows column (purely cosmetic for now — placeholder for paging).
            var colGo = new GameObject("Arrows", typeof(RectTransform));
            colGo.transform.SetParent(_root, false);
            var crt = (RectTransform)colGo.transform;
            crt.anchorMin = new Vector2(0f, 0.5f);
            crt.anchorMax = new Vector2(0f, 0.5f);
            crt.pivot     = new Vector2(0f, 0.5f);
            crt.anchoredPosition = new Vector2(4f, 0f);
            crt.sizeDelta = new Vector2(20f, slotSize * 2 + slotGap);

            BuildArrow(crt, true,  new Vector2(0f, 1f),  new Vector2(0f, -1f));
            BuildArrow(crt, false, new Vector2(0f, 0f),  new Vector2(0f, 1f));
        }

        private void BuildArrow(RectTransform parent, bool up, Vector2 anchor, Vector2 pivot)
        {
            var go = new GameObject(up ? "Up" : "Down", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = pivot;
            rt.sizeDelta = new Vector2(18f, 18f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text       = up ? "▲" : "▼";
            tmp.fontSize   = 14f;
            tmp.alignment  = TextAlignmentOptions.Center;
            tmp.color      = ACCENT;
            tmp.raycastTarget = false;
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

            if (def != null && def.sprite != null)
            {
                v.Icon.sprite  = def.sprite;
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
        //  Click → cast
        // ─────────────────────────────────────────────────────────────────────

        public void OnSlotClicked(int index)
        {
            if (_caster == null) return;
            if (index < 0 || index >= _slotViews.Length) return;
            var v = _slotViews[index];
            if (string.IsNullOrEmpty(v.SpellKey)) return;

            Vector2 dir = ResolveCastDirection();
            if (v.SlotIndex >= 0) _caster.TryCast(v.SlotIndex, dir);
            else                  _caster.TryCastByKey(v.SpellKey, dir);
        }

        public bool CanAcceptSpellDrop(int slotIndex)
        {
            ResolvePlayer();
            return _caster != null && slotIndex >= 0 && slotIndex < _caster.SlotCount;
        }

        public bool TryAssignSpellToSlot(int slotIndex, SpellDefinition spell)
        {
            ResolvePlayer();
            if (_caster == null || spell == null) return false;
            if (slotIndex < 0 || slotIndex >= _caster.SlotCount) return false;

            _caster.SetSpell(slotIndex, spell);
            Populate();
            RefreshDynamic();
            return true;
        }

        public bool TryMoveAssignedSpell(int fromSlotIndex, int toSlotIndex)
        {
            ResolvePlayer();
            if (_caster == null) return false;
            if (fromSlotIndex == toSlotIndex) return false;
            if (fromSlotIndex < 0 || fromSlotIndex >= _caster.SlotCount) return false;
            if (toSlotIndex < 0 || toSlotIndex >= _caster.SlotCount) return false;

            var fromSpell = _caster.GetSpellAtSlot(fromSlotIndex);
            if (fromSpell == null) return false;

            var toSpell = _caster.GetSpellAtSlot(toSlotIndex);
            _caster.SetSpell(toSlotIndex, fromSpell);
            _caster.SetSpell(fromSlotIndex, toSpell);
            Populate();
            RefreshDynamic();
            return true;
        }

        public void BeginSlotDrag(int index, PointerEventData ev)
        {
            ResolvePlayer();
            if (_caster == null || ev.button != PointerEventData.InputButton.Left) return;
            if (index < 0 || index >= _slotViews.Length) return;

            var view = _slotViews[index];
            if (view.SlotIndex < 0) return;

            var spell = _caster.GetSpellAtSlot(view.SlotIndex);
            if (spell == null) return;

            var dragger = view.Root != null ? view.Root.GetComponent<CanvasGroup>() : null;
            if (dragger == null && view.Root != null)
                dragger = view.Root.AddComponent<CanvasGroup>();

            if (dragger != null)
            {
                dragger.alpha = 0.55f;
                dragger.blocksRaycasts = false;
            }

            SpellDragContext.Begin(spell, view.Icon != null ? view.Icon.sprite : spell.sprite, SpellDragOrigin.HudSlot, view.SlotIndex, _canvas, ev.position);
        }

        public void UpdateSlotDrag(PointerEventData ev)
        {
            if (!SpellDragContext.IsDragging)
                return;

            SpellDragContext.UpdatePosition(ev.position, _canvas);
        }

        public void EndSlotDrag(int index, PointerEventData ev)
        {
            if (index >= 0 && index < _slotViews.Length)
            {
                var view = _slotViews[index];
                if (view.Root != null)
                {
                    var cg = view.Root.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        cg.alpha = 1f;
                        cg.blocksRaycasts = true;
                    }
                }
            }

            SpellDragContext.End();
        }

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

        // ─────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────

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

        private static Sprite _whitePixel;
        private static Sprite MakeWhitePixel()
        {
            if (_whitePixel != null) return _whitePixel;
            var tex = new Texture2D(2, 2);
            var pixels = new Color[4];
            for (int i = 0; i < 4; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            _whitePixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            return _whitePixel;
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
