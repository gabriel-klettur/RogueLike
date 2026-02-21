from __future__ import annotations

import logging
from typing import Optional, Any

import pygame

from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem


class ItemsEditorRenderer:
    """Encapsula el flujo de render del Items Editor y helpers de layout."""

    def __init__(self, controller: Any) -> None:
        self.c = controller
        # Snapshots para logs de cambios de layout
        self._last_inst_list_rect: Optional[pygame.Rect] = None
        self._last_inst_params_rect: Optional[pygame.Rect] = None
        self._last_reserved_h: Optional[int] = None

    def draw(self, screen: pygame.Surface) -> None:
        if not self.c.model.visible:
            return
        if getattr(self.c.model, 'holding_pos_focus', False):
            return
        title_rect = self._render_title_and_toolbar(screen)
        # Asegurar visibilidad del panel de instancias
        self.c.instances_controller.model.visible = True
        try:
            inst_list_rect, inst_params_rect = self.c.instances_controller.get_layout_rects()
        except Exception:
            inst_list_rect = inst_params_rect = None
        reserve_h = self._prepare_layout(screen, title_rect, inst_list_rect, inst_params_rect)
        # Flags visuales del picker
        try:
            setattr(self.c.picker_controller.view, '_spawn_mode_active', getattr(self.c.model, 'spawn_mode_active', False))
            setattr(self.c.picker_controller.view, '_spawn_item_id', getattr(self.c.model, 'spawn_item_id', None))
        except Exception:
            pass
        self.c.picker_controller.draw(screen)
        self.c.model.hovered_item_id = self.c.picker_controller.model.hovered_item_id
        self.c.properties_controller.update_context(self.c.model.items, self.c.model.selected_item_id, self.c.model.hovered_item_id)
        self.c.properties_controller.draw(screen, title_rect)
        # Panel de instancias
        list_rect, params_rect = inst_list_rect, inst_params_rect
        if (self._last_inst_list_rect != list_rect) or (self._last_inst_params_rect != params_rect) or (self._last_reserved_h != reserve_h):
            logging.getLogger(__name__).debug(
                f"[ItemsEditorRenderer.draw] instances visible={self.c.instances_controller.model.visible} list_rect={list_rect} params_rect={params_rect} reserved_h={reserve_h}"
            )
            self._last_inst_list_rect = list_rect.copy() if list_rect else None
            self._last_inst_params_rect = params_rect.copy() if params_rect else None
            self._last_reserved_h = reserve_h
        self.c.instances_controller.draw(screen)
        # Toolbars por encima
        try:
            self.c.items_toolbar_controller.render(screen)
            self.c.items_add_remove_controller.render(screen)
        except Exception:
            pass
        # Tutorial panel on top
        try:
            if getattr(self.c, 'tutorial_controller', None):
                self.c.tutorial_controller.render(screen)
        except Exception:
            pass
        # Hover estándar si el mundo no lo tiene
        try:
            if hasattr(self.c, 'game') and getattr(self.c.model, 'visible', False) and not getattr(self.c.model, 'holding_pos_focus', False):
                world_obj = getattr(self.c.game, 'ecs', None)
                world = getattr(world_obj, 'ecs_world', None)
                camera = getattr(self.c.game, 'camera', None)
                if world and camera:
                    systems_u = list(getattr(world, 'update_systems', []))
                    systems_r = list(getattr(world, 'render_systems', []))
                    has_world_hover = any(isinstance(s, DropHoverRenderSystem) for s in (systems_u + systems_r))
                    if not has_world_hover and hasattr(self.c, '_hover_renderer') and self.c._hover_renderer:
                        self.c._hover_renderer.update(world, screen, camera)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorRenderer.draw] hover render failed")
        # Borde rojo en delete_mode
        try:
            if getattr(self.c.model, 'delete_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                world, camera = self.c.drop_service.world_and_camera()
                if world and camera:
                    eid = self.c.drop_service.find_drop_entity_at(mx, my)
                    if eid is not None:
                        comps = world.components
                        pos2 = comps['Position'][eid]
                        sprite = comps['Sprite'][eid]
                        scale_comp = comps.get('Scale', {}).get(eid)
                        scale = scale_comp.scale if scale_comp else 1.0
                        w, h = sprite.image.get_size()
                        w = int(w * scale * camera.zoom)
                        h = int(h * scale * camera.zoom)
                        sx2, sy2 = camera.apply((pos2.x, pos2.y))
                        rect = pygame.Rect(sx2, sy2, w, h)
                        overlay = pygame.Surface(rect.size, pygame.SRCALPHA)
                        overlay.fill((255, 0, 0, 80))
                        screen.blit(overlay, rect.topleft)
                        pygame.draw.rect(screen, (255, 0, 0), rect, 2)
        except Exception:
            pass
        # Overlays de ayuda del cursor
        try:
            if getattr(self.c.model, 'spawn_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                if getattr(self.c.model, 'spawn_item_id', None) is None:
                    msg = "Haz clic sobre un ítem"
                else:
                    msg = "Selecciona dónde posicionar el ítem en el mapa o sobre tu inventario"
                try:
                    surf = self.c.font.render(msg, True, (255, 255, 0))
                except Exception:
                    f = pygame.font.SysFont(None, 18)
                    surf = f.render(msg, True, (255, 255, 0))
                screen.blit(surf, (mx + 10, my + 10))
            if getattr(self.c.model, 'delete_mode_active', False):
                mx, my = pygame.mouse.get_pos()
                msg = "Haz clic sobre el ítem del inventario, mapa o menú para poder eliminarlo"
                try:
                    surf = self.c.font.render(msg, True, (255, 0, 0))
                except Exception:
                    f = pygame.font.SysFont(None, 18)
                    surf = f.render(msg, True, (255, 0, 0))
                screen.blit(surf, (mx + 10, my + 10))
        except Exception:
            pass

    def _render_title_and_toolbar(self, screen: pygame.Surface) -> Optional[pygame.Rect]:
        title_rect: Optional[pygame.Rect] = None
        try:
            title_rect = self.c.title_controller.render(screen)
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorRenderer.draw] title render failed")
        try:
            if hasattr(self.c, 'items_toolbar_controller'):
                self.c.items_toolbar_controller.render(screen)
        except Exception:
            pass
        return title_rect

    def _prepare_layout(
        self,
        screen: pygame.Surface,
        title_rect: Optional[pygame.Rect],
        inst_list_rect: Optional[pygame.Rect],
        inst_params_rect: Optional[pygame.Rect],
    ) -> int:
        reserve_h = None
        margin = self.c.instances_controller.model.margin if hasattr(self.c.instances_controller, 'model') else 20
        if inst_list_rect:
            reserve_h = inst_list_rect.h + margin
        else:
            sw, sh = screen.get_size()
            reserve_h = (sh // 4) + margin
        setattr(self.c.picker_controller.view, '_reserved_bottom_h', reserve_h)
        if title_rect is not None:
            setattr(self.c.picker_controller.view, 'title_rect', title_rect)
            try:
                setattr(self.c.picker_controller.view, '_top_anchor_y', title_rect.bottom + 8)
            except Exception:
                pass
        try:
            if getattr(self.c.items_add_remove_model, 'visible', False):
                tbv = getattr(self.c, 'items_toolbar_view', None)
                arv = getattr(self.c, 'items_add_remove_view', None)
                if tbv is not None and arv is not None:
                    tb_widget = tbv.widget
                    tb_pos = getattr(tb_widget.panel, 'pos', None) or (tb_widget.x, tb_widget.y)
                    tb_panel_w = tb_widget.panel.surface.get_width()
                    ar_x = tb_pos[0] + tb_panel_w + 8
                    ar_panel_w = arv.widget.panel.surface.get_width()
                    left_anchor_x = ar_x + ar_panel_w
                    setattr(self.c.picker_controller.view, '_left_anchor_x', left_anchor_x)
                    try:
                        if getattr(self.c.properties_controller.model, 'show_add_system_selector', False) or \
                           getattr(self.c.items_add_remove_model, 'active_tool', None) == 'add_item_on_system':
                            setattr(self.c.properties_controller.view, '_left_anchor_x', left_anchor_x)
                            top_anchor_y = (title_rect.bottom + 8) if title_rect is not None else None
                            setattr(self.c.properties_controller.view, '_top_anchor_y', top_anchor_y)
                        else:
                            if hasattr(self.c.properties_controller.view, '_left_anchor_x'):
                                setattr(self.c.properties_controller.view, '_left_anchor_x', None)
                            if hasattr(self.c.properties_controller.view, '_top_anchor_y'):
                                setattr(self.c.properties_controller.view, '_top_anchor_y', None)
                    except Exception:
                        pass
            else:
                if hasattr(self.c.picker_controller.view, '_left_anchor_x'):
                    setattr(self.c.picker_controller.view, '_left_anchor_x', None)
                try:
                    if not getattr(self.c.properties_controller.model, 'show_add_system_selector', False):
                        if hasattr(self.c.properties_controller.view, '_left_anchor_x'):
                            setattr(self.c.properties_controller.view, '_left_anchor_x', None)
                        if hasattr(self.c.properties_controller.view, '_top_anchor_y'):
                            setattr(self.c.properties_controller.view, '_top_anchor_y', None)
                except Exception:
                    pass
        except Exception:
            logging.getLogger(__name__).exception("[ItemsEditorRenderer.draw] failed to compute picker left anchor")
        return reserve_h
