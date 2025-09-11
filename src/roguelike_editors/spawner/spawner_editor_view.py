from __future__ import annotations

import pygame


class SpawnerEditorView:
    """View responsible for rendering Spawner Editor overlays.

    Keeps drawing concerns separate from input/event logic.
    """
    def __init__(self, controller):
        self.controller = controller

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
