import logging
import time
from datetime import datetime
from pathlib import Path
import pygame
import json
import uuid
from roguelike_game.utils.inventory_sync import write_active_for_player
from roguelike_game.managers.map import MapManager
from roguelike_game.ecs.systems.spawner.spawner_placement_system import SpawnerPlacementSystem
from roguelike_game.ecs.systems.core.npc_restore_system import NpcRestoreSystem
from roguelike_game.ecs.systems.core.npc_respawn_system import NpcRespawnSystem

from .menu_handler import MenuHandler
from roguelike_ui.widgets.menu_renderer import MenuRenderer
from roguelike_ui.widgets.menu_configurator import MenuConfigurator
from roguelike_engine.world.models import WorldSnapshot
from roguelike_game.ecs.components.experience_component import ExperienceComponent

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class MenuManager:
    """
    Orquesta la lógica, entrada y renderizado del menú.
    """
    def __init__(self, game, state, screen, input_config, font_size=36, background_path: str | None = None):
        # Referencias básicas
        self.game = game
        self.state = state
        self.screen = screen
        self.input_config = input_config

        # Componentes del menú
        self.renderer = MenuRenderer(font_size)
        self.configurator = MenuConfigurator(input_config, screen, self.renderer.font)
        self.handler = MenuHandler(state, input_config, self.configurator)

        # Flag para mostrar/ocultar menú y modo (start|pause|load_list)
        self.show_menu = False
        self.mode = "pause"
        self.prev_mode = "start"

        # Estado de lista de partidas
        self.save_entries: list[dict] = []  # cada entrada: {"path": str, "label": str, "meta": dict}
        self.load_selected = 0
        # Layout fijo para lista de partidas
        self._saves_fixed_panel_size: tuple[int, int] | None = None
        self._saves_fixed_list_w: int | None = None
        self._saves_fixed_details_w: int | None = None
        self._saves_fixed_screen_size: tuple[int, int] | None = None
        self._saves_row_scroll_offset: int = 0
        self._saves_hovered_idx: int | None = None
        self._saves_hover_details_name: bool = False
        self._saves_editing_name: bool = False
        self._saves_edit_name_text: str = ""
        self._saves_edit_caret: int = 0
        self._last_click_time: float = 0.0
        self._last_click_pos: tuple[int, int] | None = None
        # Hover del botón Cargar
        self._saves_hover_load_button: bool = False
        # Hover del botón Borrar
        self._saves_hover_delete_button: bool = False
        # Modal de confirmación de borrado
        self._saves_show_confirm_delete: bool = False
        self._saves_hover_confirm_yes: bool = False
        self._saves_hover_confirm_cancel: bool = False
        # Selección total para edición inline (doble click)
        self._saves_select_all_edit: bool = False
        # Guardar configuración previa de key repeat para restaurar al salir de edición
        self._prev_key_repeat: tuple[int, int] | None = None

        # Fondo opcional del menú (pantalla de inicio)
        self.background_path: str | None = background_path
        self._bg_surface: pygame.Surface | None = None
        self._bg_scaled_cache: pygame.Surface | None = None
        # Tamaño del surface escalado (w,h) y de la pantalla para la que fue calculado
        self._bg_scaled_size: tuple[int, int] | None = None
        self._bg_scaled_screen_size: tuple[int, int] | None = None
        # Offset donde blitear el fondo escalado para centrarlo
        self._bg_scaled_offset: tuple[int, int] = (0, 0)
        # Modo de escala: 'cover' (rellena pantalla, puede recortar) o 'contain' (encaja, puede dejar bandas)
        self._bg_scale_mode: str = "cover"

        # Carrusel de fondos (varias imágenes)
        self.backgrounds: list[str] = []
        self._bg_surfaces_list: list[pygame.Surface] = []
        self._bg_scaled_list: list[pygame.Surface] = []
        self._bg_last_screen_size: tuple[int, int] | None = None
        self._bg_index: int = 0
        self._bg_prev_index: int | None = None
        self._bg_last_switch_time: float = time.time()
        self._bg_interval_s: float = 2.0
        self._bg_transition_s: float = 0.6
        self._bg_transition_start: float | None = None
        self._bg_slide_px: int = 24

    # ---- Configuración de fondo ----
    def set_background(self, path: str | None, *, scale_mode: str | None = None):
        """Configura la ruta del fondo del menú de inicio y reinicia la caché de escala."""
        self.background_path = path
        if scale_mode in ("cover", "contain"):
            self._bg_scale_mode = scale_mode
        self._bg_surface = None
        self._bg_scaled_cache = None
        self._bg_scaled_size = None
        self._bg_scaled_screen_size = None
        self._bg_scaled_offset = (0, 0)

    def _ensure_background_loaded(self):
        if self._bg_surface is None and self.background_path:
            try:
                surf = pygame.image.load(self.background_path)
                # Si tiene alpha, mantenerla
                try:
                    surf = surf.convert_alpha()
                except Exception:
                    surf = surf.convert()
                self._bg_surface = surf
            except Exception as e:
                logging.getLogger(__name__).warning("No se pudo cargar el fondo del menú: %s", e)
                self._bg_surface = None

    def _blit_background_if_any(self, screen):
        """Dibuja el fondo manteniendo aspect ratio con modo 'cover' o 'contain'."""
        if not self.background_path:
            return
        self._ensure_background_loaded()
        if self._bg_surface is None:
            return
        sw, sh = screen.get_size()
        iw, ih = self._bg_surface.get_size()
        # Recalcular caché si cambia pantalla o no existe
        if (
            self._bg_scaled_cache is None
            or self._bg_scaled_screen_size != (sw, sh)
            or self._bg_scaled_size is None
        ):
            try:
                if iw == 0 or ih == 0:
                    return
                if self._bg_scale_mode == "contain":
                    scale = min(sw / iw, sh / ih)
                else:
                    scale = max(sw / iw, sh / ih)
                new_w = max(1, int(iw * scale))
                new_h = max(1, int(ih * scale))
                self._bg_scaled_cache = pygame.transform.scale(self._bg_surface, (new_w, new_h))
                self._bg_scaled_size = (new_w, new_h)
                self._bg_scaled_screen_size = (sw, sh)
                # Centrar; si es 'cover' y new_w/h > screen, quedará negativo (recorte natural)
                off_x = (sw - new_w) // 2
                off_y = (sh - new_h) // 2
                self._bg_scaled_offset = (off_x, off_y)
            except Exception:
                # Fallback: usar la original si falla el escalado
                self._bg_scaled_cache = self._bg_surface
                self._bg_scaled_size = self._bg_surface.get_size()
                self._bg_scaled_screen_size = (sw, sh)
                off_x = (sw - self._bg_scaled_size[0]) // 2
                off_y = (sh - self._bg_scaled_size[1]) // 2
                self._bg_scaled_offset = (off_x, off_y)
        surface_to_blit = self._bg_scaled_cache._surf if hasattr(self._bg_scaled_cache, '_surf') else self._bg_scaled_cache
        screen.blit(surface_to_blit, self._bg_scaled_offset)

    # ---- Carrusel de fondos ----
    def set_backgrounds(self, paths: list[str], interval_s: float = 2.0, transition_s: float = 0.6, slide_px: int = 24, scale_mode: str = "cover"):
        """Configura un carrusel de imágenes de fondo.
        - paths: lista de rutas relativas/absolutas
        - interval_s: segundos visibles por imagen antes de transicionar
        - transition_s: duración del crossfade/slide
        - slide_px: desplazamiento horizontal ligero durante la transición
        """
        self.backgrounds = [p for p in paths if p]
        if scale_mode in ("cover", "contain"):
            self._bg_scale_mode = scale_mode
        self._bg_interval_s = max(0.1, float(interval_s))
        self._bg_transition_s = max(0.0, float(transition_s))
        self._bg_slide_px = int(slide_px)
        self._bg_index = 0
        self._bg_prev_index = None
        self._bg_last_switch_time = time.time()
        self._bg_transition_start = None
        # Limpiar cachés
        self._bg_surfaces_list = []
        self._bg_scaled_list = []
        self._bg_offsets_list = []
        self._bg_last_screen_size = None
        # Anular fondo único para que prevalezca el carrusel
        if self.backgrounds:
            self.background_path = None
            self._bg_surface = None
            self._bg_scaled_cache = None
            self._bg_scaled_size = None
            self._bg_scaled_screen_size = None
            self._bg_scaled_offset = (0, 0)

    def _reset_backgrounds_cache(self):
        self._bg_surfaces_list = []
        self._bg_scaled_list = []
        self._bg_offsets_list = []
        self._bg_last_screen_size = None

    def _ensure_backgrounds_loaded_and_scaled(self, screen):
        if not self.backgrounds:
            return False
        # Cargar originales si aún no
        if not self._bg_surfaces_list:
            for p in self.backgrounds:
                try:
                    surf = pygame.image.load(p)
                    try:
                        surf = surf.convert_alpha()
                    except Exception:
                        surf = surf.convert()
                    self._bg_surfaces_list.append(surf)
                except Exception as e:
                    logging.getLogger(__name__).warning("No se pudo cargar fondo '%s': %s", p, e)
                    # Placeholder: superficie negra
                    ph = pygame.Surface(screen.get_size())
                    ph.fill((0, 0, 0))
                    self._bg_surfaces_list.append(ph)
        # Escalar si cambia el tamaño de pantalla o aún no hay escalados
        sw, sh = screen.get_size()
        if (not self._bg_scaled_list) or (self._bg_last_screen_size != (sw, sh)):
            self._bg_scaled_list = []
            self._bg_offsets_list = []
            for s in self._bg_surfaces_list:
                try:
                    iw, ih = s.get_size()
                    if iw == 0 or ih == 0:
                        iw, ih = 1, 1
                    if self._bg_scale_mode == "contain":
                        scale = min(sw / iw, sh / ih)
                    else:
                        scale = max(sw / iw, sh / ih)
                    new_w = max(1, int(iw * scale))
                    new_h = max(1, int(ih * scale))
                    scaled = pygame.transform.scale(s, (new_w, new_h))
                except Exception:
                    scaled = s
                    new_w, new_h = scaled.get_size()
                off_x = (sw - new_w) // 2
                off_y = (sh - new_h) // 2
                self._bg_scaled_list.append(scaled)
                self._bg_offsets_list.append((off_x, off_y))
            self._bg_last_screen_size = (sw, sh)
        return True

    def _update_background_cycle_state(self):
        if not self.backgrounds:
            return
        now = time.time()
        # Si hay transición en curso, finalizar si terminó
        if self._bg_transition_start is not None:
            t = now - self._bg_transition_start
            if t >= self._bg_transition_s:
                # Fin de transición
                self._bg_prev_index = None
                self._bg_transition_start = None
                self._bg_last_switch_time = now
            return
        # Iniciar transición si venció el intervalo
        if (now - self._bg_last_switch_time) >= self._bg_interval_s and len(self.backgrounds) > 1:
            self._bg_prev_index = self._bg_index
            self._bg_index = (self._bg_index + 1) % len(self.backgrounds)
            self._bg_transition_start = now

    def _blit_backgrounds(self, screen):
        """Dibuja el fondo (carrusel si está configurado, si no fondo único)."""
        if self.backgrounds:
            if not self._ensure_backgrounds_loaded_and_scaled(screen):
                return
            self._update_background_cycle_state()
            # Si no hay transición, dibujar imagen actual centrada
            cur = self._bg_scaled_list[self._bg_index]
            cur_off = self._bg_offsets_list[self._bg_index] if len(self._bg_offsets_list) > self._bg_index else (0, 0)
            if self._bg_transition_start is None or self._bg_prev_index is None or self._bg_transition_s <= 0.0:
                surf = cur._surf if hasattr(cur, '_surf') else cur
                screen.blit(surf, cur_off)
                return
            # Transición cruzada con pequeño slide
            prev = self._bg_scaled_list[self._bg_prev_index]
            prev_off = self._bg_offsets_list[self._bg_prev_index] if len(self._bg_offsets_list) > self._bg_prev_index else (0, 0)
            now = time.time()
            t = (now - self._bg_transition_start) / max(1e-6, self._bg_transition_s)
            t = max(0.0, min(1.0, t))
            alpha_prev = int(255 * (1.0 - t))
            alpha_next = int(255 * t)
            dx_prev = int(-self._bg_slide_px * t)
            dx_next = int(self._bg_slide_px * (1.0 - t))
            try:
                prev.set_alpha(alpha_prev)
                surf_prev = prev._surf if hasattr(prev, '_surf') else prev
                screen.blit(surf_prev, (prev_off[0] + dx_prev, prev_off[1]))
            finally:
                try:
                    prev.set_alpha(None)
                except Exception:
                    pass
            try:
                cur.set_alpha(alpha_next)
                surf_cur = cur._surf if hasattr(cur, '_surf') else cur
                screen.blit(surf_cur, (cur_off[0] + dx_next, cur_off[1]))
            finally:
                try:
                    cur.set_alpha(None)
                except Exception:
                    pass
            return
        # Fallback: fondo único
        self._blit_background_if_any(screen)

    def handle_input(self, event):
        """
        Procesa la entrada del menú y devuelve la opción seleccionada o None.
        """
        # Modo especial: lista de partidas
        if self.mode == "load_list":
            if event.type == pygame.KEYDOWN:
                # Si está visible el modal de borrar, manejar teclas allí
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
                # Si estamos editando el nombre, capturar edición primero
                if self._saves_editing_name:
                    if event.key == pygame.K_ESCAPE:
                        # Cancelar edición
                        self._end_edit_save_name(cancel=True)
                        return None
                    if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                        # Commit del renombrado
                        self._commit_save_rename()
                        return None
                    if event.key == pygame.K_BACKSPACE:
                        if self._saves_select_all_edit:
                            # Backspace con todo seleccionado: limpiar
                            self._saves_edit_name_text = ""
                            self._saves_edit_caret = 0
                            self._saves_select_all_edit = False
                            return None
                        mods = pygame.key.get_mods()
                        if mods & pygame.KMOD_CTRL:
                            # Borrar palabra hacia la izquierda
                            i = self._saves_edit_caret
                            text = self._saves_edit_name_text
                            if i > 0 and text:
                                j = i
                                # saltar espacios a la izquierda
                                while j > 0 and text[j-1].isspace():
                                    j -= 1
                                # borrar hasta inicio o espacio anterior
                                while j > 0 and not text[j-1].isspace():
                                    j -= 1
                                self._saves_edit_name_text = text[:j] + text[i:]
                                self._saves_edit_caret = j
                        else:
                            if self._saves_edit_caret > 0 and len(self._saves_edit_name_text) > 0:
                                i = self._saves_edit_caret
                                self._saves_edit_name_text = self._saves_edit_name_text[:i-1] + self._saves_edit_name_text[i:]
                                self._saves_edit_caret -= 1
                        return None
                    if event.key == pygame.K_DELETE:
                        if self._saves_select_all_edit:
                            # Delete con todo seleccionado: limpiar
                            self._saves_edit_name_text = ""
                            self._saves_edit_caret = 0
                            self._saves_select_all_edit = False
                            return None
                        mods = pygame.key.get_mods()
                        if mods & pygame.KMOD_CTRL:
                            # Borrar palabra hacia la derecha
                            i = self._saves_edit_caret
                            text = self._saves_edit_name_text
                            if i < len(text):
                                j = i
                                # saltar espacios a la derecha
                                while j < len(text) and text[j].isspace():
                                    j += 1
                                # borrar hasta siguiente espacio/fin
                                while j < len(text) and not text[j].isspace():
                                    j += 1
                                self._saves_edit_name_text = text[:i] + text[j:]
                        else:
                            i = self._saves_edit_caret
                            if i < len(self._saves_edit_name_text):
                                self._saves_edit_name_text = self._saves_edit_name_text[:i] + self._saves_edit_name_text[i+1:]
                        return None
                    if event.key in (pygame.K_LEFT, pygame.K_KP_4):
                        if self._saves_select_all_edit:
                            # Mover caret al inicio y salir del select-all
                            self._saves_edit_caret = 0
                            self._saves_select_all_edit = False
                            return None
                        mods = pygame.key.get_mods()
                        if mods & pygame.KMOD_CTRL:
                            # Mover caret a inicio de palabra anterior
                            i = self._saves_edit_caret
                            text = self._saves_edit_name_text
                            j = i
                            while j > 0 and text[j-1].isspace():
                                j -= 1
                            while j > 0 and not text[j-1].isspace():
                                j -= 1
                            self._saves_edit_caret = j
                        else:
                            self._saves_edit_caret = max(0, self._saves_edit_caret - 1)
                        return None
                    if event.key in (pygame.K_RIGHT, pygame.K_KP_6):
                        if self._saves_select_all_edit:
                            # Mover caret al final y salir del select-all
                            self._saves_edit_caret = len(self._saves_edit_name_text)
                            self._saves_select_all_edit = False
                            return None
                        mods = pygame.key.get_mods()
                        if mods & pygame.KMOD_CTRL:
                            # Mover caret al inicio de la siguiente palabra
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
                    # Entrada de texto básica por unicode
                    ch = getattr(event, 'unicode', '') or ''
                    if ch and ord(ch) >= 32:
                        if self._saves_select_all_edit:
                            # Reemplazar selección completa por nueva entrada
                            self._saves_edit_name_text = ch
                            self._saves_edit_caret = len(ch)
                            self._saves_select_all_edit = False
                        else:
                            i = self._saves_edit_caret
                            self._saves_edit_name_text = self._saves_edit_name_text[:i] + ch + self._saves_edit_name_text[i:]
                            self._saves_edit_caret += len(ch)
                    return None

                if event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
                    if self.save_entries:
                        self.load_selected = (self.load_selected - 1) % len(self.save_entries)
                        # salir de edición si cambia selección
                        self._end_edit_save_name(cancel=True)
                        # Mantener visible
                        layout = getattr(self.renderer, 'last_saves_layout', None)
                        if layout:
                            start = layout.get('start', 0)
                            end = layout.get('end', 0)
                            if self.load_selected < start:
                                self._saves_row_scroll_offset = self.load_selected
                elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
                    if self.save_entries:
                        self.load_selected = (self.load_selected + 1) % len(self.save_entries)
                        self._end_edit_save_name(cancel=True)
                        # Mantener visible
                        layout = getattr(self.renderer, 'last_saves_layout', None)
                        if layout:
                            start = layout.get('start', 0)
                            end = layout.get('end', 0)
                            visible = max(1, end - start)
                            if self.load_selected >= end:
                                self._saves_row_scroll_offset = max(0, self.load_selected - (visible - 1))
                elif event.key in (pygame.K_PAGEUP,):
                    # PageUp: retrocede un bloque visible
                    layout = getattr(self.renderer, 'last_saves_layout', {})
                    start = layout.get('start', 0)
                    max_jump = max(1, (layout.get('end', 0) - start))
                    self._saves_row_scroll_offset = max(0, self._saves_row_scroll_offset - max_jump)
                elif event.key in (pygame.K_PAGEDOWN,):
                    layout = getattr(self.renderer, 'last_saves_layout', {})
                    start = layout.get('start', 0)
                    max_jump = max(1, (layout.get('end', 0) - start))
                    max_off = max(0, len(self.save_entries) - max_jump)
                    self._saves_row_scroll_offset = min(max_off, self._saves_row_scroll_offset + max_jump)
                elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                    # Deshabilitado: la única forma de cargar es el botón "Cargar"
                    return None
                return None

            if event.type == pygame.MOUSEMOTION:
                # Si modal visible: actualizar hover de botones del modal y salir
                if self._saves_show_confirm_delete:
                    self._saves_hover_confirm_yes = False
                    self._saves_hover_confirm_cancel = False
                    layout_c = getattr(self.renderer, 'last_confirm_layout', None)
                    if layout_c:
                        yes_rect = layout_c.get('yes_rect')
                        cancel_rect = layout_c.get('cancel_rect')
                        if yes_rect and yes_rect.collidepoint(event.pos):
                            self._saves_hover_confirm_yes = True
                        if cancel_rect and cancel_rect.collidepoint(event.pos):
                            self._saves_hover_confirm_cancel = True
                    return None
                # Hover sobre filas
                layout = getattr(self.renderer, 'last_saves_layout', None)
                self._saves_hovered_idx = None
                self._saves_hover_details_name = False
                self._saves_hover_load_button = False
                self._saves_hover_delete_button = False
                if layout:
                    for idx, rect in layout.get('row_rects', {}).items():
                        if rect.collidepoint(event.pos):
                            self._saves_hovered_idx = idx
                            break
                    # Hover sobre campo Nombre en detalles
                    name_rect = layout.get('details_name_rect')
                    if name_rect and name_rect.collidepoint(event.pos):
                        self._saves_hover_details_name = True
                    # Hover sobre botón Cargar
                    btn_rect = layout.get('load_button_rect')
                    if btn_rect and btn_rect.collidepoint(event.pos):
                        self._saves_hover_load_button = True
                    # Hover sobre botón Borrar
                    del_rect = layout.get('delete_button_rect')
                    if del_rect and del_rect.collidepoint(event.pos):
                        self._saves_hover_delete_button = True
            elif event.type == pygame.MOUSEWHEEL:
                # Scroll vertical de la lista
                self._saves_row_scroll_offset = max(0, self._saves_row_scroll_offset - event.y)
            elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                # Click para seleccionar/cargar
                # Si modal visible: click en botones o fuera para cancelar
                if self._saves_show_confirm_delete:
                    layout_c = getattr(self.renderer, 'last_confirm_layout', None)
                    if layout_c:
                        panel_rect = layout_c.get('panel_rect')
                        yes_rect = layout_c.get('yes_rect')
                        cancel_rect = layout_c.get('cancel_rect')
                        if yes_rect and yes_rect.collidepoint(event.pos):
                            self._confirm_delete_selected_save()
                            return None
                        if cancel_rect and cancel_rect.collidepoint(event.pos):
                            self._saves_show_confirm_delete = False
                            return None
                        # Click fuera del panel: cancelar
                        if panel_rect and not panel_rect.collidepoint(event.pos):
                            self._saves_show_confirm_delete = False
                            return None
                    return None

                layout = getattr(self.renderer, 'last_saves_layout', None)
                if layout:
                    # Click en botón Cargar -> cargar partida seleccionada
                    btn_rect = layout.get('load_button_rect')
                    if btn_rect and btn_rect.collidepoint(event.pos):
                        self._load_selected_save()
                        return None
                    # Click en botón Borrar -> abrir confirmación
                    del_rect = layout.get('delete_button_rect')
                    if del_rect and del_rect.collidepoint(event.pos):
                        self._saves_show_confirm_delete = True
                        # Salir de edición si estaba activo
                        self._end_edit_save_name(cancel=False)
                        return None
                    # Click en detalles -> posible doble click para editar o mover caret
                    name_rect = layout.get('details_name_rect')
                    if name_rect and name_rect.collidepoint(event.pos):
                        now = time.time()
                        dbl = False
                        if self._last_click_time and self._last_click_pos:
                            dt = (now - self._last_click_time)
                            dx = abs(event.pos[0] - self._last_click_pos[0])
                            dy = abs(event.pos[1] - self._last_click_pos[1])
                            if dt <= 0.35 and dx <= 6 and dy <= 6:
                                dbl = True
                        self._last_click_time = now
                        self._last_click_pos = event.pos
                        if dbl:
                            # Entrar en modo edición con seleccionar todo
                            self._begin_edit_save_name()
                            self._saves_select_all_edit = True
                        else:
                            # Si ya estamos editando, ajustar caret según x
                            if self._saves_editing_name:
                                self._set_caret_from_click(event.pos)
                                self._saves_select_all_edit = False
                        return None

                    for idx, rect in layout.get('row_rects', {}).items():
                        if rect.collidepoint(event.pos):
                            self.load_selected = idx
                            # Salir de edición si cambia selección
                            self._end_edit_save_name(cancel=True)
                            break
            return None

        self.handler.mode = self.mode
        return self.handler.handle_input(event)

    def draw(self, screen):
        """
        Dibuja el menú y devuelve el rect para dirty rects.
        """
        # Fondo (único o carrusel) para menú de inicio (y lista de partidas) antes del overlay
        if self.mode in ("start", "load_list"):
            self._blit_backgrounds(screen)
        # Vista especial: lista de partidas
        if self.mode == "load_list":
            # Recalcular layout fijo si cambia tamaño de ventana
            if self._saves_fixed_screen_size != screen.get_size():
                self._compute_saves_fixed_layout(screen)
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
            )
            # Si hay confirmación de borrado, dibujar modal por encima
            if self._saves_show_confirm_delete:
                # Construir líneas del mensaje con el nombre del guardado seleccionado
                name = "-"
                if self.save_entries:
                    entry = self.save_entries[self.load_selected]
                    name = (entry.get('meta') or {}).get('name') or entry.get('label') or '-'
                lines = [
                    "¿Borrar esta partida?",
                    f"{name}",
                    "Esta acción no se puede deshacer."
                ]
                overlay_rect = self.renderer.draw_confirm_dialog(
                    screen,
                    lines,
                    hover_yes=self._saves_hover_confirm_yes,
                    hover_cancel=self._saves_hover_confirm_cancel,
                )
            return overlay_rect

        self.handler.mode = self.mode
        options = self.handler.get_options()
        selected = self.handler.selected
        return self.renderer.draw(screen, selected, options)

    def execute_menu_option(self, selected, state):
        """
        Ejecuta la acción seleccionada en el menú.
        """
        # Opción 'Continuar': cerrar menú y reanudar juego
        if selected == "Continuar":
            self.show_menu = False
            return
        # Resto de acciones
        if selected == "Guardar partida":
            self._action_save()
        elif selected in ("Nuevo juego", "Nueva Partida"):
            self._action_new_game()
        elif selected == "Cargar juego":
            # Cambiar a submenú de selección de partidas
            self._enter_load_list()
        elif selected == "Opciones":
            # Abrir configurador de botones (opciones)
            self.configurator.configure()
        else:
            # Delegar en handler para opciones existentes (modo, salir, configurar botones)
            self.handler.execute_option(selected)

    # ---- API de control ----
    def set_mode(self, mode: str):
        """Establece el modo del menú: 'start' o 'pause' o 'load_list'."""
        if mode not in ("start", "pause", "load_list"):
            logger.warning("Modo de menú desconocido: %s", mode)
            return
        self.mode = mode
        # Reiniciar selección al cambiar de menú
        self.handler.selected = 0

    # ---- Acciones ----
    def _action_save(self):
        """Guardar juego sin salir."""
        try:
            self.game.shutdown_manager.shutdown()
            logger.info("Partida guardada correctamente.")
        except Exception as e:
            logger.warning("Error al guardar partida: %s", e)

    def _action_new_game(self):
        """Inicia una partida nueva en memoria (sin borrar archivos)."""
        g = self.game
        try:
            # 0) Resolución de nivel base
            try:
                level_name = getattr(g.map, 'name', None)
            except Exception:
                level_name = None
            if not level_name:
                # Si no está resuelto aún, intentar desde player en pending
                pdata = getattr(g, 'world', None)
                # Fallback al nombre actual del mapa si no hay nada
                level_name = g.map.name
            # 1) Limpiar estado persistente del mundo (NPCs, inventarios)
            try:
                g.world.npc_memory = {}
                g.world.npc_inventories = {}
                g.world.player_inventory = None
            except Exception:
                pass

            # 2) Reset de niveles cargados y diferidos
            try:
                if hasattr(g.world, 'maps'):
                    g.world.maps.clear()
                if hasattr(g.world, '_pending_levels'):
                    g.world._pending_levels = {}
                g.world.current_level = None
            except Exception:
                pass

            # 3) Crear mapa limpio y sincronizar ECS
            try:
                # Saltar colocación de spawners SOLO este frame
                try:
                    setattr(g.ecs.ecs_world, 'skip_spawners_on_first_load', True)
                except Exception:
                    pass
                new_map = MapManager(level_name)
                g.map = new_map
                g.world.maps[level_name] = new_map
                g.world.current_level = level_name
                if hasattr(g.map, '_local_state'):
                    g.map._local_state["player_pos"] = None

                # Sincronizar ECS con el nuevo mapa y limpiar entidades previas (NPCs/Spawners/Requests)
                try:
                    ecs = g.ecs.ecs_world
                    try:
                        ecs.map_manager = new_map
                        ecs.invalidate_spatial_index()
                    except Exception:
                        pass
                    comps = ecs.components
                    for eid in list(comps.get('NPCTagComponent', {}).keys()):
                        ecs.remove_entity(eid)
                    for eid in list(comps.get('SpawnerConfig', {}).keys()):
                        ecs.remove_entity(eid)
                    for eid in list(comps.get('SpawnRequest', {}).keys()):
                        ecs.remove_entity(eid)
                    # Reset de flags internos para que los sistemas apliquen en el siguiente frame
                    try:
                        for sys in getattr(ecs, 'update_systems', []) or []:
                            if isinstance(sys, SpawnerPlacementSystem):
                                sys._loaded = False
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
                except Exception:
                    pass
            except Exception as e:
                pass

            # 4) Posicionar jugador en el centro del lobby
            try:
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings
                tx = off_x + global_map_settings.zone_width // 2
                ty = off_y + global_map_settings.zone_height // 2
            except Exception:
                tx, ty = 0, 0
            g.map.spawn_player((tx, ty))
            # Convertir a píxeles y mover componente Position
            px, py = g.map.get_spawn_pixel((tx, ty))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            # 3) Resetear inventario del jugador a 10 monedas de oro
            try:
                from roguelike_game.ecs.components.inventory_component import InventoryComponent
                eid = g.ecs.ecs_world.player_entity
                inv = InventoryComponent(capacity=20, player_id="player")
                inv.add("gold", 10)
                g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                # Reflejar en WorldManager para persistencia inmediata en próximos guardados
                if hasattr(g, 'world'):
                    g.world.player_inventory = inv.serialize()
            except Exception as e:
                logger.warning("No se pudo inicializar inventario de nuevo juego: %s", e)
            # 3c) Resetear experiencia/nivel del jugador a 0
            try:
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                xp_comp.xp = 0
                xp_comp.level = 0
                # xp_to_next_level se mantiene por defecto
            except Exception as e:
                logger.warning("No se pudo reiniciar experiencia de nuevo juego: %s", e)
            # 3b) Establecer un nuevo slot de guardado con nombre 'partida_YYYY-MM-DD_HH-MM-SS.json'
            try:
                ts = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
                save_dir: Path = g.world.config.save_dir
                save_dir.mkdir(parents=True, exist_ok=True)
                slot_path = save_dir / f"partida_{ts}.json"
                g.world.current_save_path = str(slot_path)
                # Preparar metadatos iniciales
                g.world.save_metadata = {
                    "name": f"partida_{ts}",
                    "created_at": datetime.now().isoformat(timespec='seconds'),
                    "last_played": datetime.now().isoformat(timespec='seconds'),
                }
            except Exception as e:
                logger.warning("No se pudo preparar slot de guardado: %s", e)
            # 4) Salir al juego
            self.show_menu = False
            # Asegurar modo pausa en siguientes aperturas
            self.mode = "pause"
            # Guardado inicial para crear archivo del slot
            try:
                g.shutdown_manager.shutdown()
            except Exception:
                pass
            logger.info("Nuevo juego iniciado (en memoria)")
        except Exception as e:
            logger.error("Error al iniciar nuevo juego: %s", e)

    def _action_load_game(self):
        """Carga partida desde el slot actual (modo legacy). Preferir _enter_load_list."""
        g = self.game
        try:
            # 1) Cargar estado mundial desde disco
            g.world.load_world()
            # Descubrir nivel actual guardado
            level = getattr(g.world, 'current_level', None)
            if not level:
                # Si no está resuelto aún, intentar desde player en pending
                pdata = getattr(g, 'world', None)
                # Fallback al nombre actual del mapa si no hay nada
                level = g.map.name
            # 2) Cargar nivel (MapManager) y asignarlo a game
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            # 3) Restaurar posición del jugador
            tile = g.map._local_state.get("player_pos")
            if tile is None:
                # Fallback: centro del lobby
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
            # 4) Restaurar inventario si fue guardado en world
            try:
                pdata = getattr(g.world, 'player_inventory', None)
                if pdata:
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent
                    inv = InventoryComponent(capacity=pdata.get('capacity', 20), player_id=pdata.get('player_id'))
                    for slot in pdata.get('slots', []):
                        if slot:
                            inv.add(slot['item'], slot.get('quantity', 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                    # Sincronizar perfil activo con el snapshot cargado
                    try:
                        snap = inv.serialize() if hasattr(inv, 'serialize') else {}
                        if 'player_id' not in snap:
                            snap['player_id'] = pdata.get('player_id')
                        write_active_for_player(eid, snap)
                    except Exception:
                        pass
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)

            # 4a) Inyectar snapshot de inventarios de NPCs en ECS para que InventoryInitSystem los restaure
            try:
                npc_snap = getattr(g.world, 'npc_inventories', None) or {}
                if npc_snap:
                    g.ecs.ecs_world.components['NPCInventorySnapshot'] = dict(npc_snap)
                    logger.info("NPCInventorySnapshot inyectado (%d NPCs)", len(npc_snap))
            except Exception as e:
                logger.warning("No se pudo inyectar NPCInventorySnapshot: %s", e)
            # 4b) Restaurar XP/Nivel desde metadatos del guardado
            try:
                meta = getattr(g.world, 'save_metadata', {}) or {}
                p = meta.get('player', {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get('xp') is not None:
                    xp_comp.xp = int(p['xp'])
                if p.get('level') is not None:
                    xp_comp.level = int(p['level'])
                # Reflejar de vuelta en metadatos por coherencia
                meta.setdefault('player', {})
                meta['player']['xp'] = int(xp_comp.xp)
                meta['player']['level'] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
            # 5) Cerrar menú y pasar a modo pausa para próximas aperturas
            self.show_menu = False
            self.mode = "pause"
            logger.info("Partida cargada: nivel=%s", level)
        except Exception as e:
            logger.error("Error al cargar partida: %s", e)

    # ---- Load list helpers ----
    def _enter_load_list(self):
        """Prepara y entra al modo de lista de partidas guardadas."""
        self._refresh_save_list()
        self.load_selected = 0
        self.prev_mode = self.mode
        # Reset de scroll y hover
        self._saves_row_scroll_offset = 0
        self._saves_hovered_idx = None
        self._saves_hover_details_name = False
        self._saves_editing_name = False
        # Preparar layout fijo inicial
        self._compute_saves_fixed_layout(self.screen)
        self.set_mode("load_list")

    def _refresh_save_list(self):
        """Escanea el directorio de guardados y construye entradas ordenadas por fecha reciente."""
        g = self.game
        save_dir: Path = g.world.config.save_dir
        save_dir.mkdir(parents=True, exist_ok=True)
        entries: list[dict] = []
        # Buscar archivos que empiecen con 'partida_' y terminen en .json
        for path in sorted(save_dir.glob('partida_*.json'), reverse=True):
            try:
                data = self.game.world.repository.load_from_path(str(path))
            except Exception:
                data = {}
            meta = data.get("meta") or {}
            label = meta.get("name") or path.stem
            entries.append({"path": str(path), "label": label, "meta": meta})
        self.save_entries = entries

    def _compute_saves_fixed_layout(self, screen):
        """Calcula tamaños fijos del panel y columnas para la lista de partidas.
        Usa el máximo de etiquetas y detalles a través de todos los guardados.
        """
        font = self.renderer.font
        # Ancho de la lista (labels)
        list_max_w = 0
        for e in self.save_entries:
            tw, _ = font.size(e.get('label', ''))
            list_max_w = max(list_max_w, tw)
        # Ancho de detalles: medir en todas las entradas el mayor renglón
        details_max_w = 0
        for e in self.save_entries:
            lines = self._format_save_details(e.get('meta') or {})
            for line in lines:
                tw, _ = font.size(line)
                details_max_w = max(details_max_w, tw)
        # Fallback si no hay entradas
        if not self.save_entries:
            details_max_w = max(details_max_w, font.size("Sin metadatos")[0])
            list_max_w = max(list_max_w, font.size("-")[0])

        col_gap = 32
        w = self.renderer.padding_x * 2 + list_max_w + col_gap + details_max_w + 12
        # Alto: un mínimo razonable de 8 filas visibles sin overflow, luego clamp
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

    def _format_save_details(self, meta: dict) -> list[str]:
        """Construye líneas de detalle para el panel de info de guardado."""
        if not meta:
            return ["Sin metadatos", "Pulsa Enter para cargar"]
        lines = []
        lines.append(f"Nombre: {meta.get('name', '-')}")
        lines.append(f"Creada: {meta.get('created_at', '-')}")
        lines.append(f"Última vez: {meta.get('last_played', '-')}")
        p = meta.get('player', {}) or {}
        lines.append(f"Nivel: {p.get('level', '-')}")
        lines.append(f"XP: {p.get('xp', '-')}")
        it = meta.get('items_summary', {}) or {}
        lines.append(f"Pilas: {it.get('stacks', 0)}")
        top = it.get('top_items') or []
        if top:
            lines.append("Items: " + ", ".join([str(x) for x in top]))
        return lines

    def _load_selected_save(self):
        """Carga el save seleccionado de la lista y entra al juego."""
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        path = entry["path"]
        g = self.game
        try:
            # Cargar mundo desde path específico y recordar slot activo
            g.world.load_world(path)
            # Determinar nivel actual y cargarlo
            level = getattr(g.world, 'current_level', None) or g.map.name
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            # --- Limpieza robusta para evitar solapamiento entre slots ---
            try:
                ecs = g.ecs.ecs_world
                comps = ecs.components
                # 1) Eliminar NPCs existentes del slot previo
                for eid in list(comps.get('NPCTagComponent', {}).keys()):
                    ecs.remove_entity(eid)
                # 2) Eliminar entidades de Spawner (config/estado) y requests pendientes
                for eid in list(comps.get('SpawnerConfig', {}).keys()):
                    ecs.remove_entity(eid)
                for eid in list(comps.get('SpawnRequest', {}).keys()):
                    ecs.remove_entity(eid)
                # 3) Resetear banderas internas de sistemas relacionados para forzar recolocación/aplicación
                try:
                    for sys in getattr(ecs, 'update_systems', []) or []:
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
                # 4) Inyectar snapshot de inventarios de NPCs del nuevo save para coherencia con InventoryInitSystem
                try:
                    ecs.components['NPCInventorySnapshot'] = dict(getattr(g.world, 'npc_inventories', {}) or {})
                except Exception:
                    pass
                # 5) Limpiar vínculos visuales de spawners en buildings (si los hubiera)
                try:
                    blds = getattr(g, 'buildings', None)
                    if blds is not None:
                        for ob in getattr(blds, 'buildings', []) or []:
                            try:
                                if hasattr(ob, '_spawner_eid'):
                                    setattr(ob, '_spawner_eid', None)
                                if hasattr(ob, '_world_ref'):
                                    setattr(ob, '_world_ref', None)
                                if hasattr(ob, '_is_spawner_visual'):
                                    setattr(ob, '_is_spawner_visual', False)
                            except Exception:
                                continue
                except Exception:
                    pass
                # 6) Marcar el índice espacial para reconstrucción
                try:
                    ecs.invalidate_spatial_index()
                except Exception:
                    pass
            except Exception:
                # No bloquear carga de partida por fallo en limpieza
                pass
            # Restaurar posición del jugador
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
            # Restaurar inventario
            try:
                pdata = getattr(g.world, 'player_inventory', None)
                if pdata:
                    # Normalizar player_id: usar activo por eid o generar UUID si falta/ inválido
                    def _valid_uuid(x):
                        try:
                            uuid.UUID(str(x))
                            return True
                        except Exception:
                            return False
                    pid = pdata.get('player_id')
                    if not _valid_uuid(pid):
                        try:
                            eid = g.ecs.ecs_world.player_entity
                            active_path = Path('data/inventory/active/inventory_player.json')
                            active = json.loads(active_path.read_text(encoding='utf-8')) if active_path.exists() else {}
                            apid = (active.get(str(eid)) or {}).get('player_id')
                            if not _valid_uuid(apid):
                                apid = active.get('player_id')
                            pid = apid if _valid_uuid(apid) else str(uuid.uuid4())
                        except Exception:
                            pid = str(uuid.uuid4())
                        # Persistir de vuelta al save para consistencia futura
                        try:
                            pdata['player_id'] = pid
                            repo = g.world.repository
                            data = repo.load_from_path(str(path))
                            data.setdefault('player_inventory', {})
                            data['player_inventory']['player_id'] = pid
                            snapshot = WorldSnapshot(
                                version=data.get('version', 1),
                                player=data.get('player'),
                                npcs=data.get('npcs', {}),
                                levels=data.get('levels', {}),
                                player_inventory=data.get('player_inventory'),
                                npc_inventories=data.get('npc_inventories'),
                                meta=data.get('meta'),
                            )
                            repo.save_to_path(str(path), snapshot)
                            g.world.player_inventory = data.get('player_inventory', pdata)
                        except Exception:
                            # Si no se puede persistir, al menos continuar en runtime
                            pass
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent
                    inv = InventoryComponent(capacity=pdata.get('capacity', 20), player_id=pdata.get('player_id'))
                    for slot in pdata.get('slots', []):
                        if slot:
                            inv.add(slot['item'], slot.get('quantity', 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                    # Sincronizar perfil activo con el snapshot cargado para consistencia con _action_load_game
                    try:
                        snap = inv.serialize() if hasattr(inv, 'serialize') else {}
                        if 'player_id' not in snap:
                            snap['player_id'] = pdata.get('player_id')
                        write_active_for_player(eid, snap)
                    except Exception:
                        pass
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)
            
            # Restaurar XP/Nivel desde metadatos del guardado
            try:
                meta = getattr(g.world, 'save_metadata', {}) or {}
                p = meta.get('player', {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get('xp') is not None:
                    xp_comp.xp = int(p['xp'])
                if p.get('level') is not None:
                    xp_comp.level = int(p['level'])
                # Reflejar de vuelta en metadatos por coherencia
                meta.setdefault('player', {})
                meta['player']['xp'] = int(xp_comp.xp)
                meta['player']['level'] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
            # Cerrar menú y dejarlo en modo pausa para próximas aperturas
            self.show_menu = False
            self.mode = "pause"
            logger.info("Partida cargada desde %s", path)
        except Exception as e:
            logger.error("Error al cargar partida desde lista: %s", e)

    # ---- Inline rename helpers ----
    def _begin_edit_save_name(self):
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        current = (entry.get('meta') or {}).get('name') or entry.get('label') or ''
        self._saves_editing_name = True
        self._saves_edit_name_text = str(current)
        self._saves_edit_caret = len(self._saves_edit_name_text)
        # Por defecto, no seleccionar todo; el doble click lo activará
        self._saves_select_all_edit = False
        # Activar repetición de teclas para edición fluida (incluye Backspace/Delete)
        try:
            # Guardar config previa (si está disponible) y activar repeat
            self._prev_key_repeat = pygame.key.get_repeat() if hasattr(pygame.key, 'get_repeat') else None
            pygame.key.set_repeat(350, 40)
        except Exception:
            pass

    def _set_caret_from_click(self, pos: tuple[int, int]):
        try:
            layout = getattr(self.renderer, 'last_saves_layout', None)
            if not layout:
                return
            name_rect = layout.get('details_name_rect')
            if not name_rect or not name_rect.collidepoint(pos):
                return
            # Calcular índice aproximado por anchura
            rel_x = pos[0] - name_rect.left - 4  # compensar padding del rect
            text = self._saves_edit_name_text
            # Buscar el mayor índice cuyo ancho <= rel_x
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

    def _commit_save_rename(self):
        if not self.save_entries:
            self._end_edit_save_name(cancel=True)
            return
        new_name = (self._saves_edit_name_text or '').strip()
        if not new_name:
            # No permitir vacío: cancelar
            self._end_edit_save_name(cancel=True)
            return
        entry = self.save_entries[self.load_selected]
        path = entry.get('path')
        try:
            data = self.game.world.repository.load_from_path(str(path))
        except Exception:
            data = {}
        meta = data.get('meta') or {}
        meta['name'] = new_name
        data['meta'] = meta
        try:
            repo = self.game.world.repository
            snapshot = WorldSnapshot(
                version=data.get('version', 1),
                player=data.get('player'),
                npcs=data.get('npcs', {}),
                levels=data.get('levels', {}),
                player_inventory=data.get('player_inventory'),
                npc_inventories=data.get('npc_inventories'),
                meta=data.get('meta'),
            )
            repo.save_to_path(str(path), snapshot)
            # Actualizar en memoria
            entry['label'] = new_name
            entry['meta'] = meta
            self._end_edit_save_name()
            # Recalcular layout fijo por posibles cambios de ancho
            self._compute_saves_fixed_layout(self.screen)
        except Exception as e:
            logger.warning("No se pudo guardar el nuevo nombre del guardado: %s", e)
            self._end_edit_save_name(cancel=True)

    def _end_edit_save_name(self, cancel: bool = False):
        """Sale del modo edición y restaura el key repeat global."""
        self._saves_editing_name = False
        self._saves_select_all_edit = False
        try:
            # Restaurar repetición previa si la teníamos registrada, o desactivar
            if self._prev_key_repeat and all(isinstance(x, int) for x in self._prev_key_repeat):
                delay, interval = self._prev_key_repeat
                pygame.key.set_repeat(delay, interval)
            else:
                pygame.key.set_repeat(0)
        except Exception:
            pass

    # ---- Delete save helpers ----
    def _confirm_delete_selected_save(self):
        """Borra el archivo del guardado seleccionado tras confirmar, refresca la lista y cierra el modal."""
        if not self.save_entries:
            self._saves_show_confirm_delete = False
            return
        idx = self.load_selected
        path = self.save_entries[idx].get('path')
        try:
            if path:
                p = Path(path)
                if p.exists():
                    p.unlink()
        except Exception as e:
            logger.warning("No se pudo borrar el guardado %s: %s", path, e)
        # Refrescar lista y ajustar selección
        self._refresh_save_list()
        if not self.save_entries:
            # Sin partidas: volver al menú principal para ofrecer 'Nueva Partida'
            self.load_selected = 0
            self._saves_show_confirm_delete = False
            self._saves_hover_confirm_yes = False
            self._saves_hover_confirm_cancel = False
            self._saves_hover_delete_button = False
            # Reset de scroll/hover de la lista
            self._saves_row_scroll_offset = 0
            self._saves_hovered_idx = None
            self._saves_hover_details_name = False
            self._saves_editing_name = False
            # Cambiar a modo 'start' (menú principal)
            try:
                self.set_mode("start")
            except Exception:
                self.mode = "start"
            return
        else:
            self.load_selected = min(self.load_selected, len(self.save_entries) - 1)
        # Cerrar modal y limpiar hovers
        self._saves_show_confirm_delete = False
        self._saves_hover_confirm_yes = False
        self._saves_hover_confirm_cancel = False
        self._saves_hover_delete_button = False
        # Recalcular layout por si cambió el contenido
        try:
            self._compute_saves_fixed_layout(self.screen)
        except Exception:
            pass
