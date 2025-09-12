import pygame
import json
import os
from roguelike_ui.ui_blocker import register_blocker
from .chat_input_controller import ChatInputController
from roguelike_engine.chat.service.memory_store import MemoryStore
from pathlib import Path

import logging
logger = logging.getLogger(__name__)

class ChatUISystem:
    """
    Sistema de renderizado de la UI de chat.

    - Dibuja un panel en pantalla con historial y un campo de texto.
    - Registra un rectángulo bloqueador de inputs (ui_blocker) para evitar gameplay debajo.
    - Asegura que el controlador de entrada (ChatInputController) esté listo.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._font = None
        self._small = None

    def _get_fonts(self):
        if self._font is None:
            self._font = pygame.font.SysFont("Consolas", 16)
        if self._small is None:
            self._small = pygame.font.SysFont("Consolas", 14)
        return self._font, self._small

    def update(self, world, screen, camera):
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        font, small = self._get_fonts()
        sw, sh = screen.get_size()
        # Panel en parte baja de la pantalla (persistente y redimensionable)
        pad = 8
        min_w, min_h = 320, 160
        # Cargar dimensiones persistentes
        panel_w = int(getattr(state, 'chat_panel_w', 0) or 0)
        panel_h = int(getattr(state, 'chat_panel_h', 0) or 0)
        if panel_w <= 0 or panel_h <= 0:
            panel_w = min(520, sw - pad * 2)
            panel_h = min(220, sh - pad * 2)
        panel_w = max(min_w, min(panel_w, sw - pad * 2))
        panel_h = max(min_h, min(panel_h, sh - pad * 2))
        panel_x = pad
        panel_y = sh - panel_h - pad
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Persistir dimensiones
        try:
            state.chat_panel_w = panel_w
            state.chat_panel_h = panel_h
        except Exception:
            pass
        # Fondo semitransparente
        bg = pygame.Surface((panel_w, panel_h), flags=pygame.SRCALPHA)
        bg.fill((10, 10, 10, 200))
        screen.blit(bg, (panel_x, panel_y))
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, width=2)
        # Registrar bloqueo de UI
        try:
            register_blocker(panel_rect)
            state.chat_block_rect = panel_rect
        except Exception:
            pass
        # Título (dinámico: mostrar nombre del vendor si aplica)
        title_text = "Chat"
        try:
            target_eid = getattr(state, 'chat_target_eid', None)
            if target_eid is not None:
                chat_comp = world.components.get('ChatComponent', {}).get(target_eid)
                role = getattr(chat_comp, 'role', 'generic') if chat_comp else 'generic'
                if role == 'vendor':
                    ident = world.components.get('Identity', {}).get(target_eid)
                    name = getattr(ident, 'name', None)
                    if name:
                        title_text = str(name)
        except Exception:
            pass
        title = small.render(title_text, True, (255,255,0))
        title_pos = (panel_x + pad, panel_y + pad)
        screen.blit(title, title_pos)
        # Estado online/offline al lado del título
        def _estimate_online_status_local() -> bool:
            try:
                root = Path(__file__).resolve().parents[4]
            except Exception:
                root = Path('.')
            cfg_path = root / 'data' / 'config' / 'chat.json'
            prov = 'dummy'
            try:
                if cfg_path.exists():
                    with cfg_path.open('r', encoding='utf-8') as f:
                        obj = json.load(f)
                        prov = str(obj.get('provider', 'dummy')).lower()
            except Exception:
                prov = 'dummy'
            if prov == 'dummy':
                return False
            if not os.getenv('OPENAI_API_KEY'):
                return False
            return True
        online_flag = getattr(state, 'chat_llm_online', None)
        status_rect = None
        if online_flag is not None:
            status_txt = '(online)' if online_flag else '(offline)'
            status_color = (120, 220, 120) if online_flag else (200, 120, 120)
            stat_surf = small.render(status_txt, True, status_color)
            stat_pos = (title_pos[0] + title.get_width() + 8, title_pos[1])
            # Dot de estado
            dot_r = 4
            dot_x = stat_pos[0] - (dot_r * 2)
            dot_y = stat_pos[1] + small.get_height() // 2
            pygame.draw.circle(screen, status_color, (dot_x, dot_y), dot_r)
            screen.blit(stat_surf, stat_pos)
            status_rect = stat_surf.get_rect(topleft=stat_pos)
        # Botón de cierre 'X' en esquina superior derecha
        btn_size = small.get_height() + 6
        btn_x = panel_x + panel_w - pad - btn_size
        btn_y = panel_y + pad
        close_rect = pygame.Rect(btn_x, btn_y, btn_size, btn_size)
        # Botones de esquina (min, close, resize + language)
        btn_size = 18
        # Botón Close [X]
        close_rect = pygame.Rect(panel_x + panel_w - pad - btn_size, panel_y + pad, btn_size, btn_size)
        state.chat_close_rect = close_rect
        pygame.draw.rect(screen, (60, 60, 60), close_rect)
        x_txt = font.render("X", True, (220, 220, 220))
        screen.blit(x_txt, x_txt.get_rect(center=close_rect.center))
        # Sincronizar preferencia de idioma desde memoria si hay target y no está en estado
        try:
            target_eid = getattr(state, 'chat_target_eid', None)
            if target_eid is not None:
                if not hasattr(state, 'chat_lang_preference') or not getattr(state, 'chat_lang_preference'):
                    try:
                        root = Path(__file__).resolve().parents[4]
                    except Exception:
                        root = Path('.')
                    ms = MemoryStore(root)
                    pref = ms.get_language(str(target_eid)) or 'es'
                    state.chat_lang_preference = pref
        except Exception:
            pass
        # Botón Language [Lang: ES/EN]
        cur_code = (getattr(state, 'chat_lang_preference', '') or '').lower()
        cur_tag = (cur_code.upper() if cur_code in {'es','en'} else '')
        btn_text = f"Lang:{cur_tag}" if cur_tag else "Lang"
        btn_surf = small.render(btn_text, True, (220,220,220))
        btn_w = max(btn_size + 10, btn_surf.get_width() + 10)
        lang_rect = pygame.Rect(close_rect.left - (btn_w + 6), panel_y + pad, btn_w, btn_size)
        state.chat_lang_rect = lang_rect
        pygame.draw.rect(screen, (60, 60, 60), lang_rect)
        screen.blit(btn_surf, btn_surf.get_rect(center=lang_rect.center))
        # Dropdown de idioma si está abierto
        try:
            dd_open = bool(getattr(state, 'chat_lang_dropdown_open', False))
        except Exception:
            dd_open = False
        state.chat_lang_dropdown_rects = []
        if dd_open:
            # Opciones del combo (solo es/en)
            options = [
                ("Español", 'es'),
                ("Inglés", 'en'),
            ]
            opt_text_w = max(small.size(lbl)[0] for lbl, _ in options) if options else 80
            opt_w = max(opt_text_w + 16, 110)
            opt_h = small.get_linesize() + 6
            # Dibujar debajo del botón lang
            for i, (label, code) in enumerate(options):
                rx = lang_rect.left
                ry = lang_rect.bottom + 4 + i * (opt_h + 2)
                rect = pygame.Rect(rx, ry, opt_w, opt_h)
                # Guardar para manejo de eventos
                state.chat_lang_dropdown_rects.append((rect, label, code))
                # Fondo
                pygame.draw.rect(screen, (40, 40, 40), rect)
                pygame.draw.rect(screen, (160, 160, 160), rect, 1)
                # Resaltar selección actual
                cur_code2 = getattr(state, 'chat_lang_preference', None)
                if cur_code2 == code:
                    hl = pygame.Surface((rect.width-2, rect.height-2), pygame.SRCALPHA)
                    hl.fill((120, 180, 120, 60))
                    screen.blit(hl, (rect.left+1, rect.top+1))
                # Texto
                lbl = small.render(label, True, (230,230,230))
                screen.blit(lbl, (rect.left + 6, rect.top + 3))

        # Tooltips
        try:
            mx, my = pygame.mouse.get_pos()
            tooltip_lines: list[tuple[str, tuple[int,int,int]]] = []
            # Tooltip para estado online/offline
            if status_rect and status_rect.collidepoint(mx, my):
                # Leer config para provider/model
                try:
                    root = Path(__file__).resolve().parents[4]
                except Exception:
                    root = Path('.')
                prov = 'dummy'
                model = ''
                try:
                    cfg_path = root / 'data' / 'config' / 'chat.json'
                    if cfg_path.exists():
                        with cfg_path.open('r', encoding='utf-8') as f:
                            obj = json.load(f)
                            prov = str(obj.get('provider', 'dummy'))
                            model = str(obj.get('model', ''))
                except Exception:
                    pass
                key_ok = bool(os.getenv('OPENAI_API_KEY'))
                confirmed = getattr(state, 'chat_llm_online', None)
                if confirmed is not None:
                    status_color = (120, 220, 120) if confirmed else (200, 120, 120)
                    tooltip_lines.append((f"Estado: {'online' if confirmed else 'offline'} (confirmado)", status_color))
                else:
                    tooltip_lines.append(("Estado: desconocido (aún sin respuesta)", (180,180,180)))
                tooltip_lines.append((f"Provider: {prov}", (220,220,220)))
                if model:
                    tooltip_lines.append((f"Modelo: {model}", (220,220,220)))
                tooltip_lines.append((f"API Key: {'OK' if key_ok else 'faltante'}", (180,180,180)))
            # Tooltip para botón Lang
            if state.chat_lang_rect and state.chat_lang_rect.collidepoint(mx, my):
                cur = (getattr(state, 'chat_lang_preference', '') or 'es').lower()
                tooltip_lines.append((f"Idioma actual: {'Español' if cur=='es' else 'Inglés'}", (220,220,220)))
                tooltip_lines.append(("Click para cambiar (ES/EN)", (180,180,180)))
            # Pintar tooltip si hay contenido
            if tooltip_lines:
                pad_tb, pad_lr = 4, 6
                max_w = 0
                h_total = pad_tb
                line_surfs = []
                for txt, col in tooltip_lines:
                    s = small.render(txt, True, col)
                    line_surfs.append(s)
                    max_w = max(max_w, s.get_width())
                    h_total += s.get_height()
                h_total += pad_tb
                tip_w = max_w + pad_lr * 2
                tip_h = h_total
                # Evitar que se salga de pantalla
                tip_x = mx + 14
                tip_y = my + 10
                sw, sh = screen.get_size()
                if tip_x + tip_w > sw - 6:
                    tip_x = sw - tip_w - 6
                if tip_y + tip_h > sh - 6:
                    tip_y = sh - tip_h - 6
                bg = pygame.Surface((tip_w, tip_h), pygame.SRCALPHA)
                bg.fill((20, 20, 20, 220))
                pygame.draw.rect(bg, (120,120,120), bg.get_rect(), 1)
                screen.blit(bg, (tip_x, tip_y))
                cy = tip_y + pad_tb
                for s in line_surfs:
                    screen.blit(s, (tip_x + pad_lr, cy))
                    cy += s.get_height()
        except Exception:
            pass

        # Handle de redimensionado (esquina superior derecha, a la izquierda del botón)
        rh_size = btn_size
        rh_rect = pygame.Rect(panel_x + panel_w - pad - rh_size, panel_y + panel_h - pad - rh_size, rh_size, rh_size)
        state.chat_resize_rect = rh_rect
        # Estilo hover del handle
        mx, my = pygame.mouse.get_pos()
        hovering_resize = rh_rect.collidepoint(mx, my)
        rh_bg = pygame.Surface((rh_size, rh_size), flags=pygame.SRCALPHA)
        rh_bg.fill((60, 60, 60, 220) if hovering_resize else (40, 40, 40, 160))
        screen.blit(rh_bg, rh_rect.topleft)
        pygame.draw.rect(screen, (180, 180, 255) if hovering_resize else (160, 160, 160), rh_rect, width=1)
        # Dibujar un ícono de resize (diagonal ↘↗)
        pygame.draw.line(screen, (200,200,255), (rh_rect.left+3, rh_rect.bottom-4), (rh_rect.right-4, rh_rect.top+3), 2)
        pygame.draw.line(screen, (200,200,255), (rh_rect.left+5, rh_rect.bottom-4), (rh_rect.right-4, rh_rect.top+5), 1)

        # Preparar controlador de input ANTES de calcular el área de mensajes,
        # para reservar altura variable si el input está envuelto en varias líneas.
        ctrl = getattr(world, '_chat_input_ctrl', None)
        if ctrl is None:
            ctrl = ChatInputController()
            setattr(world, '_chat_input_ctrl', ctrl)
        ctrl.ensure_open(world)

        # Calcular ancho disponible del input para poder medir su altura envuelta
        input_prompt_w = small.render('>', True, (0,255,0)).get_width()
        input_x = panel_x + pad
        input_x2 = input_x + input_prompt_w + 6
        input_max_w = max(10, (panel_x + panel_w - pad) - input_x2)
        # Medir líneas/altura envuelta del input (puede ser una o varias líneas)
        try:
            _, input_total_h = ctrl.text.measure_wrapped(input_max_w)
        except Exception:
            input_total_h = font.get_height()

        # Área de mensajes
        msg_area_x = panel_x + pad
        msg_area_y = panel_y + pad + title.get_height() + 6
        msg_area_w = panel_w - pad * 2
        # Reservar altura adicional si el input ocupa más de una línea
        base_input_h = font.get_height()
        extra_input_h = max(0, int(input_total_h) - int(base_input_h))
        msg_area_h = panel_h - (pad * 3 + title.get_height() + 28 + extra_input_h)
        line_height = small.get_linesize()

        # Word-wrap de mensajes a líneas pixel-perfect
        raw_messages = list(getattr(state, 'chat_messages', []))
        wrapped_lines = []  # list[(sender:str, line:str, is_first:bool, prefix_w:int)]
        for sender, text in raw_messages:
            prefix = f"{sender}: "
            pref_surf = small.render(prefix, True, (220,220,100))
            pref_w = pref_surf.get_width()
            first_width = max(0, msg_area_w - pref_w - 12)  # margen para scrollbar
            # Wrap
            for i, seg in enumerate(_wrap_text_small(small, text or "", first_width)):
                wrapped_lines.append((sender, seg, i == 0, pref_w))

        total_lines = len(wrapped_lines)
        visible_lines = max(1, msg_area_h // line_height)
        # Scroll persistente (0 = abajo/últimos)
        scroll = int(getattr(state, 'chat_scroll_lines', 0) or 0)
        max_scroll = max(0, total_lines - visible_lines)
        scroll = max(0, min(scroll, max_scroll))
        try:
            state.chat_scroll_lines = scroll
            state.chat_total_lines = total_lines
            state.chat_visible_lines = visible_lines
        except Exception:
            pass
        start_idx = max(0, total_lines - visible_lines - scroll)
        end_idx = min(total_lines, start_idx + visible_lines)

        # Clip de mensajes
        prev_clip = screen.get_clip()
        screen.set_clip(pygame.Rect(msg_area_x, msg_area_y, msg_area_w, msg_area_h))
        # Dibujar líneas visibles
        y = msg_area_y
        for i in range(start_idx, end_idx):
            sender, seg, is_first, pref_w = wrapped_lines[i]
            if is_first:
                pref_surf = small.render(f"{sender}: ", True, (220,220,100))
                screen.blit(pref_surf, (msg_area_x, y))
                line_x = msg_area_x + pref_w
            else:
                line_x = msg_area_x + pref_w
            seg_surf = small.render(seg, True, (230,230,230))
            screen.blit(seg_surf, (line_x, y))
            y += line_height
        screen.set_clip(prev_clip)

        # Scrollbar profesional
        sb_w = 10
        sb_rect = pygame.Rect(msg_area_x + msg_area_w - sb_w, msg_area_y, sb_w, msg_area_h)
        show_scroll = total_lines > visible_lines
        if show_scroll:
            pygame.draw.rect(screen, (30,30,30,200), sb_rect)
            # Thumb (altura nunca mayor que el track)
            frac = visible_lines / float(total_lines)
            thumb_h = int(sb_rect.h * frac)
            thumb_h = max(20, min(sb_rect.h, thumb_h))
            # Invertimos el mapeo: scroll=0 (últimos) -> thumb abajo
            if max_scroll > 0:
                pos_frac = 1.0 - (scroll / float(max_scroll))
            else:
                pos_frac = 1.0
            span = max(0, sb_rect.h - thumb_h)
            thumb_y = sb_rect.y + int(span * pos_frac)
            thumb_rect = pygame.Rect(sb_rect.x+1, thumb_y, sb_w-2, thumb_h)
            hovering_sb = sb_rect.collidepoint(mx, my)
            hovering_thumb = thumb_rect.collidepoint(mx, my)
            pygame.draw.rect(screen, (70,70,70) if not hovering_sb else (90,90,90), sb_rect, width=0)
            pygame.draw.rect(screen, (160,160,160) if not hovering_thumb else (200,200,200), thumb_rect, border_radius=3)
            try:
                state.chat_scrollbar_rect = sb_rect
                state.chat_scrollbar_thumb_rect = thumb_rect
            except Exception:
                pass
        else:
            # No mostrar barra si no es necesaria y limpiar rects
            try:
                state.chat_scrollbar_rect = None
                state.chat_scrollbar_thumb_rect = None
            except Exception:
                pass

        # Indicador de escritura ('.', '..', '...')
        try:
            if getattr(state, 'chat_typing', False):
                now = pygame.time.get_ticks()
                last = getattr(state, 'chat_typing_last_ms', None)
                if last is None or (now - last) >= 300:
                    state.chat_typing_last_ms = now
                    phase = int(getattr(state, 'chat_typing_phase', 0) or 0)
                    phase = (phase + 1) % 3
                    state.chat_typing_phase = phase
                dots = ['.', '..', '...'][int(getattr(state, 'chat_typing_phase', 0) or 0)]
                tip = small.render(dots, True, (200,200,200))
                screen.blit(tip, (msg_area_x, y))
                y += line_height
        except Exception:
            pass
        # Campo input
        input_y = panel_y + panel_h - pad - font.get_height()
        # Dibuja prompt
        prompt = small.render(">", True, (0,255,0))
        screen.blit(prompt, (input_x, input_y))
        # Dibujo del input con word-wrap dentro del ancho disponible del panel
        ctrl.draw_input(screen, input_x2, input_y, color=(255,255,255), max_width=input_max_w, align_bottom=True)


# ===== Helpers de wrapping reutilizables =====
def _wrap_text_small(font: pygame.font.Font, text: str, max_width: int) -> list[str]:
    if max_width <= 0:
        return [text]
    words = (text or "").split()
    lines: list[str] = []
    cur = ""
    for w in words:
        add = (cur + (" " if cur else "") + w).strip()
        if font.size(add)[0] <= max_width:
            cur = add
            continue
        if not cur:
            # palabra que no cabe; dividir
            lines.extend(_split_long_word_small(font, w, max_width)[:-1])
            cur = _split_long_word_small(font, w, max_width)[-1] if _split_long_word_small(font, w, max_width) else w
        else:
            lines.append(cur)
            if font.size(w)[0] <= max_width:
                cur = w
            else:
                parts = _split_long_word_small(font, w, max_width)
                lines.extend(parts[:-1])
                cur = parts[-1] if parts else w
    if cur:
        lines.append(cur)
    return lines

def _split_long_word_small(font: pygame.font.Font, word: str, max_width: int) -> list[str]:
    if font.size(word)[0] <= max_width:
        return [word]
    out: list[str] = []
    buf = ""
    for ch in word:
        t = buf + ch
        if font.size(t)[0] <= max_width:
            buf = t
        else:
            if buf:
                out.append(buf)
            buf = ch
    if buf:
        out.append(buf)
    return out


# ===== Manejador de eventos de UI del chat (scroll, resize, scrollbar) =====
def handle_chat_ui_events(world, events):
    state = getattr(world, 'state', None)
    if not state or not getattr(state, 'chat_open', False):
        return
    panel_rect = getattr(state, 'chat_block_rect', None)
    if not panel_rect:
        return
    # Defaults
    if not hasattr(state, 'chat_scroll_lines'):
        state.chat_scroll_lines = 0
    # Drag de resize
    resizing = bool(getattr(state, 'chat_resizing', False))
    resize_rect = getattr(state, 'chat_resize_rect', None)
    sb_rect = getattr(state, 'chat_scrollbar_rect', None)
    thumb_rect = getattr(state, 'chat_scrollbar_thumb_rect', None)
    dragging_thumb = bool(getattr(state, 'chat_dragging_thumb', False))
    for ev in events:
        if ev.type == pygame.MOUSEWHEEL:
            mx, my = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mx, my):
                # rueda hacia arriba: ev.y > 0
                step = 3
                state.chat_scroll_lines = max(0, int(state.chat_scroll_lines) + (step if ev.y > 0 else -step))
        elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mx, my = ev.pos
            if resize_rect and resize_rect.collidepoint(mx, my):
                state.chat_resizing = True
                state.chat_resize_start = (mx, my)
                state.chat_resize_wh0 = (int(getattr(state, 'chat_panel_w', 400) or 400), int(getattr(state, 'chat_panel_h', 200) or 200))
            elif thumb_rect and thumb_rect.collidepoint(mx, my):
                state.chat_dragging_thumb = True
                state.chat_drag_thumb_off = my - thumb_rect.y
            elif sb_rect and sb_rect.collidepoint(mx, my):
                # click en el track: posicionar
                thumb_h = thumb_rect.h if thumb_rect else 30
                rel = my - sb_rect.y - thumb_h // 2
                rel = max(0, min(rel, sb_rect.h - thumb_h))
                # Convertir a scroll
                total = int(getattr(state, 'chat_total_lines', 0) or 0)
                vis = int(getattr(state, 'chat_visible_lines', 1) or 1)
                max_scroll = max(0, total - vis)
                if sb_rect.h - thumb_h > 0:
                    pos_frac = rel / float(sb_rect.h - thumb_h)
                else:
                    pos_frac = 0.0
                # Invertido: pos_frac=1 (abajo) => scroll=0 (últimos)
                state.chat_scroll_lines = int(round(max_scroll * (1.0 - pos_frac)))
        elif ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
            state.chat_resizing = False
            state.chat_dragging_thumb = False
        elif ev.type == pygame.MOUSEMOTION:
            mx, my = ev.pos
            if dragging_thumb:
                # ajustar segun posición
                off = int(getattr(state, 'chat_drag_thumb_off', 0) or 0)
                if sb_rect and thumb_rect:
                    thumb_h = thumb_rect.h
                    rel = my - sb_rect.y - off
                    rel = max(0, min(rel, sb_rect.h - thumb_h))
                    total = int(getattr(state, 'chat_total_lines', 0) or 0)
                    vis = int(getattr(state, 'chat_visible_lines', 1) or 1)
                    max_scroll = max(0, total - vis)
                    if sb_rect.h - thumb_h > 0:
                        pos_frac = rel / float(sb_rect.h - thumb_h)
                    else:
                        pos_frac = 0.0
                    # Invertido: pos_frac=1 (abajo) => scroll=0 (últimos)
                    state.chat_scroll_lines = int(round(max_scroll * (1.0 - pos_frac)))
            if resizing:
                sx, sy = getattr(state, 'chat_resize_start', (mx, my))
                w0, h0 = getattr(state, 'chat_resize_wh0', (400, 200))
                dx = mx - sx
                dy = my - sy
                # Esquina superior derecha: dx aumenta ancho, dy hacia abajo reduce alto
                new_w = max(320, min(1200, int(w0 + dx)))
                new_h = max(160, min(600, int(h0 - dy)))
                state.chat_panel_w = new_w
                state.chat_panel_h = new_h
