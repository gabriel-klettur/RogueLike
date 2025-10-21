import os
import json
import pygame

from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.ecs.components.item_models import ItemStack

import logging
logger = logging.getLogger(__name__)

class InventoryEditorSystem:
    """
    ECS system that manages the inventory editor UI overlay (toggle with F6).
    Supports editing player and NPC inventories by drag & drop.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.active = False
        self.selected_eid = None
        self.entities = []
        cwd = os.getcwd()
        # Unificar rutas: usar data/inventory/defaults en vez de data/defaults
        self.default_monster_path = os.path.join(cwd, 'data', 'inventory', 'defaults', 'inventory_monsters.json')
        self.default_player_path = os.path.join(cwd, 'data', 'inventory', 'defaults', 'inventory_player.json')
        self.active_monster_path = os.path.join(cwd, 'data', 'inventory', 'active', 'inventory_monsters.json')
        self.active_player_path = os.path.join(cwd, 'data', 'inventory', 'active', 'inventory_player.json')
        # Asegurar que los archivos de defaults existan para evitar fallos en arranque sin defaults
        try:
            os.makedirs(os.path.dirname(self.default_monster_path), exist_ok=True)
            if not os.path.exists(self.default_monster_path):
                with open(self.default_monster_path, 'w', encoding='utf-8') as f:
                    json.dump({}, f, ensure_ascii=False, indent=2)
                logger.info(f"[InventoryEditor] Creado defaults de monstruos en: {self.default_monster_path}")
            os.makedirs(os.path.dirname(self.default_player_path), exist_ok=True)
            if not os.path.exists(self.default_player_path):
                with open(self.default_player_path, 'w', encoding='utf-8') as f:
                    json.dump({}, f, ensure_ascii=False, indent=2)
                logger.info(f"[InventoryEditor] Creado defaults de player en: {self.default_player_path}")
        except Exception:
            # No bloquear el editor por permisos/rutas; se manejará al usar los archivos
            pass
        # Load item models from SQLite
        self.items, _assets = ItemsLoader().load()
        # Drag state
        self.drag_item = None  # (item_id, quantity)
        self.drag_slot = None
        # Selection keys state
        self.prev_left = False
        self.prev_right = False
        # UI layout
        self.slot_size = 50
        self.margin = 5
        self.grid_origin = (50, 50)
        # Cached images
        self.images = {}
        # Buttons
        self.button_size = (120, 30)
        self.save_button_rect = None
        self.apply_button_rect = None
        # Font
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 24)

    def update(self, world, *args):
        # Toggle editor mode
        for eid, inp in world.components.get('InputComponent', {}).items():
            if getattr(inp, 'toggle_editor', False):
                self.active = not self.active
                if self.active:
                    logger.debug("[InventoryEditorOpened]")
                    # Initialize entity list and selection
                    players = list(world.components.get('PlayerTagComponent', {}).keys())
                    npcs = list(world.components.get('NPCTagComponent', {}).keys())
                    self.entities = players + npcs
                    self.selected_eid = self.entities[0] if self.entities else None
                else:
                    logger.debug("[InventoryEditorClosed]")
                inp.toggle_editor = False
        if not self.active or self.selected_eid is None:
            return
        # Cycle entity selection with left/right arrows
        keys = pygame.key.get_pressed()
        curr_left = keys[pygame.K_LEFT]
        curr_right = keys[pygame.K_RIGHT]
        if curr_left and not self.prev_left:
            idx = self.entities.index(self.selected_eid)
            self.selected_eid = self.entities[(idx - 1) % len(self.entities)]
        if curr_right and not self.prev_right:
            idx = self.entities.index(self.selected_eid)
            self.selected_eid = self.entities[(idx + 1) % len(self.entities)]
        self.prev_left = curr_left
        self.prev_right = curr_right
        # Mouse handling for drag & drop
        mouse_pos = pygame.mouse.get_pos()
        mouse_pressed = pygame.mouse.get_pressed()[0]
        inv = world.components.get('InventoryComponent', {}).get(self.selected_eid)
        if inv is None:
            return
        if mouse_pressed:
            if self.drag_item is None:
                slot_idx = self._get_slot_at_pos(mouse_pos, len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx]:
                    stack = inv.slots[slot_idx]
                    self.drag_item = (stack.item_id, stack.quantity)
                    self.drag_slot = slot_idx
                    inv.slots[slot_idx] = None
                    logger.debug("[InventoryChanged]")
        else:
            if self.drag_item is not None:
                slot_idx = self._get_slot_at_pos(mouse_pos, len(inv.slots))
                if slot_idx is not None and inv.slots[slot_idx] is None:
                    inv.slots[slot_idx] = ItemStack(self.drag_item[0], self.drag_item[1])
                else:
                    inv.slots[self.drag_slot] = ItemStack(self.drag_item[0], self.drag_item[1])
                self.drag_item = None
                self.drag_slot = None
                logger.debug("[InventoryChanged]")
        # Button clicks
        if not mouse_pressed:
            mx, my = mouse_pos
            if self.save_button_rect and self.save_button_rect.collidepoint(mx, my):
                self._save_template(inv)
                logger.debug("[InventoryEditorSaved]")
            if self.apply_button_rect and self.apply_button_rect.collidepoint(mx, my):
                self._apply_changes(inv)
                logger.debug("[InventoryEditorApplied]")

    def render(self, world, surface, camera=None):
        if not self.active:
            return
        ow, oh = surface.get_size()
        # Overlay
        overlay = pygame.Surface((ow, oh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Early overlay draw when no world is provided
        if world is None:
            surface.blit(overlay, (0, 0))
            return
        # Title
        title = f"Inventory Editor - Entity {self.selected_eid}"
        text = self.font.render(title, True, (255, 255, 255))
        overlay.blit(text, (10, 10))

        # Draw grid
        inv = world.components.get('InventoryComponent', {}).get(self.selected_eid)
        slots = inv.slots if inv else []
        origin_x, origin_y = self.grid_origin
        cols = min(len(slots), 10)
        for idx, stack in enumerate(slots):
            col = idx % cols
            row = idx // cols
            x = origin_x + col * (self.slot_size + self.margin)
            y = origin_y + row * (self.slot_size + self.margin) + 30
            rect = pygame.Rect(x, y, self.slot_size, self.slot_size)
            pygame.draw.rect(overlay, (100,100,100), rect)
            pygame.draw.rect(overlay, (255,255,255), rect, 2)
            if stack:
                item_id, qty = stack.item_id, stack.quantity
                img = self._get_item_image(item_id)
                if img:
                    iw, ih = img.get_size()
                    overlay.blit(img, (x + (self.slot_size - iw)//2, y + (self.slot_size - ih)//2))
                qty_text = self.font.render(str(qty), True, (255,255,0))
                overlay.blit(qty_text, (x + self.slot_size - qty_text.get_width() - 2, y + self.slot_size - qty_text.get_height() - 2))
        # Buttons
        bx = ow - self.button_size[0] - 20
        by = origin_y + 30
        self.save_button_rect = pygame.Rect(bx, by, *self.button_size)
        self.apply_button_rect = pygame.Rect(bx, by + self.button_size[1] + 10, *self.button_size)
        pygame.draw.rect(overlay, (50,150,50), self.save_button_rect)
        pygame.draw.rect(overlay, (50,150,50), self.apply_button_rect)
        save_text = self.font.render("Guardar plantilla", True, (255,255,255))
        apply_text = self.font.render("Aplicar cambios", True, (255,255,255))
        overlay.blit(save_text, (bx + (self.button_size[0] - save_text.get_width())//2, by + 5))
        overlay.blit(apply_text, (bx + (self.button_size[0] - apply_text.get_width())//2, by + self.button_size[1] + 15))
        # Dragging
        if self.drag_item:
            mx, my = pygame.mouse.get_pos()
            item_id, qty = self.drag_item
            img = self._get_item_image(item_id)
            if img:
                iw, ih = img.get_size()
                overlay.blit(img, (mx - iw//2, my - ih//2))
        surface.blit(overlay, (0,0))

    def _get_item_image(self, item_id):
        if item_id in self.images:
            return self.images[item_id]
        model = self.items.get(item_id)
        if not model:
            return None
        icon = getattr(model, 'icon_small', None) or (model.icon[0] if isinstance(model.icon, list) else model.icon)
        if not icon:
            return None
        try:
            raw = pygame.image.load(os.path.join(os.getcwd(), icon)).convert_alpha()
            img = pygame.transform.scale(raw, (self.slot_size-10, self.slot_size-10))
            self.images[item_id] = img
            return img
        except Exception:
            return None

    def _get_slot_at_pos(self, pos, count):
        x, y = pos
        origin_x, origin_y = self.grid_origin
        y0 = origin_y + 30
        cols = min(count, 10)
        for idx in range(count):
            col = idx % cols
            row = idx // cols
            rx = origin_x + col * (self.slot_size + self.margin)
            ry = y0 + row * (self.slot_size + self.margin)
            rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            if rect.collidepoint(x, y):
                return idx
        return None

    def _save_template(self, inv):
        data = {}
        try:
            with open(self.default_player_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except:
            pass
        out = {
            'player_id': inv.player_id,
            'capacity': inv.capacity,
            'slots': inv.serialize().get('slots'),
            'schema_version': data.get('schema_version', '1.0.0')
        }
        with open(self.default_player_path, 'w', encoding='utf-8') as f:
            json.dump(out, f, indent=2)
        try:
            total_slots = len(out.get('slots') or [])
            non_empty = sum(1 for s in (out.get('slots') or []) if s)
            logger.info(f"[InventoryEditor] Guardada plantilla por defecto: path={self.default_player_path}, slots={total_slots}, ocupados={non_empty}")
        except Exception:
            pass

    def _apply_changes(self, inv):
        try:
            with open(self.active_player_path, 'r', encoding='utf-8') as f:
                d = json.load(f)
        except:
            d = {}
        key = None
        for eid_str, entry in d.items():
            if entry.get('player_id') == inv.player_id:
                key = eid_str
                entry = entry
                break
        if key is None:
            key = str(self.selected_eid)
        d[key] = {
            'player_id': inv.player_id,
            'slots': inv.serialize().get('slots'),
            'schema_version': entry.get('schema_version', '1.0.0')
        }
        with open(self.active_player_path, 'w', encoding='utf-8') as f:
            json.dump(d, f, indent=2)
        try:
            slots = d[key].get('slots') or []
            non_empty = sum(1 for s in slots if s)
            logger.info(f"[InventoryEditor] Aplicados cambios a inventario activo: path={self.active_player_path}, entity_id={key}, stacks={non_empty}")
        except Exception:
            pass

