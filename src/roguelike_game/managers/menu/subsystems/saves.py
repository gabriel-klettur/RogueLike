from __future__ import annotations

import json
import logging
import time
import uuid
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import List, Optional, Tuple

import pygame

from roguelike_engine.world.models import WorldSnapshot
from roguelike_game.ecs.systems.spawner.spawner_placement_system import (
    SpawnerPlacementSystem,
)
from roguelike_game.ecs.systems.core.npc_restore_system import NpcRestoreSystem
from roguelike_game.ecs.systems.core.npc_respawn_system import NpcRespawnSystem
from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.utils.inventory_sync import write_active_for_player

logger = logging.getLogger(__name__)


@dataclass
class SaveEntry:
    path: str
    label: str
    meta: dict


class SaveListManager:
    """Manages the Save/Load list view, inline rename, and delete confirm modal."""

    def __init__(self, game, renderer, screen: pygame.Surface) -> None:
        self.game = game
        self.renderer = renderer
        self.screen = screen
        # Data
        self.save_entries: List[dict] = []
        self.load_selected: int = 0
        # Layout caches
        self._saves_fixed_panel_size: Optional[Tuple[int, int]] = None
        self._saves_fixed_list_w: Optional[int] = None
        self._saves_fixed_details_w: Optional[int] = None
        self._saves_fixed_screen_size: Optional[Tuple[int, int]] = None
        # Scroll/hover/edit state
        self._saves_row_scroll_offset: int = 0
        self._saves_hovered_idx: Optional[int] = None
        self._saves_hover_details_name: bool = False
        self._saves_editing_name: bool = False
        self._saves_edit_name_text: str = ""
        self._saves_edit_caret: int = 0
        self._saves_select_all_edit: bool = False
        self._prev_key_repeat: Optional[Tuple[int, int]] = None
        self._last_click_time: float = 0.0
        self._last_click_pos: Optional[Tuple[int, int]] = None
        self._saves_hover_load_button: bool = False
        self._saves_hover_delete_button: bool = False
        self._saves_show_confirm_delete: bool = False
        self._saves_hover_confirm_yes: bool = False
        self._saves_hover_confirm_cancel: bool = False

    # ---------------- Public API ----------------
    def enter(self) -> None:
        self.refresh_list()
        self.load_selected = 0
        # Reset state
        self._saves_row_scroll_offset = 0
        self._saves_hovered_idx = None
        self._saves_hover_details_name = False
        self._saves_editing_name = False
        # Prepare fixed layout for current screen size
        self.compute_fixed_layout(self.screen)

    def handle_input(self, event) -> None:
        if event.type == pygame.KEYDOWN:
            if self._saves_show_confirm_delete:
                if event.key == pygame.K_ESCAPE:
                    self._saves_show_confirm_delete = False
                    self._saves_hover_confirm_yes = False
                    self._saves_hover_confirm_cancel = False
                    return None
                if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    self._confirm_delete_selected_save()
                    return None
                return None
            if self._saves_editing_name:
                if event.key == pygame.K_ESCAPE:
                    self._end_edit_save_name(cancel=True)
                    return None
                if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    self._commit_save_rename()
                    return None
                if event.key == pygame.K_BACKSPACE:
                    if self._saves_select_all_edit:
                        self._saves_edit_name_text = ""
                        self._saves_edit_caret = 0
                        self._saves_select_all_edit = False
                        return None
                    mods = pygame.key.get_mods()
                    if mods & pygame.KMOD_CTRL:
                        i = self._saves_edit_caret
                        text = self._saves_edit_name_text
                        if i > 0 and text:
                            j = i
                            while j > 0 and text[j - 1].isspace():
                                j -= 1
                            while j > 0 and not text[j - 1].isspace():
                                j -= 1
                            self._saves_edit_name_text = text[:j] + text[i:]
                            self._saves_edit_caret = j
                    else:
                        if self._saves_edit_caret > 0 and len(self._saves_edit_name_text) > 0:
                            i = self._saves_edit_caret
                            self._saves_edit_name_text = (
                                self._saves_edit_name_text[: i - 1] + self._saves_edit_name_text[i:]
                            )
                            self._saves_edit_caret -= 1
                    return None
                if event.key == pygame.K_DELETE:
                    if self._saves_select_all_edit:
                        self._saves_edit_name_text = ""
                        self._saves_edit_caret = 0
                        self._saves_select_all_edit = False
                        return None
                    mods = pygame.key.get_mods()
                    if mods & pygame.KMOD_CTRL:
                        i = self._saves_edit_caret
                        text = self._saves_edit_name_text
                        if i < len(text):
                            j = i
                            while j < len(text) and text[j].isspace():
                                j += 1
                            while j < len(text) and not text[j].isspace():
                                j += 1
                            self._saves_edit_name_text = text[:i] + text[j:]
                    else:
                        i = self._saves_edit_caret
                        if i < len(self._saves_edit_name_text):
                            self._saves_edit_name_text = (
                                self._saves_edit_name_text[:i] + self._saves_edit_name_text[i + 1 :]
                            )
                    return None
                if event.key in (pygame.K_LEFT, pygame.K_KP_4):
                    if self._saves_select_all_edit:
                        self._saves_edit_caret = 0
                        self._saves_select_all_edit = False
                        return None
                    mods = pygame.key.get_mods()
                    if mods & pygame.KMOD_CTRL:
                        i = self._saves_edit_caret
                        text = self._saves_edit_name_text
                        j = i
                        while j > 0 and text[j - 1].isspace():
                            j -= 1
                        while j > 0 and not text[j - 1].isspace():
                            j -= 1
                        self._saves_edit_caret = j
                    else:
                        self._saves_edit_caret = max(0, self._saves_edit_caret - 1)
                    return None
                if event.key in (pygame.K_RIGHT, pygame.K_KP_6):
                    if self._saves_select_all_edit:
                        self._saves_edit_caret = len(self._saves_edit_name_text)
                        self._saves_select_all_edit = False
                        return None
                    mods = pygame.key.get_mods()
                    if mods & pygame.KMOD_CTRL:
                        i = self._saves_edit_caret
                        text = self._saves_edit_name_text
                        j = i
                        while j < len(text) and text[j].isspace():
                            j += 1
                        while j < len(text) and not text[j].isspace():
                            j += 1
                        self._saves_edit_caret = j
                    else:
                        self._saves_edit_caret = min(len(self._saves_edit_name_text), self._saves_edit_caret + 1)
                    return None
                if event.key == pygame.K_HOME:
                    self._saves_edit_caret = 0
                    self._saves_select_all_edit = False
                    return None
                if event.key == pygame.K_END:
                    self._saves_edit_caret = len(self._saves_edit_name_text)
                    self._saves_select_all_edit = False
                    return None
                ch = getattr(event, "unicode", "") or ""
                if ch and ord(ch) >= 32:
                    if self._saves_select_all_edit:
                        self._saves_edit_name_text = ch
                        self._saves_edit_caret = len(ch)
                        self._saves_select_all_edit = False
                    else:
                        i = self._saves_edit_caret
                        self._saves_edit_name_text = (
                            self._saves_edit_name_text[:i] + ch + self._saves_edit_name_text[i:]
                        )
                        self._saves_edit_caret += len(ch)
                return None
            if event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
                if self.save_entries:
                    self.load_selected = (self.load_selected - 1) % len(self.save_entries)
                    self._end_edit_save_name(cancel=True)
                    layout = getattr(self.renderer, "last_saves_layout", None)
                    if layout:
                        start = layout.get("start", 0)
                        if self.load_selected < start:
                            self._saves_row_scroll_offset = self.load_selected
            elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
                if self.save_entries:
                    self.load_selected = (self.load_selected + 1) % len(self.save_entries)
                    self._end_edit_save_name(cancel=True)
                    layout = getattr(self.renderer, "last_saves_layout", None)
                    if layout:
                        start = layout.get("start", 0)
                        end = layout.get("end", 0)
                        visible = max(1, end - start)
                        if self.load_selected >= end:
                            self._saves_row_scroll_offset = max(0, self.load_selected - (visible - 1))
            elif event.key in (pygame.K_PAGEUP,):
                layout = getattr(self.renderer, "last_saves_layout", {})
                start = layout.get("start", 0)
                max_jump = max(1, (layout.get("end", 0) - start))
                self._saves_row_scroll_offset = max(0, self._saves_row_scroll_offset - max_jump)
            elif event.key in (pygame.K_PAGEDOWN,):
                layout = getattr(self.renderer, "last_saves_layout", {})
                start = layout.get("start", 0)
                max_jump = max(1, (layout.get("end", 0) - start))
                max_off = max(0, len(self.save_entries) - max_jump)
                self._saves_row_scroll_offset = min(max_off, self._saves_row_scroll_offset + max_jump)
            elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                return None
            return None
        if event.type == pygame.MOUSEMOTION:
            if self._saves_show_confirm_delete:
                self._saves_hover_confirm_yes = False
                self._saves_hover_confirm_cancel = False
                layout_c = getattr(self.renderer, "last_confirm_layout", None)
                if layout_c:
                    yes_rect = layout_c.get("yes_rect")
                    cancel_rect = layout_c.get("cancel_rect")
                    if yes_rect and yes_rect.collidepoint(event.pos):
                        self._saves_hover_confirm_yes = True
                    if cancel_rect and cancel_rect.collidepoint(event.pos):
                        self._saves_hover_confirm_cancel = True
                return None
            layout = getattr(self.renderer, "last_saves_layout", None)
            self._saves_hovered_idx = None
            self._saves_hover_details_name = False
            self._saves_hover_load_button = False
            self._saves_hover_delete_button = False
            if layout:
                for idx, rect in layout.get("row_rects", {}).items():
                    if rect.collidepoint(event.pos):
                        self._saves_hovered_idx = idx
                        break
                name_rect = layout.get("details_name_rect")
                if name_rect and name_rect.collidepoint(event.pos):
                    self._saves_hover_details_name = True
                btn_rect = layout.get("load_button_rect")
                if btn_rect and btn_rect.collidepoint(event.pos):
                    self._saves_hover_load_button = True
                del_rect = layout.get("delete_button_rect")
                if del_rect and del_rect.collidepoint(event.pos):
                    self._saves_hover_delete_button = True
        elif event.type == pygame.MOUSEWHEEL:
            self._saves_row_scroll_offset = max(0, self._saves_row_scroll_offset - event.y)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self._saves_show_confirm_delete:
                layout_c = getattr(self.renderer, "last_confirm_layout", None)
                if layout_c:
                    panel_rect = layout_c.get("panel_rect")
                    yes_rect = layout_c.get("yes_rect")
                    cancel_rect = layout_c.get("cancel_rect")
                    if yes_rect and yes_rect.collidepoint(event.pos):
                        self._confirm_delete_selected_save()
                        return None
                    if cancel_rect and cancel_rect.collidepoint(event.pos):
                        self._saves_show_confirm_delete = False
                        return None
                    if panel_rect and not panel_rect.collidepoint(event.pos):
                        self._saves_show_confirm_delete = False
                        return None
                return None
            layout = getattr(self.renderer, "last_saves_layout", None)
            if layout:
                btn_rect = layout.get("load_button_rect")
                if btn_rect and btn_rect.collidepoint(event.pos):
                    self._load_selected_save()
                    return None
                del_rect = layout.get("delete_button_rect")
                if del_rect and del_rect.collidepoint(event.pos):
                    self._saves_show_confirm_delete = True
                    self._end_edit_save_name(cancel=False)
                    return None
                name_rect = layout.get("details_name_rect")
                if name_rect and name_rect.collidepoint(event.pos):
                    now = time.time()
                    dbl = False
                    if self._last_click_time and self._last_click_pos:
                        dt = now - self._last_click_time
                        dx = abs(event.pos[0] - self._last_click_pos[0])
                        dy = abs(event.pos[1] - self._last_click_pos[1])
                        if dt <= 0.35 and dx <= 6 and dy <= 6:
                            dbl = True
                    self._last_click_time = now
                    self._last_click_pos = event.pos
                    if dbl:
                        self._begin_edit_save_name()
                        self._saves_select_all_edit = True
                    else:
                        if self._saves_editing_name:
                            self._set_caret_from_click(event.pos)
                            self._saves_select_all_edit = False
                    return None
                for idx, rect in layout.get("row_rects", {}).items():
                    if rect.collidepoint(event.pos):
                        self.load_selected = idx
                        self._end_edit_save_name(cancel=True)
                        break
            return None
        return None

    def draw(self, screen: pygame.Surface, *, panel_top_min: Optional[int], logo_layout) -> pygame.Rect:
        if self._saves_fixed_screen_size != screen.get_size():
            self.compute_fixed_layout(screen)
        items = [e["label"] for e in self.save_entries]
        meta = self.save_entries[self.load_selected]["meta"] if self.save_entries else {}
        detail_lines = self._format_save_details(meta)
        overlay_rect = self.renderer.draw_saves_panel(
            screen,
            selected=self.load_selected,
            items=items,
            detail_lines=detail_lines,
            row_scroll_offset=self._saves_row_scroll_offset,
            hovered_index=self._saves_hovered_idx,
            fixed_panel_size=self._saves_fixed_panel_size,
            fixed_list_width=self._saves_fixed_list_w,
            fixed_details_width=self._saves_fixed_details_w,
            hover_details_name=self._saves_hover_details_name,
            editing_name=self._saves_editing_name,
            edit_name_text=self._saves_edit_name_text,
            caret_pos=self._saves_edit_caret,
            hover_load_button=self._saves_hover_load_button,
            hover_delete_button=self._saves_hover_delete_button,
            select_all_edit=self._saves_select_all_edit,
            panel_top_min=panel_top_min if panel_top_min is not None else None,
        )
        if logo_layout is not None:
            surf, pos, _ = logo_layout
            screen.blit(surf, pos)
        if self._saves_show_confirm_delete:
            name = "-"
            if self.save_entries:
                entry = self.save_entries[self.load_selected]
                name = (entry.get("meta") or {}).get("name") or entry.get("label") or "-"
            lines = ["¿Borrar esta partida?", f"{name}", "Esta acción no se puede deshacer."]
            overlay_rect = self.renderer.draw_confirm_dialog(
                screen,
                lines,
                hover_yes=self._saves_hover_confirm_yes,
                hover_cancel=self._saves_hover_confirm_cancel,
            )
        return overlay_rect

    # ---------------- Internal helpers ----------------
    def refresh_list(self) -> None:
        g = self.game
        save_dir: Path = g.world.config.save_dir
        save_dir.mkdir(parents=True, exist_ok=True)
        entries: List[dict] = []
        for path in sorted(save_dir.glob("partida_*.json"), reverse=True):
            try:
                data = self.game.world.repository.load_from_path(str(path))
            except Exception:
                data = {}
            meta = data.get("meta") or {}
            label = meta.get("name") or path.stem
            entries.append({"path": str(path), "label": label, "meta": meta})
        self.save_entries = entries

    def compute_fixed_layout(self, screen: pygame.Surface) -> None:
        font = self.renderer.font
        list_max_w = 0
        for e in self.save_entries:
            tw, _ = font.size(e.get("label", ""))
            list_max_w = max(list_max_w, tw)
        details_max_w = 0
        for e in self.save_entries:
            lines = self._format_save_details(e.get("meta") or {})
            for line in lines:
                tw, _ = font.size(line)
                details_max_w = max(details_max_w, tw)
        if not self.save_entries:
            details_max_w = max(details_max_w, font.size("Sin metadatos")[0])
            list_max_w = max(list_max_w, font.size("-")[0])
        col_gap = 32
        w = self.renderer.padding_x * 2 + list_max_w + col_gap + details_max_w + 12
        min_rows = 8
        inner_rows_h = min_rows * self.renderer.line_height + max(0, (min_rows - 1)) * self.renderer.item_gap
        h = self.renderer.padding_y * 2 + inner_rows_h
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
        self._saves_fixed_panel_size = (w, h)
        self._saves_fixed_list_w = list_max_w
        self._saves_fixed_details_w = details_max_w
        self._saves_fixed_screen_size = (sw, sh)

    def _format_save_details(self, meta: dict) -> List[str]:
        if not meta:
            return ["Sin metadatos", "Pulsa Enter para cargar"]
        lines = []
        lines.append(f"Nombre: {meta.get('name', '-')}")
        lines.append(f"Creada: {meta.get('created_at', '-')}")
        lines.append(f"Última vez: {meta.get('last_played', '-')}")
        p = meta.get("player", {}) or {}
        lines.append(f"Nivel: {p.get('level', '-')}")
        lines.append(f"XP: {p.get('xp', '-')}")
        it = meta.get("items_summary", {}) or {}
        lines.append(f"Pilas: {it.get('stacks', 0)}")
        top = it.get("top_items") or []
        if top:
            lines.append("Items: " + ", ".join([str(x) for x in top]))
        return lines

    def _load_selected_save(self) -> None:
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        path = entry["path"]
        g = self.game
        try:
            g.world.load_world(path)
            level = getattr(g.world, "current_level", None) or g.map.name
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            try:
                ecs = g.ecs.ecs_world
                comps = ecs.components
                for eid in list(comps.get("NPCTagComponent", {}).keys()):
                    ecs.remove_entity(eid)
                for eid in list(comps.get("SpawnerConfig", {}).keys()):
                    ecs.remove_entity(eid)
                for eid in list(comps.get("SpawnRequest", {}).keys()):
                    ecs.remove_entity(eid)
                try:
                    for sys in getattr(ecs, "update_systems", []) or []:
                        if isinstance(sys, SpawnerPlacementSystem):
                            try:
                                sys._loaded = False
                            except Exception:
                                pass
                        elif isinstance(sys, NpcRestoreSystem):
                            try:
                                sys._applied.clear()
                            except Exception:
                                sys._applied = set()
                        elif isinstance(sys, NpcRespawnSystem):
                            try:
                                sys._requested.clear()
                            except Exception:
                                sys._requested = set()
                except Exception:
                    pass
                try:
                    ecs.components["NPCInventorySnapshot"] = dict(getattr(g.world, "npc_inventories", {}) or {})
                except Exception:
                    pass
                try:
                    ecs.invalidate_spatial_index()
                except Exception:
                    pass
            except Exception:
                pass
            tile = g.map._local_state.get("player_pos")
            if tile is None:
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings

                tile = (
                    off_x + global_map_settings.zone_width // 2,
                    off_y + global_map_settings.zone_height // 2,
                )
                g.map.spawn_player(tile)
            px, py = g.map.get_spawn_pixel(tuple(tile))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            try:
                pdata = getattr(g.world, "player_inventory", None)
                if pdata:
                    def _valid_uuid(x):
                        try:
                            uuid.UUID(str(x))
                            return True
                        except Exception:
                            return False

                    pid = pdata.get("player_id")
                    if not _valid_uuid(pid):
                        try:
                            eid = g.ecs.ecs_world.player_entity
                            active_path = Path("data/inventory/active/inventory_player.json")
                            active = json.loads(active_path.read_text(encoding="utf-8")) if active_path.exists() else {}
                            apid = (active.get(str(eid)) or {}).get("player_id")
                            if not _valid_uuid(apid):
                                apid = active.get("player_id")
                            pid = apid if _valid_uuid(apid) else str(uuid.uuid4())
                        except Exception:
                            pid = str(uuid.uuid4())
                        try:
                            pdata["player_id"] = pid
                            repo = g.world.repository
                            data = repo.load_from_path(str(path))
                            data.setdefault("player_inventory", {})
                            data["player_inventory"]["player_id"] = pid
                            snapshot = WorldSnapshot(
                                version=data.get("version", 1),
                                player=data.get("player"),
                                npcs=data.get("npcs", {}),
                                levels=data.get("levels", {}),
                                player_inventory=data.get("player_inventory"),
                                npc_inventories=data.get("npc_inventories"),
                                meta=data.get("meta"),
                            )
                            repo.save_to_path(str(path), snapshot)
                            g.world.player_inventory = data.get("player_inventory", pdata)
                        except Exception:
                            pass
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent

                    inv = InventoryComponent(capacity=pdata.get("capacity", 20), player_id=pdata.get("player_id"))
                    for slot in pdata.get("slots", []):
                        if slot:
                            inv.add(slot["item"], slot.get("quantity", 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                    try:
                        snap = inv.serialize() if hasattr(inv, "serialize") else {}
                        if "player_id" not in snap:
                            snap["player_id"] = pdata.get("player_id")
                        write_active_for_player(eid, snap)
                    except Exception:
                        pass
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)
            try:
                meta = getattr(g.world, "save_metadata", {}) or {}
                p = meta.get("player", {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get("xp") is not None:
                    xp_comp.xp = int(p["xp"])
                if p.get("level") is not None:
                    xp_comp.level = int(p["level"])
                meta.setdefault("player", {})
                meta["player"]["xp"] = int(xp_comp.xp)
                meta["player"]["level"] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
            # Close menu and signal enter game
            from roguelike_game.managers.menu.subsystems.music import MusicManager

            try:
                music = getattr(self.game, "_menu_music_mgr", None)
                if music and isinstance(music, MusicManager):
                    music.stop_music(fade_ms=None)
            except Exception:
                pass
            try:
                aq = g.ecs.ecs_world.components.setdefault("AudioEventQueue", [])
                aq.append({"type": "enter_game_default", "duration_ms": 600})
            except Exception:
                pass
            logger.info("Partida cargada desde %s", path)
        except Exception as e:
            logger.error("Error al cargar partida desde lista: %s", e)

    # ---------------- Inline rename ----------------
    def _begin_edit_save_name(self) -> None:
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        current = (entry.get("meta") or {}).get("name") or entry.get("label") or ""
        self._saves_editing_name = True
        self._saves_edit_name_text = str(current)
        self._saves_edit_caret = len(self._saves_edit_name_text)
        self._saves_select_all_edit = False
        try:
            self._prev_key_repeat = pygame.key.get_repeat() if hasattr(pygame.key, "get_repeat") else None
            pygame.key.set_repeat(350, 40)
        except Exception:
            pass

    def _set_caret_from_click(self, pos: Tuple[int, int]) -> None:
        try:
            layout = getattr(self.renderer, "last_saves_layout", None)
            if not layout:
                return
            name_rect = layout.get("details_name_rect")
            if not name_rect or not name_rect.collidepoint(pos):
                return
            rel_x = pos[0] - name_rect.left - 4
            text = self._saves_edit_name_text
            best_i = 0
            for i in range(1, len(text) + 1):
                w, _ = self.renderer.font.size(text[:i])
                if w <= rel_x:
                    best_i = i
                else:
                    break
            self._saves_edit_caret = best_i
        except Exception:
            pass

    def _commit_save_rename(self) -> None:
        if not self.save_entries:
            self._end_edit_save_name(cancel=True)
            return
        new_name = (self._saves_edit_name_text or "").strip()
        if not new_name:
            self._end_edit_save_name(cancel=True)
            return
        entry = self.save_entries[self.load_selected]
        path = entry.get("path")
        try:
            data = self.game.world.repository.load_from_path(str(path))
        except Exception:
            data = {}
        meta = data.get("meta") or {}
        meta["name"] = new_name
        data["meta"] = meta
        try:
            repo = self.game.world.repository
            snapshot = WorldSnapshot(
                version=data.get("version", 1),
                player=data.get("player"),
                npcs=data.get("npcs", {}),
                levels=data.get("levels", {}),
                player_inventory=data.get("player_inventory"),
                npc_inventories=data.get("npc_inventories"),
                meta=data.get("meta"),
            )
            repo.save_to_path(str(path), snapshot)
            entry["label"] = new_name
            entry["meta"] = meta
            self._end_edit_save_name()
            self.compute_fixed_layout(self.screen)
        except Exception as e:
            logger.warning("No se pudo guardar el nuevo nombre del guardado: %s", e)
            self._end_edit_save_name(cancel=True)

    def _end_edit_save_name(self, cancel: bool = False) -> None:
        self._saves_editing_name = False
        self._saves_select_all_edit = False
        try:
            if self._prev_key_repeat and all(isinstance(x, int) for x in self._prev_key_repeat):
                delay, interval = self._prev_key_repeat
                pygame.key.set_repeat(delay, interval)
            else:
                pygame.key.set_repeat(0)
        except Exception:
            pass

    # ---------------- Delete ----------------
    def _confirm_delete_selected_save(self) -> None:
        if not self.save_entries:
            self._saves_show_confirm_delete = False
            return
        idx = self.load_selected
        path = self.save_entries[idx].get("path")
        try:
            if path:
                p = Path(path)
                if p.exists():
                    p.unlink()
        except Exception as e:
            logger.warning("No se pudo borrar el guardado %s: %s", path, e)
        self.refresh_list()
        if not self.save_entries:
            self.load_selected = 0
            self._saves_show_confirm_delete = False
            self._saves_hover_confirm_yes = False
            self._saves_hover_confirm_cancel = False
            self._saves_hover_delete_button = False
            self._saves_row_scroll_offset = 0
            self._saves_hovered_idx = None
            self._saves_hover_details_name = False
            self._saves_editing_name = False
            return
        else:
            self.load_selected = min(self.load_selected, len(self.save_entries) - 1)
        self._saves_show_confirm_delete = False
        self._saves_hover_confirm_yes = False
        self._saves_hover_confirm_cancel = False
        self._saves_hover_delete_button = False
        try:
            self.compute_fixed_layout(self.screen)
        except Exception:
            pass
