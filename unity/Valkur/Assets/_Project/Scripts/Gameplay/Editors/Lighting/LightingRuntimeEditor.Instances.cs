using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Lighting Editor — instances list (right-bottom panel). Lists every live
    /// point light spawned by <see cref="WorldLightLoader"/>; each row focuses
    /// the camera on its target on click and exposes a Delete button.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        private void MaybeRefreshInstances()
        {
            if (Time.unscaledTime < _instancesRefreshNext) return;
            _instancesRefreshNext = Time.unscaledTime + INSTANCES_REFRESH_INTERVAL;
            RebuildInstancesList();
        }

        private void RebuildInstancesList()
        {
            if (_ui.InstancesListContent == null) return;

            for (int i = _ui.InstancesListContent.childCount - 1; i >= 0; i--)
                Destroy(_ui.InstancesListContent.GetChild(i).gameObject);

            var loader = WorldLightLoader.Instance;

            // Authored lights only. The list used to show ActiveLightObjects, which folds in the
            // lights derived from lamp-post buildings: those are not in the light file, a save
            // never writes them, and deleting a row for one does nothing the next load undoes.
            // Counting them made the header disagree with the save by however many lamps the
            // world happened to hold.
            int authored  = loader != null ? loader.PersistentLightCount : 0;
            int derived   = loader != null ? loader.DerivedLightCount    : 0;
            int unspawned = loader != null ? loader.UnspawnedRecordCount : 0;
            int listed    = authored - unspawned;

            if (_ui.InstancesCountTmp != null)
            {
                string text = listed == 1 ? "1 authored light" : $"{listed} authored lights";
                if (unspawned > 0) text += $"  (+{unspawned} unspawnable, kept)";
                if (derived   > 0) text += $"  -  {derived} from buildings";
                _ui.InstancesCountTmp.text = text;
            }

            if (listed == 0)
            {
                string empty = "(no lights placed yet)\n\nUse Spawn mode to drop a light.\nClick a row to focus the camera.";
                if (unspawned > 0)
                    empty = $"(none of the {unspawned} record(s) in the light file\ncould be spawned - unknown preset, or the\nzone is not loaded)\n\nThey are preserved on save. See the console.";
                else if (derived > 0)
                    empty += $"\n\n({derived} light(s) come from buildings and are\nnot listed here - they are not in the light file.)";
                AddInstancePlaceholder(empty);
                return;
            }

            foreach (var go in loader.PersistentLightObjects)
            {
                if (go == null) continue;
                AddInstanceRow(go);
            }
        }

        private void AddInstancePlaceholder(string text)
        {
            var go = EditorUIHelpers.CreateUI("InstancesPlaceholder", _ui.InstancesListContent);
            go.AddComponent<LayoutElement>().preferredHeight = 80f;
            var tmp                    = go.AddComponent<TextMeshProUGUI>();
            tmp.text                   = text;
            tmp.fontSize               = 10f;
            tmp.fontStyle              = FontStyles.Italic;
            tmp.alignment              = TextAlignmentOptions.Center;
            tmp.color                  = EditorUIHelpers.TEXT_MUTED;
            tmp.enableWordWrapping     = true;
        }

        private void AddInstanceRow(GameObject lightGo)
        {
            var row = EditorUIHelpers.CreateUI($"Row_{lightGo.name}", _ui.InstancesListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing                = 4f;
            hl.padding                = new RectOffset(4, 4, 0, 0);
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;
            hl.childAlignment         = TextAnchor.MiddleLeft;

            // Focus button (entire row width minus the delete button)
            var focusGo               = EditorUIHelpers.CreateUI("Focus", row.transform);
            var focusLE               = focusGo.AddComponent<LayoutElement>();
            focusLE.flexibleWidth     = 1f;
            var focusImg              = focusGo.AddComponent<Image>();
            focusImg.color            = lightGo == _selectedLight
                ? EditorUIHelpers.SLOT_SELECTED
                : EditorUIHelpers.BTN_NORMAL;
            var focusBtn              = focusGo.AddComponent<Button>();
            var fc                    = focusBtn.colors;
            fc.normalColor            = focusImg.color;
            fc.highlightedColor       = EditorUIHelpers.BTN_HOVER;
            fc.pressedColor           = EditorUIHelpers.BTN_ACTIVE;
            focusBtn.colors           = fc;
            focusBtn.targetGraphic    = focusImg;
            focusBtn.onClick.AddListener(() => OnFocusInstance(lightGo));

            var lblGo                       = EditorUIHelpers.CreateUI("Lbl", focusGo.transform);
            EditorUIHelpers.StretchFill(lblGo);
            var lblTmp                      = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text                     = $"  {lightGo.name}  ({lightGo.transform.position.x:F0}, {lightGo.transform.position.y:F0})";
            lblTmp.fontSize                 = 10f;
            lblTmp.alignment                = TextAlignmentOptions.MidlineLeft;
            lblTmp.color                    = EditorUIHelpers.TEXT_PRIMARY;
            lblTmp.enableWordWrapping       = false;
            lblTmp.overflowMode             = TextOverflowModes.Ellipsis;

            // Delete button
            var delGo                       = EditorUIHelpers.CreateUI("Del", row.transform);
            delGo.AddComponent<LayoutElement>().preferredWidth = 26f;
            var delImg                      = delGo.AddComponent<Image>();
            delImg.color                    = LightingEditorUIBuilder.DANGER_NORMAL;
            var delBtn                      = delGo.AddComponent<Button>();
            var dc                          = delBtn.colors;
            dc.normalColor                  = delImg.color;
            dc.highlightedColor             = LightingEditorUIBuilder.DANGER_HOVER;
            dc.pressedColor                 = LightingEditorUIBuilder.DANGER_PRESSED;
            delBtn.colors                   = dc;
            delBtn.targetGraphic            = delImg;
            delBtn.onClick.AddListener(() => DeleteLight(lightGo));
            EditorUIHelpers.AddCenteredText(delGo.transform, "X", 11f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY)
                .alignment = TextAlignmentOptions.Center;
        }

        private void OnFocusInstance(GameObject lightGo)
        {
            if (lightGo == null) return;
            _selectedLight = lightGo;
            // Move the editor-detached camera to the light, so the user can
            // immediately see it. Mirrors ItemsRuntimeEditor.FocusCameraOn:
            // we move the *vcam transform exposed by CameraSetup* — writing
            // to Camera.main.transform directly is overwritten by Cinemachine
            // every LateUpdate, so the camera would visibly snap back to the
            // player. The detached vcam transform persists between frames.
            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            if (camSetup != null)
            {
                var vcamT = camSetup.GetDetachedTransform();
                if (vcamT != null)
                {
                    Vector3 p = lightGo.transform.position;
                    p.z = vcamT.position.z;
                    vcamT.position = p;
                }
            }
            RefreshPresetProperties();
            RebuildInstancesList();
            SetStatus($"Focused on '{lightGo.name}'.");
        }
    }
}
