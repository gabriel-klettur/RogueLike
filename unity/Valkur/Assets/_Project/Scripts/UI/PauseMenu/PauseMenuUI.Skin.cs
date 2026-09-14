using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.PauseMenu
{
    /// <summary>
    /// The pause menu in the pre-game menus' language, so pausing mid-run opens the same object
    /// the main menu and the loading screen are: bevelled housing, the selected row FILLED like
    /// the bar, sliders that are the bar with a gem for a handle, arrows and buttons from the kit.
    ///
    /// <para><b>It reskins what the builders made, it does not rebuild them.</b> Every row keeps
    /// its pill, bar, label and hit target — the input code and the fixtures address those — and
    /// this file hides the flat pill and the accent bar and hangs a <see cref="FrontendFillGraphic"/>
    /// beside each pill. The one visual decision each screen still makes ("is row i selected") goes
    /// through <see cref="PaintRow"/>, so the four update methods share one look.</para>
    ///
    /// <para><b>Particles answer what the player DID</b>: the selection moving onto a row, and a
    /// slider moving under a drag or a key. Nothing emits while the menu sits open — the game is
    /// paused, and a menu that sparkles over a frozen world would be the only thing moving.</para>
    /// </summary>
    public partial class PauseMenuUI
    {
        private const int SkinMoteCapacity = 160;

        private readonly Dictionary<Image, FrontendFillGraphic> _rowFills = new Dictionary<Image, FrontendFillGraphic>();
        private readonly List<(Slider slider, FrontendFillGraphic fill, FrontendGemGraphic gem)> _sliderSkins =
            new List<(Slider, FrontendFillGraphic, FrontendGemGraphic)>();
        private readonly List<Image> _staleFills = new List<Image>();
        private MenuFxLayer _skinMotes;
        private float _skinClock;

        private static MenuStyle SkinStyle => MenuStyle.Active;
        private static Color SkinGold => SkinStyle != null ? SkinStyle.Gold : AccentGold;
        private static Color SkinInk => SkinStyle != null ? SkinStyle.textOnSelection : Color.black;
        private static bool SkinReduceMotion => GameSettings.Instance != null && GameSettings.Instance.reduceMotion;

        /// <summary>After <c>BuildCanvas</c>: every static panel, every pill, every slider and arrow.</summary>
        private void SkinCanvas()
        {
            foreach (var panel in new[] { _optionsPanel, _soundsPanel, _videoPanel, _inputsPanel, _loadGamePanel, _pausePanel })
                SkinPanel(panel);

            SkinPills(_optPills);
            SkinPills(_soundPills);
            SkinPills(_videoPills);
            SkinPills(_loadPills);
            HideImages(_optBars); HideImages(_soundBars); HideImages(_videoBars); HideImages(_loadBars);

            foreach (var row in _soundRows) SkinSlider(row.slider);
            SkinVideoArrows();

            if (_loadGamePanel != null)
                foreach (var btn in _loadGamePanel.GetComponentsInChildren<Button>(true))
                    if (btn.name.StartsWith("LoadBtn_"))
                    {
                        bool danger = btn.name.EndsWith("Delete");
                        EditorFrontendSkin.SkinButton(btn, danger ? Valkur.UIKit.UITheme.DANGER_IDLE : SkinGold,
                                                      danger ? Valkur.UIKit.UITheme.DANGER : Valkur.UIKit.UITheme.SUCCESS);
                    }

            if (_overlayRoot != null)
            {
                _skinMotes = MenuFxLayer.Create(_overlayRoot.transform, MenuArt.Get(SkinStyle), SkinMoteCapacity,
                                                FrontendKit.Get(SkinStyle).Additive);
                _skinMotes.name = "PauseMotes";
                _skinMotes.UseSoftMotes = true;
                _skinMotes.transform.SetAsLastSibling();
            }
        }

        /// <summary>After <c>RebuildPausePanelRows</c>: the pause list is rebuilt on every open.</summary>
        private void SkinPauseRows()
        {
            // The previous rows are Destroy()ed (deferred), so their keys are stale or about to be.
            _staleFills.Clear();
            foreach (var kv in _rowFills)
                if (kv.Key == null || kv.Value == null || kv.Key.name.StartsWith("Pill_") && kv.Key.transform.parent == _pausePanel?.transform)
                    _staleFills.Add(kv.Key);
            foreach (var key in _staleFills) _rowFills.Remove(key);
            SkinPills(_pausePills);
            HideImages(_pauseBars);
            _skinMotes?.transform.SetAsLastSibling();
        }

        private static void SkinPanel(GameObject panel)
        {
            if (panel == null) return;
            var flat = panel.GetComponent<Image>();
            // Alpha 0, not removed: it still catches clicks inside the panel.
            if (flat != null) flat.color = Color.clear;
            if (panel.transform.Find("PanelFrame") == null)
            {
                var frame = MenuUIKit.Panel("PanelFrame", panel.transform, MenuArt.Get(SkinStyle), SkinStyle);
                frame.HeaderHeight = 48f;
                frame.HeaderGemLit = 0.85f;
                frame.transform.SetAsFirstSibling();
            }
            var title = panel.transform.Find("PanelTitle")?.GetComponent<TextMeshProUGUI>();
            if (title != null) title.color = SkinGold;
        }

        private void SkinPills(Image[] pills)
        {
            if (pills == null) return;
            foreach (var pill in pills)
            {
                if (pill == null || _rowFills.ContainsKey(pill)) continue;
                // Named after the pill so RebuildPausePanelRows' "Pill_" sweep takes it with the row.
                var fill = FrontendFillGraphic.Create(pill.transform.parent, pill.name + "_Fill");
                var src = pill.rectTransform;
                var rt = fill.rectTransform;
                rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
                rt.anchoredPosition = src.anchoredPosition; rt.sizeDelta = src.sizeDelta;
                rt.SetSiblingIndex(pill.transform.GetSiblingIndex() + 1);
                fill.Profile = FrontendFillProfile.Row;
                fill.Border = true;
                fill.StartCore = true;
                fill.Tint = SkinGold;
                fill.enabled = false;
                _rowFills[pill] = fill;
                pill.color = Color.clear;
            }
        }

        private static void HideImages(Image[] images)
        {
            if (images == null) return;
            foreach (var img in images) if (img != null) img.enabled = false;
        }

        /// <summary>
        /// The one "is this row selected" painter every screen uses: the filled row with dark ink
        /// when chosen, nothing but the label when not. Arriving on a row is an event and throws a
        /// few sparks off its accent core.
        /// </summary>
        private void PaintRow(Image pill, TextMeshProUGUI label, bool selected, TextMeshProUGUI value = null)
        {
            // A gold value on the gold fill disappears; it takes the ink with its label.
            if (value != null) value.color = selected ? SkinInk : SkinGold;
            if (pill != null) pill.color = Color.clear;
            if (pill != null && _rowFills.TryGetValue(pill, out var fill) && fill != null)
            {
                bool arrived = selected && !fill.enabled;
                fill.enabled = selected;
                fill.Flow = selected && !SkinReduceMotion;
                if (arrived) EmitAt(fill.rectTransform, 8, FrontendMoteStyle.Sparks, SkinGold);
            }
            if (label != null) label.color = selected ? SkinInk : TextNormal;
        }

        /// <summary>A slim slider as the loading bar in miniature: bevelled track, molten fill, a gem handle.</summary>
        private void SkinSlider(Slider slider)
        {
            if (slider == null) return;
            var track = slider.transform.Find("Track");
            if (track != null)
            {
                var trackImg = track.GetComponent<Image>();
                if (trackImg != null) trackImg.color = Color.clear;
                var frame = BevelFrameGraphic.Create(track, "Frame");
                frame.Thickness = 3f;
                frame.ShadowScale = 0.35f;
                frame.Brackets = false;
                frame.Tint = SkinGold;
                frame.transform.SetAsFirstSibling();
                var fa = track.Find("FillArea") as RectTransform;
                if (fa != null) { fa.offsetMin = new Vector2(3f, 3f); fa.offsetMax = new Vector2(-3f, -3f); }
            }

            FrontendFillGraphic fill = null;
            if (slider.fillRect != null)
            {
                var fillImg = slider.fillRect.GetComponent<Image>();
                if (fillImg != null) fillImg.color = Color.clear;
                fill = FrontendFillGraphic.Create(slider.fillRect, "Molten");
                fill.Profile = FrontendFillProfile.Bar;
                fill.Tint = SkinGold;
                fill.LeadingCore = true;
            }

            FrontendGemGraphic gem = null;
            if (slider.handleRect != null)
            {
                var handleImg = slider.handleRect.GetComponent<Image>();
                if (handleImg != null) handleImg.color = Color.clear;
                gem = FrontendGemGraphic.Create(slider.handleRect, "Gem", SkinGold);
                var grt = gem.rectTransform;
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = new Vector2(-3f, -3f); grt.offsetMax = new Vector2(3f, 3f);
                gem.Lit = 0.7f;
            }
            slider.transition = Selectable.Transition.None;
            _sliderSkins.Add((slider, fill, gem));
        }

        /// <summary>A slider moved (drag or key): a few sparks off its handle and a blink of the gem.</summary>
        private void OnSkinSliderMoved(Slider slider, int count)
        {
            for (int i = 0; i < _sliderSkins.Count; i++)
            {
                if (_sliderSkins[i].slider != slider) continue;
                if (_sliderSkins[i].gem != null) _sliderSkins[i].gem.Lit = 1f;
                if (slider.handleRect != null) EmitAt(slider.handleRect, count, FrontendMoteStyle.Sparks, SkinGold);
                return;
            }
        }

        private void SkinVideoArrows()
        {
            if (_videoPanel == null) return;
            foreach (var btn in _videoPanel.GetComponentsInChildren<Button>(true))
            {
                bool left = btn.name.StartsWith("VLeft_");
                if (!left && !btn.name.StartsWith("VRight_")) continue;
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = Color.clear;
                foreach (var glyph in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) glyph.enabled = false;
                var arrow = FrontendArrowGraphic.Create(btn.transform, "Arrow", left);
                var rt = arrow.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(16f, 20f);
                arrow.raycastTarget = false;
                btn.transition = Selectable.Transition.None;
            }
        }

        private void EmitAt(RectTransform rt, int count, FrontendMoteStyle style, Color colour)
        {
            if (_skinMotes == null || rt == null || SkinReduceMotion) return;
            var r = rt.rect;
            FrontendIconMotes.Burst(_skinMotes, rt, new Vector2(r.xMin + Mathf.Min(r.width, 40f) * 0.5f, r.center.y),
                                    Mathf.Min(r.height, 20f), colour, style, count);
        }

        /// <summary>Unscaled: the pause menu freezes the game clock it sits over.</summary>
        private void TickSkin(float dt)
        {
            if (dt <= 0f || _screen == PauseScreen.None) return;
            _skinClock += dt;
            foreach (var kv in _rowFills)
                if (kv.Value != null && kv.Value.enabled) kv.Value.Clock = _skinClock;
            foreach (var s in _sliderSkins)
                if (s.gem != null && s.gem.Lit > 0.7f) s.gem.Lit = Mathf.MoveTowards(s.gem.Lit, 0.7f, dt / 0.35f);
            _skinMotes?.Tick(Mathf.Min(dt, 0.1f));
        }
    }
}
