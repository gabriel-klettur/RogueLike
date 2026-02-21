import pygame
from roguelike_game.ecs.components.transform.scale import Scale


class ChatBubbleRenderSystem:
    """
    Dibuja burbujas de texto flotantes sobre entidades que tengan
    FloatingChatBubbleComponent.
    Las burbujas se desvanecen con el tiempo y se apilan verticalmente.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 18)
        # caches simples para medir texto si se quisiera optimizar más

    def update(self, world, screen, camera):
        comps = getattr(world, 'components', {})
        pos_map = comps.get('Position', {})
        sprite_map = comps.get('Sprite', {})
        scale_map = comps.get('Scale', {})
        bubble_map = comps.get('FloatingChatBubbleComponent', {})
        if not bubble_map:
            return
        now = pygame.time.get_ticks()
        # Iterar solo entidades visibles
        for eid in world.get_entities_in_camera(camera, 'Position'):
            comp = bubble_map.get(eid)
            if not comp or not comp.bubbles:
                continue
            pos = pos_map.get(eid)
            spr = sprite_map.get(eid)
            scl_comp: Scale = scale_map.get(eid, Scale())
            scl = float(getattr(scl_comp, 'scale', 1.0) or 1.0)
            if not pos:
                continue
            # Calcular anclaje sobre la cabeza
            wx, wy = float(getattr(pos, 'x', 0.0)), float(getattr(pos, 'y', 0.0))
            if spr and hasattr(spr, 'image') and spr.image:
                try:
                    sw, sh = spr.image.get_size()
                    anchor_x = wx + (sw * scl) / 2.0
                    # Justo por encima de la cabeza del sprite
                    anchor_y = wy - 6
                except Exception:
                    anchor_x, anchor_y = wx, wy - 6
            else:
                anchor_x, anchor_y = wx, wy - 6
            # Convertir a pantalla
            sx, sy = camera.apply((anchor_x, anchor_y))

            # Limpiar expiradas y preparar orden de dibujo (más antiguas abajo)
            bubbles = [b for b in comp.bubbles if (now - int(b.created_ms)) < int(b.ttl_ms)]
            comp.bubbles = bubbles
            if not bubbles:
                continue
            # Orden: antiguas primero para dibujar abajo
            bubbles.sort(key=lambda b: b.created_ms)

            # Apilar hacia arriba
            y_offset = 0
            spacing = 2
            for b in bubbles:
                elapsed = now - int(b.created_ms)
                ttl = max(1, int(b.ttl_ms))
                t = max(0.0, min(1.0, 1.0 - (elapsed / ttl)))  # 1 -> 0
                alpha_bg = int(200 * t)
                alpha_fg = int(255 * t)
                # Preparar superficies
                text_surf = self.font.render(b.text, True, b.color)
                text_surf = text_surf.convert_alpha()
                text_surf.set_alpha(alpha_fg)
                pad = 4
                w = text_surf.get_width() + pad * 2
                h = text_surf.get_height() + pad * 2
                bubble_surf = pygame.Surface((w, h), pygame.SRCALPHA)
                # Fondo
                bubble_surf.fill((b.bg_color[0], b.bg_color[1], b.bg_color[2], alpha_bg))
                # Borde
                pygame.draw.rect(bubble_surf, (*b.outline_color, min(255, alpha_bg + 40)), bubble_surf.get_rect(), 1)
                # Texto
                bubble_surf.blit(text_surf, (pad, pad))
                # Posición: centrar horizontalmente
                draw_x = int(sx - w / 2)
                draw_y = int(sy - y_offset - h)
                screen.blit(bubble_surf, (draw_x, draw_y))
                y_offset += h + spacing
