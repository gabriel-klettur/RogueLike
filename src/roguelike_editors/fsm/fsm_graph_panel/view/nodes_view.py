from __future__ import annotations
from typing import Any, Callable


def draw_nodes(model: Any, surf: Any, W: Callable[[tuple[float, float]], tuple[int, int]], zoom: float, view: Any) -> None:
    try:
        import pygame  # type: ignore
    except Exception:
        return None
    try:
        base_font_size = 20
        base_color = (235, 235, 235)
        font = pygame.font.SysFont(None, base_font_size)
        for n in getattr(model, 'nodes', []):
            nx = int(n.get('x', 0)); ny = int(n.get('y', 0))
            nw = int(n.get('w', 120)); nh = int(n.get('h', 60))
            tl = W((nx, ny))
            rect = pygame.Rect(tl[0], tl[1], int(nw*zoom), int(nh*zoom))
            # body
            pygame.draw.rect(surf, (40, 44, 52), rect, 0, border_radius=6)
            # border (selected/hover/special/terminal/initial highlighted)
            is_hover_node = (n.get('id') == getattr(model, 'hover_node_id', None))
            if n.get('id') == getattr(model, 'selected_node_id', None):
                color = (255, 210, 90)
                border_w = 3
            elif is_hover_node:
                color = (255, 230, 120)
                border_w = 3
            else:
                # Special states styling (damage/alert/interrupt/external)
                spec = n.get('special')
                spec_l = spec.lower() if isinstance(spec, str) else None
                nid = n.get('id')
                ncls = n.get('class')
                is_damage = (spec_l == 'damage') or (nid == 'Damage') or (ncls == 'DamageState')
                is_alert = (spec_l == 'alert') or (nid == 'AlertChase') or (ncls == 'AlertChaseState')
                is_interrupt = (spec_l == 'interrupt') or (spec_l == 'external') or (n.get('external_entry') is True)
                if is_damage:
                    # Damage: purple highlight
                    color = (160, 80, 200)
                    border_w = 3
                elif is_alert:
                    # Alert-chase or alert-like: magenta/pink highlight
                    color = (220, 100, 180)
                    border_w = 3
                elif is_interrupt:
                    # External-entry/interruptible: cyan highlight
                    color = (90, 200, 220)
                    border_w = 3
                elif n.get('terminal'):
                    # Terminal/end nodes: red highlight
                    color = (220, 80, 80)
                    border_w = 3
                elif n.get('initial'):
                    # Initial/start nodes: green highlight
                    color = (80, 200, 120)
                    border_w = 3
                else:
                    color = (90, 90, 100)
                    border_w = 2
            pygame.draw.rect(surf, color, rect, border_w, border_radius=6)
            # label (hover highlight)
            is_hover = (n.get('id') == getattr(model, 'hover_node_id', None))
            label = str(n.get('label', n.get('id', '?')))
            node_font = pygame.font.SysFont(None, (base_font_size + 2) if is_hover else base_font_size)
            editing_this = (getattr(model, 'editing_node_id', None) == n.get('id'))
            text_for_rect = str(getattr(model, 'editing_text', label) if editing_this else label)
            txt = node_font.render(text_for_rect, True, (255, 230, 120) if is_hover else base_color)
            tr = txt.get_rect(center=(rect.centerx, rect.centery))
            # Do not draw the node label if currently editing this node; still record rect
            if not editing_this:
                surf.blit(txt, tr)
            try:
                view.node_label_rects[n.get('id')] = tr.copy()
            except Exception:
                pass
            # Per-node lint badges (non-intrusive) near top-right of node rect
            try:
                nid = n.get('id')
                items = (getattr(model, 'node_lint_by_id', {}) or {}).get(nid) or []
                if items:
                    errs = [it for it in items if it.get('severity') == 'error']
                    warns = [it for it in items if it.get('severity') == 'warning']
                    if errs or warns:
                        # Helper to draw a small badge with count
                        def _badge(text: str, color_bg: tuple[int,int,int]):
                            f = pygame.font.SysFont(None, 14)
                            t = f.render(text, True, (255, 255, 255))
                            pad_x, pad_y = 4, 2
                            bw, bh = t.get_width() + pad_x * 2, t.get_height() + pad_y * 2
                            s = pygame.Surface((bw, bh), pygame.SRCALPHA)
                            s.fill((*color_bg, 230))
                            pygame.draw.rect(s, (255, 255, 255), s.get_rect(), 1, border_radius=6)
                            s.blit(t, (pad_x, pad_y))
                            return s
                        # Place error (red) then warning (amber) from top-right inward
                        cx = rect.right - 6
                        cy = rect.top + 6
                        rects_map: dict[str, any] = {}
                        if errs:
                            b = _badge(str(len(errs)), (200, 60, 60))
                            br = b.get_rect(); br.top = cy; br.right = cx
                            surf.blit(b, br)
                            rects_map['error'] = br
                            cx = br.left - 4
                        if warns:
                            b = _badge(str(len(warns)), (220, 160, 60))
                            br = b.get_rect(); br.top = cy; br.right = cx
                            surf.blit(b, br)
                            rects_map['warning'] = br
                        # Save for tooltip detection
                        try:
                            view.node_badge_rects[nid] = rects_map
                        except Exception:
                            pass
            except Exception:
                pass
    except Exception:
        pass
