from __future__ import annotations

import logging
from typing import Any, Callable, Optional, Tuple

import pygame

from roguelike_editors.particles.services.preview import build_preview_for_definition


class ParticlePreviewManager:
    """Manages particle preview providers for spells.

    It owns a cache of preview objects (one per spell id) and installs
    provider callables into the editor view's `preview_providers` dict.
    """

    def __init__(
        self,
        view: Any,
        get_frame_id: Callable[[], int],
        enable_debug: bool = False,
    ) -> None:
        self.view = view
        self._get_frame_id = get_frame_id
        self._particle_previews: dict[str, Any] = {}
        self._enable_debug = enable_debug
        self._last_preview_debug_frame: int = -1
        self._logger = logging.getLogger(__name__)

    # Expose for compatibility (other modules may read this field on controller)
    @property
    def previews_cache(self) -> dict[str, Any]:
        return self._particle_previews

    # ---------- build/rebuild API ----------
    def rebuild(self, spells: dict[str, dict]) -> None:
        # Remove providers for spells that no longer exist
        for sid in list(self.view.preview_providers.keys()):
            if sid not in spells:
                self.view.preview_providers.pop(sid, None)
                self._particle_previews.pop(sid, None)
        # Rebuild/add providers for current spells
        for sid, sdef in spells.items():
            if not isinstance(sdef, dict):
                continue
            self.build_for_spell(sid, sdef)

    def build_for_spell(self, spell_id: str, sdef: dict) -> None:
        if not self.is_particle_spell(sdef):
            # Remove any existing provider/cache
            self._particle_previews.pop(spell_id, None)
            self.view.preview_providers.pop(spell_id, None)
            return

        preview_obj = build_preview_for_definition(sdef)
        if preview_obj is None:
            # Remove if cannot build
            self._particle_previews.pop(spell_id, None)
            self.view.preview_providers.pop(spell_id, None)
            return

        self._particle_previews[spell_id] = preview_obj
        last_frame_seen: int = -1
        sim_size: Optional[Tuple[int, int]] = None
        last_base_frame_id: int = -1
        last_base_surface: Optional[pygame.Surface] = None

        def provider(size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
            nonlocal last_frame_seen, sim_size, last_base_frame_id, last_base_surface
            frame_id = self._get_frame_id()

            # Choose a stable simulation size: grow to the largest requested so far
            req_w, req_h = max(1, int(size[0])), max(1, int(size[1]))
            sim_changed = False
            if sim_size is None:
                sim_size = (req_w, req_h)
                sim_changed = True
            else:
                new_sim = (max(sim_size[0], req_w), max(sim_size[1], req_h))
                if new_sim != sim_size:
                    sim_size = new_sim
                    sim_changed = True

            # Gate time so we only advance simulation once per frame
            effective_dt = dt_ms if frame_id != last_frame_seen else 0
            if (
                self._enable_debug
                and effective_dt > 0
                and self._last_preview_debug_frame != frame_id
            ):
                try:
                    self._logger.debug(
                        "[SpellsPreviewCall] %s: frame=%d dt_ms=%d size=%s sim_size=%s",
                        spell_id,
                        frame_id,
                        effective_dt,
                        (req_w, req_h),
                        sim_size,
                    )
                finally:
                    self._last_preview_debug_frame = frame_id

            last_frame_seen = frame_id

            # Render base surface at sim_size. Re-render if advancing time this frame,
            # or if simulation size changed, or if we don't have a cached base surface yet.
            need_base_render = (
                effective_dt > 0
                or sim_changed
                or last_base_surface is None
                or last_base_frame_id != frame_id
            )
            if need_base_render:
                base = preview_obj.render(sim_size, effective_dt if effective_dt > 0 else 0)
                last_base_surface = base
                last_base_frame_id = frame_id
            else:
                base = last_base_surface  # type: ignore[assignment]

            # Return scaled copy when requested size differs from sim_size
            if (req_w, req_h) != sim_size:
                try:
                    return pygame.transform.smoothscale(base, (req_w, req_h))  # type: ignore[arg-type]
                except Exception:
                    return pygame.transform.scale(base, (req_w, req_h))  # type: ignore[arg-type]
            return base  # type: ignore[return-value]

        self.view.preview_providers[spell_id] = provider
        if self._enable_debug:
            try:
                self._logger.debug(
                    "[SpellsPreview] %s: provider=%s",
                    spell_id,
                    type(preview_obj).__name__,
                )
            except Exception:
                pass

    # ---------- classification ----------
    def is_particle_spell(self, sdef: dict) -> bool:
        try:
            vfx = sdef.get("vfx", {}) or {}
            # Explicit flag
            if vfx.get("preview") == "particles":
                return True
            # Implicit: if particles config exists and is a dict, assume particle preview
            parts = vfx.get("particles")
            if isinstance(parts, dict) and len(parts) > 0:
                return True
            # Inferred by spell type
            stype = sdef.get("type")
            if stype in (
                "lightning",
                "aura",
                "beam",
                "dash",
                "slash",
                "arcane_flame",
                "firework",
                "firework_launch",
                "smoke_emitter",
                "smoke",
                "teleport",
                "sphere_magic_shield",
            ):
                return True
            # Fallback by id substring
            sid_l = str(sdef.get("id") or "").lower()
            for kw in (
                "aura",
                "beam",
                "laser",
                "dash",
                "slash",
                "lightning",
                "firework",
                "smoke",
                "flame",
                "teleport",
                "shield",
            ):
                if kw in sid_l:
                    return True
            return False
        except Exception:
            return False
