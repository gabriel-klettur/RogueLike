import pygame
from typing import Dict

from roguelike_editors.particles.services.preview_builder import build_preview_for_definition
from roguelike_game.config.particles_config import get_preset


class ParticlePresetRenderSystem:
    """Render system for persisted particle instances by preset id.

    Draws the same animated preview used by the picker/editor, centered at the
    entity's Position, with the same cell size convention.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Cache of preview providers per INSTANCE, not per preset, to avoid
        # synchronized animations when multiple entities share the same preset.
        # Key format: f"{preset_id}#{entry_id or eid}"
        self._providers: Dict[str, object] = {}
        self._last_ticks = 0
        # Match picker default cell size minus padding
        self._cell_size = 56

    def _get_provider(self, cache_key: str, preset_id: str):
        prov = self._providers.get(cache_key)
        if prov is not None:
            return prov
        try:
            p = get_preset(preset_id)
            if p is None:
                return None
            defn = {
                "id": getattr(p, "id", preset_id),
                "name": getattr(p, "name", preset_id),
                "type": getattr(p, "type", ""),
                "vfx": getattr(p, "vfx", {}),
            }
            obj = build_preview_for_definition(defn)
            if obj is None:
                return None
            def provider(size, dt_ms):
                return obj.render(size, dt_ms)
            self._providers[cache_key] = provider
            return provider
        except Exception:
            return None

    def update(self, world, screen: pygame.Surface, camera):
        # Animate at modest dt similar to picker/editor
        now = pygame.time.get_ticks()
        if self._last_ticks == 0:
            dt_ms = 16
        else:
            dt_ms = max(0, min(48, now - self._last_ticks))
        self._last_ticks = now
        # Cache screen rect once for offscreen culling
        screen_rect = screen.get_rect()
        # Read camera zoom once per frame (assume uniform scaling)
        try:
            base_zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
        except Exception:
            base_zoom = 1.0
        base_zoom = max(0.25, min(4.0, base_zoom))
        # Precompute integer-zoom info
        izoom = int(round(base_zoom))
        is_integer_zoom = abs(base_zoom - float(izoom)) < 0.01 and izoom >= 1

        pos_map = world.components.get('Position', {})
        presets = world.components.get('ParticlePresetComponent', {})
        if not presets:
            return
        for eid, comp in list(presets.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            pid = getattr(comp, 'preset_id', '')
            # Use persisted entry_id when available to ensure per-instance state
            try:
                entry_id = getattr(comp, 'entry_id', None)
            except Exception:
                entry_id = None
            cache_key = f"{pid}#{int(entry_id)}" if entry_id is not None else f"{pid}#{int(eid)}"
            provider = self._get_provider(cache_key, pid)
            if provider is None:
                continue
            try:
                sx, sy = camera.apply((pos.x, pos.y))
                # Efecto de escala por instancia (impacto más grande/pequeño)
                try:
                    inst_mul = float(getattr(comp, 'scale_multiplier', 1.0))
                except Exception:
                    inst_mul = 1.0
                eff_zoom = max(0.05, base_zoom * max(0.05, inst_mul))
                # Offscreen culling BEFORE calling provider to skip unnecessary work
                bw = int(self._cell_size * eff_zoom)
                bh = int(self._cell_size * eff_zoom)
                if (sx + bw // 2) < screen_rect.left or (sx - bw // 2) > screen_rect.right or (sy + bh // 2) < screen_rect.top or (sy - bh // 2) > screen_rect.bottom:
                    continue
                base_size = (self._cell_size, self._cell_size)
                surf = provider(base_size, dt_ms)
                if surf is not None:
                    if abs(eff_zoom - 1.0) > 0.01:
                        tw = max(1, int(surf.get_width() * eff_zoom))
                        th = max(1, int(surf.get_height() * eff_zoom))
                        try:
                            if is_integer_zoom and abs(inst_mul - 1.0) < 0.01:
                                scaled = pygame.transform.scale(surf, (tw, th))
                            else:
                                scaled = pygame.transform.smoothscale(surf, (tw, th))
                        except Exception:
                            scaled = pygame.transform.scale(surf, (tw, th))
                        screen.blit(scaled, (int(sx - tw // 2), int(sy - th // 2)))
                    else:
                        screen.blit(surf, (int(sx - surf.get_width() // 2), int(sy - surf.get_height() // 2)))
            except Exception:
                continue
