from __future__ import annotations

import math
from typing import Any, Dict, Tuple

import pygame


class SpawnerAuraRenderSystem:
    """Renderiza auras alrededor de los edificios marcados como visuals de spawner.

    Usa la configuración almacenada en ``_spawner_visual_fx`` (inyectada por SpawnerVisualSync)
    para dibujar un contorno tipo "aura" alrededor de la imagen del Building cuando el
    spawner está en cierto estado (por ejemplo, ``wait_cooldown``).

    Espera un diccionario de FX similar a::

        {
            "aura_outline": {
                "kind": "outline",
                "color": [255, 230, 100],
                "thickness": 10,
                "pulse_speed_hz": 3.0,
                "alpha_min": 120,
                "alpha_max": 220,
            }
        }

    Solo se apoya en atributos dinámicos de Building:
    - ``_is_spawner_visual``: True si el Building pertenece a un spawner.
    - ``runtime_hidden``: True si el visual está oculto en este frame.
    - ``_spawner_visual_fx``: dict con la configuración de FX para el estado actual.
    - ``image``, ``x``, ``y``: usados para tamaño y posición.
    """

    def __init__(self, perf_log: Any | None = None) -> None:
        self.perf_log = perf_log
        # Cache de superficies de contorno por (frame_id, thickness)
        self._outline_cache: Dict[Tuple[int, int], pygame.Surface] = {}
        # Cache de superficies escaladas por zoom: (frame_id, thickness, zoom_key)
        self._zoom_cache: Dict[Tuple[int, int, int], pygame.Surface] = {}

    def update(self, world, screen: pygame.Surface, camera) -> None:  # type: ignore[override]
        buildings = getattr(world, "buildings", []) or []
        if not buildings:
            return

        zoom = float(getattr(camera, "zoom", 1.0) or 1.0)
        zoom_key = max(1, int(zoom * 100))

        for b in buildings:
            try:
                # Solo visuals de spawner visibles en este frame
                if getattr(b, "runtime_hidden", False):
                    continue
                if not bool(getattr(b, "_is_spawner_visual", False)):
                    continue

                fx = getattr(b, "_spawner_visual_fx", None)
                if not isinstance(fx, dict):
                    continue
                aura_cfg = fx.get("aura_outline")
                if not isinstance(aura_cfg, dict):
                    continue

                kind = str(aura_cfg.get("kind", "outline") or "outline").strip().lower()
                if kind != "outline":
                    continue

                # Color base del aura
                col_val = aura_cfg.get("color", (255, 230, 100))
                try:
                    r, g, bcol = int(col_val[0]), int(col_val[1]), int(col_val[2])
                    base_color = (
                        max(0, min(r, 255)),
                        max(0, min(g, 255)),
                        max(0, min(bcol, 255)),
                    )
                except Exception:
                    base_color = (255, 230, 100)

                # Grosor y parámetros de pulso
                try:
                    thickness = max(1, int(aura_cfg.get("thickness", 8)))
                except Exception:
                    thickness = 8
                try:
                    alpha_min = int(aura_cfg.get("alpha_min", 120))
                    alpha_max = int(aura_cfg.get("alpha_max", 220))
                except Exception:
                    alpha_min, alpha_max = 120, 220
                if alpha_max < alpha_min:
                    alpha_min, alpha_max = alpha_max, alpha_min
                try:
                    freq_hz = float(aura_cfg.get("pulse_speed_hz", 3.0) or 0.0)
                except Exception:
                    freq_hz = 3.0

                # Calcular alpha pulsante
                t = pygame.time.get_ticks() / 1000.0
                if freq_hz > 0.0:
                    arg = 2.0 * math.pi * freq_hz * t
                else:
                    arg = 0.0
                pulse = 0.5 + 0.5 * math.sin(arg)
                alpha = int(alpha_min + (alpha_max - alpha_min) * pulse)

                base_img = getattr(b, "image", None)
                if not isinstance(base_img, pygame.Surface):
                    continue
                w, h = base_img.get_size()
                if w <= 0 or h <= 0:
                    continue

                frame_id = id(base_img)
                cache_key = (frame_id, thickness)
                aura = self._outline_cache.get(cache_key)

                if aura is None:
                    # Generar contorno desde la máscara del Building
                    try:
                        mask = pygame.mask.from_surface(base_img)
                        outline = mask.outline()
                    except Exception:
                        outline = []

                    aura = pygame.Surface((w, h), pygame.SRCALPHA)
                    if outline:
                        # Capas de glow alrededor del contorno
                        pygame.draw.polygon(aura, (*base_color, 60), outline, thickness + 8)
                        pygame.draw.polygon(aura, (*base_color, 140), outline, thickness + 4)
                        pygame.draw.polygon(aura, (*base_color, 220), outline, thickness)
                    else:
                        # Fallback: rectángulo alrededor del edificio
                        pygame.draw.rect(
                            aura,
                            (*base_color, 200),
                            pygame.Rect(0, 0, w, h),
                            width=thickness,
                        )

                    self._outline_cache[cache_key] = aura

                # Escalar por zoom de cámara (cacheado)
                if zoom_key != 100:
                    zkey = (frame_id, thickness, zoom_key)
                    aura_zoom = self._zoom_cache.get(zkey)
                    if aura_zoom is None:
                        zw = max(1, int(aura.get_width() * zoom))
                        zh = max(1, int(aura.get_height() * zoom))
                        aura_zoom = pygame.transform.smoothscale(aura, (zw, zh))
                        self._zoom_cache[zkey] = aura_zoom
                else:
                    aura_zoom = aura

                # Posición de dibujo: top-left del Building en mundo -> pantalla
                try:
                    bx = int(getattr(b, "x"))
                    by = int(getattr(b, "y"))
                except Exception:
                    # Fallback: usar rect si existe
                    rect = getattr(b, "rect", None)
                    if rect is None:
                        continue
                    bx, by = int(rect.x), int(rect.y)

                draw_x, draw_y = camera.apply((bx, by))

                # Aplicar alpha y dibujar
                aura_zoom.set_alpha(max(0, min(255, alpha)))
                screen.blit(aura_zoom, (int(draw_x), int(draw_y)))

            except Exception:
                # Nunca romper el frame por errores de VFX
                continue
