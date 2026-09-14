using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// The launcher in the pre-game menus' language: the loading bar's bevelled housing around
    /// the panel, tabs that FILL like the bar when chosen, a talent-style icon socket per entry,
    /// and one particle layer over all of it. The pieces are <see cref="EditorFrontendSkin"/>'s,
    /// shared with every editor that opts in.
    ///
    /// <para><b>Only this editor's panel.</b> The panel is still a <see cref="DraggablePanel"/>
    /// from <c>MakeDropPanel</c> — dragged, remembered by the workspace, closed by its own button
    /// — and this file only reskins what that helper built, so no editor-wide helper changed
    /// shape.</para>
    /// </summary>
    public partial class GeneralEditorManager
    {
        private const int MOTE_CAPACITY = 180;
        private const float MOTE_PAD = 50f;
        internal const float TAB_FONT_MAX = EditorFrontendSkin.TabFontMax;
        internal const float TAB_FONT_MIN = EditorFrontendSkin.TabFontMin;

        private MenuFxLayer _motes;
        private readonly System.Collections.Generic.Dictionary<GeneralEditorSection, (FrontendFillGraphic fill, FrontendHoverGraphic groove)> _tabSkins =
            new System.Collections.Generic.Dictionary<GeneralEditorSection, (FrontendFillGraphic, FrontendHoverGraphic)>(3);

        /// <summary>The particle layer every tile and tab emits into. For the tests.</summary>
        internal MenuFxLayer Motes => _motes;

        private void SkinPanel(GameObject panelRoot)
        {
            if (panelRoot == null) return;
            EditorFrontendSkin.SkinDropPanel(panelRoot, EditorFrontendSkin.Gold);
            _motes = EditorFrontendSkin.CreateMotes(panelRoot.transform, "LauncherMotes", MOTE_CAPACITY, MOTE_PAD);
        }

        private (Image hit, Button btn, TextMeshProUGUI tmp) BuildTab(Transform row, GeneralEditorSection section)
        {
            var captured = section;
            var btn = EditorFrontendSkin.BuildTab(row, $"Tab_{section}", TabLabel(section), () => OnTabClicked(captured),
                                                  out var fill, out var groove, out var tmp);
            _tabSkins[section] = (fill, groove);
            return (btn.GetComponent<Image>(), btn, tmp);
        }

        private void PaintTabs(GeneralEditorSection active)
        {
            foreach (var kv in _tabs)
            {
                _tabSkins.TryGetValue(kv.Key, out var skin);
                EditorFrontendSkin.PaintTab(skin.fill, skin.groove, kv.Value.tmp, kv.Key == active);
            }
        }

        /// <summary>A tab the author chose: switch, and let the fill answer with a few sparks.</summary>
        private void OnTabClicked(GeneralEditorSection section)
        {
            bool changed = section != _activeTab;
            SelectTab(section);
            if (!changed || _motes == null || !_tabSkins.TryGetValue(section, out var skin) || skin.fill == null) return;
            var rt = skin.fill.rectTransform;
            FrontendIconMotes.Burst(_motes, rt, rt.rect.center, rt.rect.height * 0.5f,
                                    EditorFrontendSkin.Gold, FrontendMoteStyle.Sparks, 10);
        }

        /// <summary>The header's close control as a small bevelled danger button.</summary>
        private static void SkinCloseButton(GameObject btnGo, Button btn)
        {
            var face = EditorFrontendSkin.SkinButton(btn, UITheme.DANGER_IDLE, UITheme.DANGER, 2f);
            if (face != null) face.ShadowScale = 0f;
        }

        /// <summary>Advances every tile's glow and the motes. Unscaled: the launcher freezes the game.</summary>
        private void TickSkin(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < _entryButtons.Count; i++)
            {
                var tile = _entryButtons[i].tile;
                if (tile != null && _entryButtons[i].entry != null && _entryButtons[i].entry.Section == _activeTab)
                    tile.Tick(dt);
            }
            _motes?.Tick(Mathf.Min(dt, 0.1f));
        }
    }
}
