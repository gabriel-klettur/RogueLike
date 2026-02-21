from __future__ import annotations

import os
from typing import Any, Dict, List, Optional, Tuple

import pygame

from roguelike_game.ecs.systems.inventory.ui_assets import load_items_and_icons
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble


class VendorUISystem:
    """
    Renderiza un panel de comercio junto al chat cuando el target actual es un vendedor.

    Muestra por ítem: icono, nombre, cantidad (stock del NPC), precio compra, precio venta y botones
    para comprar/vender de a 1 unidad.
    """

    def __init__(self, perf_log: Any | None = None) -> None:
        self.perf_log = perf_log
        # Cargar modelos e iconos desde SQLite (vía ItemsLoader)
        items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items, self.icon_surfaces = load_items_and_icons(items_path)
        # Fuente
        self.font_small: Optional[pygame.font.Font] = None
        self.font: Optional[pygame.font.Font] = None
        # Índice perezoso de assets para fallback de iconos ausentes en DB
        self._assets_index: Optional[Dict[str, str]] = None

    # --- API ECS --------------------------------------------------------------
    def update(self, world: Any, screen: pygame.Surface, camera: Any) -> None:
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        target = getattr(state, 'chat_target_eid', None)
        if target is None:
            return
        # Validar que el target tenga inventario (vendedor)
        invs = getattr(world, 'components', {}).get('InventoryComponent', {}) or {}
        if target not in invs:
            return
        # Vincular el VTS al estado para que el manejador de eventos reutilice el mismo
        try:
            vts = self._get_vts(world)
            setattr(state, '_vendor_ui_vts', vts)
        except Exception:
            pass
        # Fonts y filas primero (para medir contenido)
        fnt, small = self._fonts()
        rows = self._collect_rows(world, target)
        # Estado de scroll
        try:
            scroll = int(getattr(state, 'vendor_ui_scroll', 0) or 0)
        except Exception:
            scroll = 0

        # Medir contenido para adaptar ancho
        pad = 8
        gap = 8
        sb_w = 10
        col_icon_w = 32  # un poco más grande para mejorar legibilidad
        # Nombre
        max_name_w = small.size("Item")[0]
        for _iid, name, _qty, _pb, _ps in rows:
            try:
                max_name_w = max(max_name_w, small.size(str(name))[0])
            except Exception:
                pass
        col_name_w = max(120, max_name_w + 16)
        # Cantidad
        max_qty_w = small.size("Cant.")[0]
        for _iid, _name, qty, _pb, _ps in rows:
            max_qty_w = max(max_qty_w, small.size(str(qty))[0])
        col_qty_w = max(56, max_qty_w + 12)
        # Precios
        def _p_text(v: Optional[float]) -> str:
            return "—" if v is None else str(int(v))
        max_buy_w = small.size("Compra")[0]
        max_sell_w = small.size("Venta")[0]
        for _iid, _name, _qty, pb, ps in rows:
            max_buy_w = max(max_buy_w, small.size(_p_text(pb))[0])
            max_sell_w = max(max_sell_w, small.size(_p_text(ps))[0])
        col_buy_w = max(70, max_buy_w + 14)
        col_sell_w = max(70, max_sell_w + 14)
        # Botones
        col_btn_w = 56  # por botón (+1 y -1)
        buttons_block_w = col_btn_w * 2 + 6

        # Ancho deseado en función del contenido
        desired_w = (
            pad * 2
            + col_icon_w + 6
            + col_name_w + 6
            + col_qty_w + 6
            + col_buy_w + 6
            + col_sell_w + 6
            + buttons_block_w + 6
            + sb_w
        )

        # Anclar el panel a la derecha del chat si hay espacio (cap con pantalla)
        sw, sh = screen.get_size()
        min_w, min_h = 320, 160
        max_w = sw - pad * 2
        desired_w = max(min_w, min(desired_w, max_w))
        chat_rect = getattr(state, 'chat_block_rect', None)
        if chat_rect and isinstance(chat_rect, pygame.Rect):
            rx = chat_rect.right + gap
            avail_w = sw - rx - pad
            if avail_w >= min_w:
                panel_x = rx
                panel_y = chat_rect.top
                panel_w = max(min_w, min(desired_w, avail_w))
                panel_h = max(min_h, min(chat_rect.height, sh - panel_y - pad))
            else:
                # Colocar arriba del chat (centrado horizontalmente dentro de pantalla)
                panel_w = min(desired_w, sw - pad * 2)
                panel_h = min(260, sh // 3)
                panel_x = max(pad, min(sw - pad - panel_w, chat_rect.centerx - panel_w // 2))
                panel_y = max(pad, chat_rect.top - (panel_h + gap))
        else:
            panel_w = desired_w
            panel_h = min(260, sh - pad * 2)
            panel_x = sw - pad - panel_w
            panel_y = sh - pad - panel_h
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)

        # Fondo y borde
        bg = pygame.Surface((panel_w, panel_h), flags=pygame.SRCALPHA)
        bg.fill((15, 15, 15, 220))
        screen.blit(bg, panel_rect.topleft)
        pygame.draw.rect(screen, (180, 180, 180), panel_rect, width=2)

        # Título
        title = small.render("Comercio", True, (255, 255, 0))
        screen.blit(title, (panel_rect.x + pad, panel_rect.y + pad))

        header_y = panel_rect.y + pad + title.get_height() + 6
        list_y = header_y + small.get_linesize() + 4
        # Altura y fila
        row_h = max(24, small.get_linesize() + 8)
        list_h = panel_rect.bottom - pad - list_y - row_h  # dejar espacio para footer ligero
        visible_rows = max(1, list_h // row_h)
        # Exponer conteos al estado para facilitar pruebas y debugging
        try:
            state.vendor_ui_total_rows = len(rows)
            state.vendor_ui_visible_rows = visible_rows
        except Exception:
            pass

        # Encabezado
        headers = [
            ("Item", col_name_w),
            ("Cant.", col_qty_w),
            ("Compra", col_buy_w),
            ("Venta", col_sell_w),
            ("", col_btn_w * 2),
        ]
        x = panel_rect.x + pad + col_icon_w + 6
        for label, w in headers:
            s = small.render(label, True, (200, 200, 200))
            screen.blit(s, (x, header_y))
            x += w + 6

        # Calcular paginación simple
        start = max(0, min(len(rows), len(rows) - visible_rows - scroll))
        end = min(len(rows), start + visible_rows)

        # Guardar rects por frame para clicks
        try:
            state.vendor_ui_btn_rects = []
            state.vendor_ui_panel_rect = panel_rect
        except Exception:
            pass

        # Clip interno para que nada se dibuje fuera del panel
        prev_clip = screen.get_clip()
        inner_clip = pygame.Rect(panel_rect.x + 1, panel_rect.y + 1, panel_rect.w - 2, panel_rect.h - 2)
        screen.set_clip(inner_clip)

        # Dibujar filas
        y = list_y
        for i in range(start, end):
            item_id, name, qty, p_buy, p_sell = rows[i]
            # Icono
            icon = self._get_icon(item_id)
            if icon:
                img = pygame.transform.smoothscale(icon, (col_icon_w, col_icon_w))
                screen.blit(img, (panel_rect.x + pad, y + (row_h - col_icon_w) // 2))
            # Nombre
            name_s = small.render(name, True, (230, 230, 230))
            screen.blit(name_s, (panel_rect.x + pad + col_icon_w + 6, y + 3))
            # Cantidad
            qty_s = small.render(str(qty), True, (200, 220, 200))
            qty_x = panel_rect.x + pad + col_icon_w + 6 + col_name_w + 6
            screen.blit(qty_s, (qty_x, y + 3))
            # Precios
            def fmt_price(v: Optional[float]) -> str:
                return "—" if v is None else str(int(v))
            buy_s = small.render(fmt_price(p_buy), True, (220, 220, 180))
            sell_s = small.render(fmt_price(p_sell), True, (220, 220, 180))
            buy_x = qty_x + col_qty_w + 6
            sell_x = buy_x + col_buy_w + 6
            screen.blit(buy_s, (buy_x, y + 3))
            screen.blit(sell_s, (sell_x, y + 3))
            # Botones
            btn_h = row_h - 6
            btn_y = y + 3
            btn_buy_rect = pygame.Rect(sell_x + col_sell_w + 6, btn_y, col_btn_w, btn_h)
            btn_sell_rect = pygame.Rect(btn_buy_rect.right + 6, btn_y, col_btn_w, btn_h)
            self._draw_button(screen, btn_buy_rect, small, "+1", enabled=p_buy is not None and qty > 0)
            self._draw_button(screen, btn_sell_rect, small, "-1", enabled=p_sell is not None)
            try:
                state.vendor_ui_btn_rects.append(
                    {
                        'item_id': item_id,
                        'buy': btn_buy_rect,
                        'sell': btn_sell_rect,
                    }
                )
            except Exception:
                pass
            y += row_h

        # Scrollbar simple (si hay más filas que visibles)
        total = len(rows)
        # Clamp del scroll para evitar que el thumb salga del track
        max_scroll = max(0, total - visible_rows)
        scroll = max(0, min(int(scroll), max_scroll))
        try:
            state.vendor_ui_scroll = scroll
        except Exception:
            pass
        if total > visible_rows:
            # Asegurar que el track quede completamente dentro del panel
            inner_right = panel_rect.right - 2  # compensar el borde
            track_x = inner_right - pad - sb_w
            # Altura disponible exacta para lista
            track_h = max(0, panel_rect.bottom - pad - list_y)
            sb_rect = pygame.Rect(int(track_x), int(list_y), int(sb_w), int(track_h))
            pygame.draw.rect(screen, (30, 30, 30), sb_rect)
            thumb_h = max(18, int(sb_rect.h * (visible_rows / float(total))))
            pos_frac = 1.0 - (scroll / float(max_scroll)) if max_scroll > 0 else 1.0
            span = max(0, sb_rect.h - thumb_h)
            thumb_y = sb_rect.y + int(span * pos_frac)
            thumb_rect = pygame.Rect(sb_rect.x + 1, thumb_y, sb_w - 2, thumb_h)
            pygame.draw.rect(screen, (180, 180, 180), thumb_rect, border_radius=3)
            try:
                state.vendor_ui_scrollbar_rect = sb_rect
                state.vendor_ui_scrollbar_thumb_rect = thumb_rect
                state.vendor_ui_visible_rows = visible_rows
                state.vendor_ui_total_rows = total
            except Exception:
                pass
        else:
            try:
                state.vendor_ui_scrollbar_rect = None
                state.vendor_ui_scrollbar_thumb_rect = None
                state.vendor_ui_visible_rows = total
                state.vendor_ui_total_rows = total
            except Exception:
                pass

        # Restaurar clip
        screen.set_clip(prev_clip)

    # --- Helpers --------------------------------------------------------------
    def _fonts(self) -> Tuple[pygame.font.Font, pygame.font.Font]:
        if self.font is None:
            self.font = pygame.font.SysFont("Consolas", 16)
        if self.font_small is None:
            self.font_small = pygame.font.SysFont("Consolas", 14)
        return self.font, self.font_small

    def _get_vts(self, world: Any):
        for s in getattr(world, 'update_systems', []):
            if type(s).__name__ == 'VendorTradeSystem':
                return s
        from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem
        inst = VendorTradeSystem()
        world.update_systems.append(inst)
        return inst

    def _collect_rows(self, world: Any, vendor_eid: int) -> List[Tuple[str, str, int, Optional[float], Optional[float]]]:
        rows: List[Tuple[str, str, int, Optional[float], Optional[float]]] = []
        invs = getattr(world, 'components', {}).get('InventoryComponent', {}) or {}
        inv = invs.get(vendor_eid)
        if not inv or not hasattr(inv, 'slots'):
            return rows
        counts: Dict[str, int] = {}
        for st in getattr(inv, 'slots', []) or []:
            if not st:
                continue
            iid = str(getattr(st, 'item_id', '')).lower()
            if not iid or iid == 'gold':
                continue
            counts[iid] = counts.get(iid, 0) + int(getattr(st, 'quantity', 0) or 0)
        if not counts:
            return rows
        vts = self._get_vts(world)
        # Construir filas con nombres y precios
        for iid, qty in sorted(counts.items()):
            try:
                price_buy = vts._get_price(world, vendor_eid, iid, op='buy')
                if price_buy is None:
                    # No se ofrece en venta
                    continue
                price_sell = vts._get_price(world, vendor_eid, iid, op='sell')
            except Exception:
                price_buy = None
                price_sell = None
            # Nombre del ítem
            name = iid
            try:
                model = self.items.get(iid)
                if model and getattr(model, 'name', None):
                    nm = str(getattr(model, 'name'))
                    if nm and not nm.lower().endswith('.png'):
                        name = nm
                    else:
                        name = iid.replace('_', ' ').title()
                else:
                    name = iid.replace('_', ' ').title()
            except Exception:
                name = iid.replace('_', ' ').title()
            rows.append((iid, name, int(qty), price_buy, price_sell))
        return rows

    # --- Fallback de iconos ---------------------------------------------------
    def _ensure_assets_index(self) -> None:
        if isinstance(self._assets_index, dict):
            return
        idx: Dict[str, str] = {}
        try:
            root = os.path.join(os.getcwd(), 'assets')
            for r, _d, files in os.walk(root):
                for fn in files:
                    if fn.lower().endswith('.png'):
                        idx.setdefault(fn.lower(), os.path.join(r, fn))
        except Exception:
            idx = {}
        self._assets_index = idx

    def _get_icon(self, item_id: str) -> Optional[pygame.Surface]:
        # 1) Catálogo precargado
        icon = self.icon_surfaces.get(item_id)
        if icon is not None:
            return icon
        # 2) Fallback por nombre de archivo <item_id>.png en assets
        try:
            self._ensure_assets_index()
            idx = self._assets_index or {}
            candidate = idx.get(f"{str(item_id).lower()}.png")
            if candidate and os.path.exists(candidate):
                surf = pygame.image.load(candidate).convert_alpha()
                # Cachear para siguientes frames
                self.icon_surfaces[item_id] = surf
                return surf
        except Exception:
            pass
        # Cachear None para evitar buscar cada frame
        self.icon_surfaces[item_id] = None
        return None

    def _draw_button(self, screen: pygame.Surface, rect: pygame.Rect, font: pygame.font.Font, label: str, *, enabled: bool) -> None:
        bg = (60, 60, 60) if enabled else (40, 40, 40)
        pygame.draw.rect(screen, bg, rect, border_radius=3)
        pygame.draw.rect(screen, (160, 160, 160), rect, 1, border_radius=3)
        txt = font.render(label, True, (230, 230, 230) if enabled else (120, 120, 120))
        screen.blit(txt, txt.get_rect(center=rect.center))


# ===== Manejador de eventos del panel de vendor ===============================

def handle_vendor_ui_events(world: Any, events: List[pygame.event.Event]) -> None:
    state = getattr(world, 'state', None)
    if not state or not getattr(state, 'chat_open', False):
        return
    target = getattr(state, 'chat_target_eid', None)
    if target is None:
        return
    # Debe haber inventario para ser vendedor
    invs = getattr(world, 'components', {}).get('InventoryComponent', {}) or {}
    if target not in invs:
        return
    # Panel actual
    panel_rect = getattr(state, 'vendor_ui_panel_rect', None)
    if not isinstance(panel_rect, pygame.Rect):
        return
    # Scroll
    try:
        scroll = int(getattr(state, 'vendor_ui_scroll', 0) or 0)
    except Exception:
        scroll = 0
    sb_rect = getattr(state, 'vendor_ui_scrollbar_rect', None)
    thumb_rect = getattr(state, 'vendor_ui_scrollbar_thumb_rect', None)
    dragging = bool(getattr(state, 'vendor_ui_dragging_thumb', False))

    for ev in events:
        if ev.type == pygame.MOUSEWHEEL:
            mx, my = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mx, my):
                step = 1
                total = int(getattr(state, 'vendor_ui_total_rows', 0) or 0)
                vis = int(getattr(state, 'vendor_ui_visible_rows', 1) or 1)
                max_scroll = max(0, total - vis)
                new_scroll = int(scroll) + (step if ev.y > 0 else -step)
                state.vendor_ui_scroll = max(0, min(new_scroll, max_scroll))
        elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mx, my = ev.pos
            if sb_rect and thumb_rect and sb_rect.collidepoint(mx, my):
                if thumb_rect.collidepoint(mx, my):
                    state.vendor_ui_dragging_thumb = True
                    state.vendor_ui_drag_thumb_off = my - thumb_rect.y
                else:
                    thumb_h = thumb_rect.h if thumb_rect else 30
                    rel = my - sb_rect.y - thumb_h // 2
                    rel = max(0, min(rel, sb_rect.h - thumb_h))
                    total = int(getattr(state, 'vendor_ui_total_rows', 0) or 0)
                    vis = int(getattr(state, 'vendor_ui_visible_rows', 1) or 1)
                    max_scroll = max(0, total - vis)
                    pos_frac = rel / float(max(1, sb_rect.h - thumb_h))
                    state.vendor_ui_scroll = max(0, min(int(round(max_scroll * (1.0 - pos_frac))), max_scroll))
            else:
                # Clicks en botones
                btn_rows = list(getattr(state, 'vendor_ui_btn_rects', []) or [])
                for entry in btn_rows:
                    iid = entry.get('item_id')
                    buy_rect = entry.get('buy')
                    sell_rect = entry.get('sell')
                    if isinstance(buy_rect, pygame.Rect) and buy_rect.collidepoint(mx, my):
                        _perform_buy(world, target, str(iid or ''), 1)
                        break
                    if isinstance(sell_rect, pygame.Rect) and sell_rect.collidepoint(mx, my):
                        _perform_sell(world, target, str(iid or ''), 1)
                        break
        elif ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
            state.vendor_ui_dragging_thumb = False
        elif ev.type == pygame.MOUSEMOTION and dragging:
            mx, my = ev.pos
            off = int(getattr(state, 'vendor_ui_drag_thumb_off', 0) or 0)
            if sb_rect and thumb_rect:
                thumb_h = thumb_rect.h
                rel = my - sb_rect.y - off
                rel = max(0, min(rel, sb_rect.h - thumb_h))
                total = int(getattr(state, 'vendor_ui_total_rows', 0) or 0)
                vis = int(getattr(state, 'vendor_ui_visible_rows', 1) or 1)
                max_scroll = max(0, total - vis)
                pos_frac = rel / float(max(1, sb_rect.h - thumb_h))
                state.vendor_ui_scroll = max(0, min(int(round(max_scroll * (1.0 - pos_frac))), max_scroll))


def _get_vts(world: Any):
    # Preferir instancia previamente vinculada por la UI (permite stubs en tests)
    try:
        st = getattr(world, 'state', None)
        if st is not None:
            vts = getattr(st, '_vendor_ui_vts', None)
            if vts is not None:
                return vts
    except Exception:
        pass
    for s in getattr(world, 'update_systems', []):
        if type(s).__name__ == 'VendorTradeSystem':
            return s
    from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem
    inst = VendorTradeSystem()
    world.update_systems.append(inst)
    return inst


def _perform_buy(world: Any, vendor_eid: int, item_id: str, qty: int) -> None:
    vts = _get_vts(world)
    try:
        text = vts.buy(world, vendor_eid, item_id, qty)
        state = getattr(world, 'state', None)
        if state:
            state.chat_add_message('NPC', text)
        try:
            push_bubble(world, vendor_eid, text, color=(255, 235, 180), ttl_ms=3000)
        except Exception:
            pass
    except Exception as e:
        try:
            state = getattr(world, 'state', None)
            if state:
                state.chat_add_message('NPC', f"No pude completar la compra: {e}")
        except Exception:
            pass


def _perform_sell(world: Any, vendor_eid: int, item_id: str, qty: int) -> None:
    vts = _get_vts(world)
    try:
        text = vts.sell(world, vendor_eid, item_id, qty)
        state = getattr(world, 'state', None)
        if state:
            state.chat_add_message('NPC', text)
        try:
            push_bubble(world, vendor_eid, text, color=(255, 235, 180), ttl_ms=3000)
        except Exception:
            pass
    except Exception as e:
        try:
            state = getattr(world, 'state', None)
            if state:
                state.chat_add_message('NPC', f"No pude completar la venta: {e}")
        except Exception:
            pass
