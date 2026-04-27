using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Drag-from-picker spawn for the Entities Editor (F5).
    /// Mirrors <c>BuildingsRuntimeEditor</c>'s behaviour: LMB-pressing a picker
    /// slot starts a drag once the cursor moves past <see cref="PICKER_DRAG_THRESHOLD"/>
    /// pixels; releasing over the map spawns the entity / player at the world
    /// position under the cursor. A floating UI ghost (sprite + pulsating outline)
    /// sits on the editor canvas overlay so it stays above every other panel.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Drag-from-picker state ────────────────────────────────────────────
        private bool    _pickerDragging;
        private string  _pickerDragKey;
        private bool    _pickerDragIsPlayer;
        private Sprite  _pickerDragSprite;
        private Color   _pickerDragTint = Color.white;
        private Vector2 _pickerDragStartScreen;

        private const float PICKER_DRAG_THRESHOLD = 8f; // px before click→drag

        // Drag preview (Canvas Overlay → renders above world AND every panel)
        private GameObject    _dragGhostGo;
        private RectTransform _dragGhostRt;
        private Image         _dragGhostImg;
        private Image         _dragGhostOutline;

        private const float ENTITY_PPU         = 16f; // Valkur PPU for entities
        private const float DRAG_GHOST_BORDER  = 10f; // px outline thickness
        private const float DRAG_GHOST_DEFAULT = 64f; // px fallback size

        private static readonly Color DRAG_GHOST_TINT    = new Color(0.55f, 1f, 1f, 0.70f);
        private static readonly Color DRAG_GHOST_OUTLINE = new Color(1f, 0.85f, 0.10f, 0.95f);

        private Camera _cachedMainCamera;
        private Camera CachedMainCamera => _cachedMainCamera != null
            ? _cachedMainCamera
            : (_cachedMainCamera = Camera.main);

        // ── Slot pointer-down handler (registered in AddPickerSlot) ───────────
        private void OnPickerSlotPointerDown(string key, bool isPlayer, Sprite sprite, Color tint)
        {
            _pickerDragKey         = key;
            _pickerDragIsPlayer    = isPlayer;
            _pickerDragSprite      = sprite;
            _pickerDragTint        = tint;
            _pickerDragStartScreen = Mouse.current?.position.ReadValue() ?? Vector2.zero;
        }

        // ── Ghost construction ────────────────────────────────────────────────
        private void BuildDragGhost()
        {
            if (_dragGhostGo != null) return;

            _dragGhostGo = EditorUIHelpers.CreateUI("PickerDragGhost", _canvas.transform);
            _dragGhostRt = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(DRAG_GHOST_DEFAULT, DRAG_GHOST_DEFAULT);
            _dragGhostRt.anchorMin = _dragGhostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _dragGhostRt.pivot     = new Vector2(0.5f, 0.5f);

            // Outline (renders behind sprite)
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-DRAG_GHOST_BORDER, -DRAG_GHOST_BORDER);
            outlineRt.offsetMax = new Vector2( DRAG_GHOST_BORDER,  DRAG_GHOST_BORDER);
            _dragGhostOutline               = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            // Sprite (renders on top of outline)
            var spriteGo = EditorUIHelpers.CreateUI("Sprite", _dragGhostGo.transform);
            var spriteRt = spriteGo.GetComponent<RectTransform>();
            spriteRt.anchorMin = Vector2.zero;
            spriteRt.anchorMax = Vector2.one;
            spriteRt.offsetMin = spriteRt.offsetMax = Vector2.zero;
            _dragGhostImg                = spriteGo.AddComponent<Image>();
            _dragGhostImg.raycastTarget  = false;
            _dragGhostImg.preserveAspect = true;

            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        /// <summary>Sizes the ghost so its on-screen pixel size matches a 1×1 world tile at current zoom.</summary>
        private void SizeDragGhostFromSprite(Sprite sprite)
        {
            if (_dragGhostRt == null) return;
            float worldUnits = 1f;
            if (sprite != null)
            {
                float ppu = sprite.pixelsPerUnit > 0 ? sprite.pixelsPerUnit : ENTITY_PPU;
                worldUnits = Mathf.Max(sprite.rect.width, sprite.rect.height) / ppu;
            }

            float pxPerWorldUnit = 32f;
            var cam = CachedMainCamera;
            if (cam != null && cam.orthographic && cam.orthographicSize > 0.001f)
                pxPerWorldUnit = Screen.height / (2f * cam.orthographicSize);

            float scale = (_canvas != null && _canvas.scaleFactor > 0.001f) ? _canvas.scaleFactor : 1f;
            float sizePx = worldUnits * pxPerWorldUnit / scale;
            sizePx = Mathf.Clamp(sizePx, 32f, 256f);
            _dragGhostRt.sizeDelta = new Vector2(sizePx, sizePx);
        }

        // ── Per-frame drag update (called from Update while editor is active) ─
        private void UpdatePickerDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos = mouse.position.ReadValue();

            // Phase 1: waiting for drag threshold
            if (!_pickerDragging && !string.IsNullOrEmpty(_pickerDragKey))
            {
                if (mouse.leftButton.isPressed)
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        _pickerDragging = true;
                        BuildDragGhost();
                        _dragGhostImg.sprite  = _pickerDragSprite;
                        _dragGhostImg.enabled = _pickerDragSprite != null;
                        // Apply the picker tint to the sprite, but keep a soft alpha so the ghost reads as a preview.
                        var c = _pickerDragTint; c.a = 0.85f;
                        _dragGhostImg.color = c;
                        SizeDragGhostFromSprite(_pickerDragSprite);
                        _dragGhostGo.transform.SetAsLastSibling();
                        _dragGhostGo.SetActive(true);

                        SetStatus($"Dragging '{_pickerDragKey}' — release over the map to spawn.");
                    }
                }
                else
                {
                    // Released before threshold → normal click handled by Button.onClick.
                    _pickerDragKey = null;
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2: ghost follows the cursor
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : CachedMainCamera,
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Pulsating outline
            if (_dragGhostOutline != null)
            {
                float t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 5f) + 1f) * 0.5f;
                var c = DRAG_GHOST_OUTLINE;
                c.a = Mathf.Lerp(0.35f, 1.0f, t);
                _dragGhostOutline.color = c;
            }

            // Drop
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                var cam = CachedMainCamera;
                if (!overUi && cam != null)
                {
                    Vector3 worldPos = cam.ScreenToWorldPoint(screenPos);
                    worldPos.z = 0f;
                    PlaceEntityFromDrag(_pickerDragKey, _pickerDragIsPlayer, worldPos);
                }
                else
                {
                    SetStatus("Drag cancelled (released over UI). Drop on the map to spawn.");
                }
                CancelPickerDrag();
            }
        }

        private void CancelPickerDrag()
        {
            _pickerDragging      = false;
            _pickerDragKey       = null;
            _pickerDragSprite    = null;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }

        // ── Spawn dispatch ────────────────────────────────────────────────────
        private void PlaceEntityFromDrag(string key, bool isPlayer, Vector3 worldPos)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (isPlayer) SpawnPlayerAt(key, worldPos);
            else          SpawnMonsterAt(key, worldPos);
        }

        private void SpawnPlayerAt(string playerKey, Vector3 worldPos)
        {
            var setup = FindObjectOfType<GameplaySceneSetup>();
            var prefab = setup != null ? setup.PlayerPrefab : null;
            if (prefab == null)
            {
                SetStatus("Spawn failed: no playerPrefab on GameplaySceneSetup.");
                Debug.LogWarning("[EntitiesEditor] Cannot spawn player — playerPrefab missing.");
                return;
            }

            var def = FindPlayerDefinition(playerKey);
            if (def == null)
            {
                SetStatus($"Spawn failed: player '{playerKey}' not found.");
                return;
            }

            var go = Instantiate(prefab, worldPos, Quaternion.identity);
            // Use a unique name so multiple players can coexist and be inspected.
            go.name = $"Player_{playerKey}_{System.DateTime.Now:HHmmss}";
            var entitiesContainer = GameObject.Find("[Entities]")?.transform;
            if (entitiesContainer != null) go.transform.SetParent(entitiesContainer, true);
            EntitySetup.ConfigurePlayer(go, def);

            SetStatus($"Spawned player '{playerKey}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
            Debug.Log($"[EntitiesEditor] Spawned player {playerKey} at {worldPos}");
        }

        private void SpawnMonsterAt(string monsterKey, Vector3 worldPos)
        {
            if (_monsterCatalog == null)
            {
                SetStatus("Spawn failed: monster catalog not assigned.");
                return;
            }
            var def = _monsterCatalog.GetByKey(monsterKey);
            if (def == null)
            {
                SetStatus($"Spawn failed: monster '{monsterKey}' not in catalog.");
                return;
            }

            // Prefer MonsterSpawner so the entity is tracked alongside other spawned monsters.
            var spawner = FindObjectOfType<Valkur.Gameplay.MonsterSpawner>();
            GameObject go;
            if (spawner != null)
            {
                go = spawner.SpawnEntity(def, worldPos);
            }
            else
            {
                var setup  = FindObjectOfType<GameplaySceneSetup>();
                var prefab = setup != null ? setup.MonsterPrefab : null;
                if (prefab == null)
                {
                    SetStatus("Spawn failed: no monsterPrefab on GameplaySceneSetup.");
                    Debug.LogWarning("[EntitiesEditor] Cannot spawn monster — monsterPrefab missing.");
                    return;
                }
                go = Instantiate(prefab, worldPos, Quaternion.identity);
                var entitiesContainer = GameObject.Find("[Entities]")?.transform;
                if (entitiesContainer != null) go.transform.SetParent(entitiesContainer, true);
                EntitySetup.ConfigureMonster(go, def);
            }

            if (go != null)
            {
                SetStatus($"Spawned '{monsterKey}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
                Debug.Log($"[EntitiesEditor] Spawned monster {monsterKey} at {worldPos}");
            }
        }
    }
}
