from __future__ import annotations

import pygame
from roguelike_ui.ui_blocker import is_blocked
from roguelike_editors.buildings.tools.z_tool.z_tool_view import ZToolView
from roguelike_editors.buildings.tools.split_z_tool.split_tool_view import SplitToolView


class SpawnerEditorView:
    """View responsible for rendering Spawner Editor overlays.

    Keeps drawing concerns separate from input/event logic.
    """
    def __init__(self, controller):
        self.controller = controller
        # Small font for ID label (lazy)
        try:
            self._id_font = pygame.font.Font(None, 16)
        except Exception:
            self._id_font = None
        # Reuse Building Editor Z tool views for UI parity
        try:
            self._z_bottom_view = ZToolView(None, None, target="bottom")
            self._z_top_view = ZToolView(None, None, target="top")
        except Exception:
            self._z_bottom_view = None
            self._z_top_view = None
        # Split bar view (visual split ratio control)
        try:
            self._split_view = SplitToolView(None, None)
        except Exception:
            self._split_view = None

    def render(self, screen: pygame.Surface) -> None:
        c = self.controller
        if not c.model.visible:
            return
        # While hold-to-focus is active, hide all editor panels/overlays
        try:
            if getattr(c.model, 'hold_focus_active', False):
                return
        except Exception:
            pass
        # Reset last rects each frame
        try:
            self._last_title_rect = None
            self._last_toolbar_rect = None
            self._last_instance_toolbar_rect = None
            self._last_manager_rect = None
            self._last_instances_rect = None
            self._last_properties_rect = None
            self._last_selected_delete_rect = None
            self._last_selected_resize_rect = None
            self._last_selected_reset_rect = None
            self._last_z_bottom_minus_rect = None
            self._last_z_bottom_plus_rect = None
            self._last_z_top_minus_rect = None
            self._last_z_top_plus_rect = None
            self._last_split_handle_rect = None
        except Exception:
            pass
        # 1) Title bar (always renders with its own font)
        try:
            title_rect = c.title_controller.render(screen)
        except Exception:
            title_rect = None
        try:
            self._last_title_rect = title_rect
        except Exception:
            pass
        # 2) Spawner toolbar just below title
        tb_rect = None
        try:
            if hasattr(c, 'spawner_toolbar') and c.spawner_toolbar:
                if title_rect is not None:
                    anchor = (title_rect.left, title_rect.bottom + 8)
                else:
                    anchor = (20, 60)
                c.spawner_toolbar.render(screen, anchor=anchor)
                tb_rect = getattr(getattr(c.spawner_toolbar, 'view', None), 'last_rect', None)
        except Exception:
            pass
        try:
            self._last_toolbar_rect = tb_rect
        except Exception:
            pass
        # 2b) Instance Toolbar to the RIGHT of main toolbar when Instances tool is active
        inst_tb_rect = None
        try:
            # Visible is synced in controller based on active tool
            if hasattr(c, 'instance_toolbar') and getattr(getattr(c.instance_toolbar, 'model', None), 'visible', False):
                if tb_rect is not None:
                    anchor = (tb_rect.right + 8, tb_rect.top)
                else:
                    base_x = title_rect.left if title_rect else 20
                    anchor = (base_x, (title_rect.bottom + 8) if title_rect else 90)
                c.instance_toolbar.render(screen, anchor=anchor)
                inst_tb_rect = getattr(getattr(c.instance_toolbar, 'view', None), 'last_rect', None)
        except Exception:
            pass
        try:
            self._last_instance_toolbar_rect = inst_tb_rect
        except Exception:
            pass
        # 3) Spawner Manager (Templates list) to the RIGHT of toolbar/instance toolbar when active
        mgr_rect = None
        try:
            if hasattr(c, 'spawner_manager') and getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
                if inst_tb_rect is not None:
                    # Prefer right of Instance Toolbar if present to avoid overlap
                    anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
                elif tb_rect is not None:
                    # Right of main toolbar
                    anchor = (tb_rect.right + 8, tb_rect.top)
                else:
                    # Fallback: place below title if toolbar rect missing
                    base_x = title_rect.left if title_rect else 20
                    anchor = (base_x, (title_rect.bottom + 8) if title_rect else 90)
                mgr_rect = c.spawner_manager.render(screen, anchor=anchor)
        except Exception:
            pass
        try:
            self._last_manager_rect = mgr_rect
        except Exception:
            pass
        # 3b) Spawner Instances list (spawners_instances.json) when active, same placement
        inst_rect = None
        try:
            if hasattr(c, 'spawner_instances') and getattr(getattr(c.spawner_instances, 'model', None), 'visible', True):
                # Only render when not showing manager to avoid overlap
                if not getattr(getattr(c.spawner_manager, 'model', None), 'visible', False):
                    # Prefer placing to the right of the Instance Toolbar if present; else right of main toolbar
                    if inst_tb_rect is not None:
                        anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
                    elif tb_rect is not None:
                        anchor = (tb_rect.right + 8, tb_rect.top)
                    else:
                        base_x = title_rect.left if title_rect else 20
                        anchor = (base_x, (title_rect.bottom + 8) if title_rect else 90)
                    inst_rect = c.spawner_instances.render(screen, anchor=anchor)
        except Exception:
            pass
        try:
            self._last_instances_rect = inst_rect
        except Exception:
            pass
        # 3c) Instance Properties panel to the RIGHT of Instances list when a selection exists
        try:
            ip = getattr(c, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visible', False):
                if inst_rect is not None:
                    anchor = (inst_rect.right + 8, inst_rect.top)
                elif inst_tb_rect is not None:
                    anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
                elif tb_rect is not None:
                    anchor = (tb_rect.right + 8, tb_rect.top)
                else:
                    base_x = title_rect.left if title_rect else 20
                    anchor = (base_x + 420, (title_rect.bottom + 8) if title_rect else 90)
                props_rect = ip.render(screen, anchor=anchor)
                try:
                    self._last_properties_rect = props_rect
                except Exception:
                    pass
        except Exception:
            pass
        # 3d) Visual focus overlay when editing a Visuals Template cell: dim the world and re-render properties
        try:
            ip = getattr(c, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visible', False):
                if getattr(getattr(ip, 'model', None), 'visuals_editing_state', None) is not None:
                    overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                    overlay.fill((0, 0, 0, 140))
                    screen.blit(overlay, (0, 0))
                    # Re-render properties panel on top for clarity
                    # Use last known rect as anchor to avoid layout shift
                    last_rect = getattr(self, '_last_properties_rect', None)
                    if last_rect is not None:
                        ip.render(screen, anchor=(last_rect.left, last_rect.top))
                    else:
                        # Fallback to anchor calculation above
                        if inst_rect is not None:
                            anchor = (inst_rect.right + 8, inst_rect.top)
                        elif inst_tb_rect is not None:
                            anchor = (inst_tb_rect.right + 8, inst_tb_rect.top)
                        elif tb_rect is not None:
                            anchor = (tb_rect.right + 8, tb_rect.top)
                        else:
                            base_x = title_rect.left if title_rect else 20
                            anchor = (base_x + 420, (title_rect.bottom + 8) if title_rect else 90)
                        ip.render(screen, anchor=anchor)
        except Exception:
            pass
        # 4) Hint overlay (only if editor font is available); place below title/toolbar/manager
        try:
            if c.font:
                base_y = (title_rect.bottom + 6) if title_rect else 10
                if tb_rect is not None:
                    base_y = max(base_y, tb_rect.bottom + 6)
                if mgr_rect is not None:
                    base_y = max(base_y, mgr_rect.bottom + 6)
                if inst_rect is not None:
                    base_y = max(base_y, inst_rect.bottom + 6)
                text = c.font.render("Spawner Editor (RMB drag to move)", True, (0, 200, 255))
                screen.blit(text, (10, base_y))
        except Exception:
            pass

        # 5) Zone change confirmation overlay
        try:
            pending = getattr(c.model, 'pending_zone_confirm', None)
            if pending:
                # Full-screen translucent dark backdrop
                overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                overlay.fill((0, 0, 0, 160))
                screen.blit(overlay, (0, 0))

                # Compose message lines
                orig_zone = str(pending.get('orig_zone'))
                prop_zone = str(pending.get('proposed_zone'))
                lines = [
                    f"Move spawner to zone '{prop_zone}'?",
                    f"Original zone: '{orig_zone}'",
                    "Press Y/Enter to confirm, N/Esc to cancel",
                ]
                # Draw centered panel
                font = getattr(c, 'font', None)
                if not font:
                    return
                max_w = 0
                rendered = []
                for ln in lines:
                    surf = font.render(ln, True, (255, 255, 255))
                    rendered.append(surf)
                    max_w = max(max_w, surf.get_width())
                pad = 14
                line_h = rendered[0].get_height()
                total_h = line_h * len(rendered) + pad * 2
                total_w = max_w + pad * 2
                vw, vh = screen.get_size()
                rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
                pygame.draw.rect(screen, (20, 20, 20), rect)
                pygame.draw.rect(screen, (200, 200, 200), rect, 2)
                y = rect.top + pad
                for surf in rendered:
                    x = rect.left + (rect.width - surf.get_width()) // 2
                    screen.blit(surf, (x, y))
                    y += line_h
        except Exception:
            pass

        # 7) Visuals Picker overlay (uses Buildings Picker UI)
        try:
            ip = getattr(c, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
                cam = getattr(c, 'game', None)
                cam = getattr(cam, 'camera', None)
                if cam is not None:
                    # Anchor the picker just below the Spawner Instances panel if available
                    try:
                        inst_rect_anchor = getattr(self, '_last_instances_rect', None)
                        picker = ip.get_visuals_picker()
                        if picker is not None and inst_rect_anchor is not None:
                            picker.set_anchors(left_x=int(inst_rect_anchor.left), top_y=int(inst_rect_anchor.bottom + 6), reserved_bottom_h=0)
                    except Exception:
                        pass
                    ip.render_visuals_picker(screen, cam)
        except Exception:
            pass

        # 7b) Draw hover (cyan) and selection (yellow) outlines for spawner-linked buildings
        ip = getattr(c, 'instance_properties', None)
        cam = getattr(c, 'game', None)
        cam = getattr(cam, 'camera', None)
        if ip is not None and cam is not None:
                vmodel = getattr(ip, 'visuals', None)
                vmodel = getattr(vmodel, 'model', None)
                sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
                hov_bid = getattr(vmodel, 'hovered_building_id', None) if vmodel else None
                # Per-frame fallback hover detection (robust if events were skipped)
                try:
                    mx, my = pygame.mouse.get_pos()
                    ob_hover = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                    if ob_hover is not None and getattr(ob_hover, 'id', None) is not None:
                        hov_bid = int(getattr(ob_hover, 'id'))
                except Exception:
                    pass
                # Choose which building to render overlays for: selected has priority, else hovered
                target_bid = sel_bid if sel_bid is not None else hov_bid
                # Draw hover first (cyan), unless it is the same as selected
                if hov_bid is not None and hov_bid != sel_bid:
                    ob_h = None
                    try:
                        ob_h = ip.visuals._find_building_entity_by_id(int(hov_bid))
                        if ob_h is None:
                            ip.visuals._ensure_building_loaded(int(hov_bid))
                            ob_h = ip.visuals._find_building_entity_by_id(int(hov_bid))
                    except Exception:
                        ob_h = None
                    if ob_h is not None:
                        try:
                            img = getattr(ob_h, 'image', getattr(getattr(ob_h, 'model', ob_h), 'image', None))
                            x = getattr(ob_h, 'x', getattr(getattr(ob_h, 'model', ob_h), 'x', None))
                            y = getattr(ob_h, 'y', getattr(getattr(ob_h, 'model', ob_h), 'y', None))
                            if img is not None and x is not None and y is not None:
                                sx, sy = cam.apply((x, y))
                                sw, sh = cam.scale(img.get_size())
                                rect = pygame.Rect(int(sx), int(sy), int(sw), int(sh))
                                pygame.draw.rect(screen, (0, 255, 255), rect, 2)
                        except Exception:
                            pass
                # Draw selection (yellow) on top
                if sel_bid is not None:
                    ob = None
                    try:
                        ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
                        if ob is None:
                            ip.visuals._ensure_building_loaded(int(sel_bid))
                            ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
                    except Exception:
                        ob = None
                    if ob is not None:                        
                        img = getattr(ob, 'image', getattr(getattr(ob, 'model', ob), 'image', None))
                        x = getattr(ob, 'x', getattr(getattr(ob, 'model', ob), 'x', None))
                        y = getattr(ob, 'y', getattr(getattr(ob, 'model', ob), 'y', None))
                        if img is not None and x is not None and y is not None:
                            sx, sy = cam.apply((x, y))
                            sw, sh = cam.scale(img.get_size())
                            rect = pygame.Rect(int(sx), int(sy), int(sw), int(sh))
                            pygame.draw.rect(screen, (255, 215, 0), rect, 5)
                            # ID label like Building Editor
                            try:
                                if self._id_font is not None:
                                    label = f"ID {int(sel_bid)}"
                                    text_surf = self._id_font.render(label, True, (255, 255, 255))
                                    shadow_surf = self._id_font.render(label, True, (0, 0, 0))
                                    lx = rect.left
                                    ly = rect.top - text_surf.get_height() - 2
                                    if ly < 0:
                                        ly = rect.top + 2
                                    screen.blit(shadow_surf, (lx + 1, ly + 1))
                                    screen.blit(text_surf, (lx, ly))
                            except Exception:
                                pass
                            # Draw Delete (red), Reset (white), and Resize (blue) handles like Building Editor
                            try:
                                mouse_pos = pygame.mouse.get_pos()
                                blocked = bool(is_blocked(*mouse_pos))
                            except Exception:
                                mouse_pos = (0, 0)
                                blocked = False
                            # Dynamic handle size ~10% of width (min 15, max 65)
                            handle_size = max(15, min(65, int(sw * 0.10)))
                            # Delete handle (leftmost of the trio)
                            del_rect = pygame.Rect(rect.left + sw - 3 * handle_size, rect.top, handle_size, handle_size)
                            try:
                                self._last_selected_delete_rect = del_rect.copy()
                            except Exception:
                                self._last_selected_delete_rect = del_rect
                            is_hover_del = (not blocked) and del_rect.collidepoint(mouse_pos)
                            pygame.draw.rect(screen, (220, 40, 40), del_rect)
                            pygame.draw.rect(screen, (0, 0, 0), del_rect, 2)
                            if is_hover_del:
                                pygame.draw.rect(screen, (255, 255, 0), del_rect, 4)
                            pygame.draw.line(screen, (255, 255, 255), del_rect.topleft, del_rect.bottomright, 3)
                            pygame.draw.line(screen, (255, 255, 255), del_rect.topright, del_rect.bottomleft, 3)
                            # Reset handle (middle)
                            rst_rect = pygame.Rect(rect.left + sw - 2 * handle_size, rect.top, handle_size, handle_size)
                            try:
                                self._last_selected_reset_rect = rst_rect.copy()
                            except Exception:
                                self._last_selected_reset_rect = rst_rect
                            is_hover_rst = (not blocked) and rst_rect.collidepoint(mouse_pos)
                            pygame.draw.rect(screen, (255, 255, 255), rst_rect)
                            pygame.draw.rect(screen, (0, 0, 0), rst_rect, 2)
                            if is_hover_rst:
                                pygame.draw.rect(screen, (0, 255, 255), rst_rect, 4)
                            try:
                                dfont = pygame.font.SysFont("arial", int(handle_size * 0.6), bold=True)
                                ds = dfont.render('D', True, (0, 0, 0))
                                screen.blit(ds, ds.get_rect(center=rst_rect.center))
                            except Exception:
                                pass
                            # Resize handle (rightmost)
                            rz_rect = pygame.Rect(rect.left + sw - handle_size, rect.top, handle_size, handle_size)
                            try:
                                self._last_selected_resize_rect = rz_rect.copy()
                            except Exception:
                                self._last_selected_resize_rect = rz_rect
                            is_hover_rz = (not blocked) and rz_rect.collidepoint(mouse_pos)
                            pygame.draw.rect(screen, (80, 120, 255), rz_rect)
                            pygame.draw.rect(screen, (0, 0, 0), rz_rect, 2)
                            if is_hover_rz:
                                pygame.draw.rect(screen, (255, 0, 255), rz_rect, 4)
                            # Decorative ellipse + 'R'
                            try:
                                pygame.draw.ellipse(screen, (255, 255, 0), rz_rect, 5)
                                rfont = pygame.font.SysFont("arial", int(handle_size * 0.8), bold=True)
                                rs = rfont.render('R', True, (255, 255, 0))
                                screen.blit(rs, rs.get_rect(center=rz_rect.center))
                            except Exception:
                                pass
                            # Z toolbars (bottom/top) using Building Editor UI for parity
                            try:
                                if self._z_bottom_view is not None:
                                    zb = self._z_bottom_view.render(screen, ob, cam)
                                    if isinstance(zb, dict):
                                        px, py = zb.get('panel_pos', (0, 0))
                                        m = zb.get('minus_rect')
                                        p = zb.get('plus_rect')
                                        if m is not None:
                                            self._last_z_bottom_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                                        if p is not None:
                                            self._last_z_bottom_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                                if self._z_top_view is not None:
                                    zt = self._z_top_view.render(screen, ob, cam)
                                    if isinstance(zt, dict):
                                        px, py = zt.get('panel_pos', (0, 0))
                                        m = zt.get('minus_rect')
                                        p = zt.get('plus_rect')
                                        if m is not None:
                                            self._last_z_top_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                                        if p is not None:
                                            self._last_z_top_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                            except Exception:
                                pass
                            # Split ratio bar and handle
                            try:
                                if self._split_view is not None:
                                    sret = self._split_view.render(screen, ob, cam)
                                    if isinstance(sret, dict):
                                        self._last_split_handle_rect = sret.get('handle_rect')
                            except Exception:
                                pass
                # If nothing is selected, still render Z panels and split bar for hovered target
                if sel_bid is None and target_bid is not None:
                    ob_t = None
                    try:
                        ob_t = ip.visuals._find_building_entity_by_id(int(target_bid))
                        if ob_t is None:
                            ip.visuals._ensure_building_loaded(int(target_bid))
                            ob_t = ip.visuals._find_building_entity_by_id(int(target_bid))
                    except Exception:
                        ob_t = None
                    if ob_t is not None:
                        try:
                            # Draw Z panels
                            if self._z_bottom_view is not None:
                                zb = self._z_bottom_view.render(screen, ob_t, cam)
                                if isinstance(zb, dict):
                                    px, py = zb.get('panel_pos', (0, 0))
                                    m = zb.get('minus_rect')
                                    p = zb.get('plus_rect')
                                    if m is not None:
                                        self._last_z_bottom_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                                    if p is not None:
                                        self._last_z_bottom_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                            if self._z_top_view is not None:
                                zt = self._z_top_view.render(screen, ob_t, cam)
                                if isinstance(zt, dict):
                                    px, py = zt.get('panel_pos', (0, 0))
                                    m = zt.get('minus_rect')
                                    p = zt.get('plus_rect')
                                    if m is not None:
                                        self._last_z_top_minus_rect = pygame.Rect(px + m.x, py + m.y, m.w, m.h)
                                    if p is not None:
                                        self._last_z_top_plus_rect = pygame.Rect(px + p.x, py + p.y, p.w, p.h)
                            # Split bar
                            if self._split_view is not None:
                                sret = self._split_view.render(screen, ob_t, cam)
                                if isinstance(sret, dict):
                                    self._last_split_handle_rect = sret.get('handle_rect')
                        except Exception:
                            pass

        # 6) Delete instance confirmation overlay
        try:
            pending_del = getattr(c.model, 'pending_delete_confirm', None)
            if pending_del:
                # Full-screen translucent dark backdrop
                overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                overlay.fill((0, 0, 0, 160))
                screen.blit(overlay, (0, 0))

                tpl = str(pending_del.get('template_id'))
                zone = str(pending_del.get('zone'))
                lt = pending_del.get('local_tile') or (0, 0)
                lines = [
                    f"Delete spawner instance?",
                    f"Template: '{tpl}' | Zone: '{zone}' | Tile: ({lt[0]}, {lt[1]})",
                    "Press Y/Enter to confirm, N/Esc to cancel",
                ]
                font = getattr(c, 'font', None)
                if not font:
                    return
                max_w = 0
                rendered = []
                for ln in lines:
                    surf = font.render(ln, True, (255, 200, 200))
                    rendered.append(surf)
                    max_w = max(max_w, surf.get_width())
                pad = 14
                line_h = rendered[0].get_height()
                total_h = line_h * len(rendered) + pad * 2
                total_w = max_w + pad * 2
                vw, vh = screen.get_size()
                rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
                pygame.draw.rect(screen, (30, 0, 0), rect)
                pygame.draw.rect(screen, (220, 60, 60), rect, 2)
                y = rect.top + pad
                for surf in rendered:
                    x = rect.left + (rect.width - surf.get_width()) // 2
                    screen.blit(surf, (x, y))
                    y += line_h
        except Exception:
            pass
