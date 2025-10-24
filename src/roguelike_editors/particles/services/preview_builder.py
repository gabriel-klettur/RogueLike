import logging
from typing import Any, Dict, Tuple, Optional

from roguelike_editors.spells.services.particle_preview import (
    ParticlePreviewSmoke,
    ParticlePreviewSmokeBurst,
    ParticlePreviewFirework,
    ParticlePreviewLightning,
    ParticlePreviewAura,
    ParticlePreviewHealingAura,
    ParticlePreviewDash,
    ParticlePreviewSlash,
    ParticlePreviewLaser,
    ParticlePreviewExplosion,
    ParticlePreviewArcaneFlame,
    ParticlePreviewTeleport,
    ParticlePreviewWaterFountain,
    ParticlePreviewFallingLeaf,
    ParticlePreviewWaterFlow,
)
from roguelike_game.config.particles_config import get_preset

logger = logging.getLogger(__name__)


def _resolve_particles_dict_from_definition(defn: Dict[str, Any]) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    """Resolve a final particles dict from a spell/effect definition handling presets.

    Returns (particles_dict, source_meta) where `source_meta` may include
    auxiliary info like `sid` (string id), `stype`, `vfx_obj`.
    """
    vfx = defn.get("vfx")
    source_meta: Dict[str, Any] = {}
    base: Dict[str, Any] = {}
    overrides: Dict[str, Any] = {}

    # New style: nested vfx object with optional preset + particles overrides
    if isinstance(vfx, dict):
        source_meta["vfx_obj"] = vfx
        preset_id = vfx.get("preset") if isinstance(vfx.get("preset"), str) else None
        if preset_id:
            source_meta["sid"] = preset_id
            p = get_preset(preset_id)
            if p and isinstance(p.vfx, dict):
                try:
                    pv = p.vfx.get("particles")
                    if isinstance(pv, dict):
                        base = dict(pv)
                except Exception:
                    pass
        # Overrides specificos en el spell/definición
        pov = vfx.get("particles")
        if isinstance(pov, dict):
            overrides = dict(pov)
    # Legacy: vfx como string => preset id
    elif isinstance(vfx, str):
        source_meta["sid"] = vfx
        p = get_preset(vfx)
        if p and isinstance(p.vfx, dict):
            try:
                pv = p.vfx.get("particles")
                if isinstance(pv, dict):
                    base = dict(pv)
            except Exception:
                pass
    # Merge con prioridad a overrides
    parts = {**base, **overrides}

    # Fallback: si no hay kind explícito, intentar inferir por type/id
    if "kind" not in parts:
        stype = defn.get("type")
        sid_l = str(defn.get("id") or "").lower()
        if stype in ("aura",):
            parts["kind"] = "aura"
        elif stype in ("beam",):
            parts["kind"] = "laser"
        elif stype in ("dash",):
            parts["kind"] = "dash"
        elif stype in ("slash",):
            parts["kind"] = "slash"
        elif stype in ("lightning",):
            parts["kind"] = "lightning"
        elif stype in ("arcane_flame",):
            parts["kind"] = "arcane_flame"
        elif stype in ("firework", "firework_launch"):
            parts["kind"] = "firework"
        elif stype in ("smoke_emitter",):
            parts["kind"] = "smoke_emitter"
        elif stype in ("smoke",):
            parts["kind"] = "smoke"
        elif stype in ("teleport",):
            parts["kind"] = "teleport"
        elif stype in ("sphere_magic_shield",):
            parts["kind"] = "aura"
        elif not parts.get("kind"):
            if "aura" in sid_l:
                parts["kind"] = "aura"
            elif "beam" in sid_l or "laser" in sid_l:
                parts["kind"] = "laser"
            elif "dash" in sid_l:
                parts["kind"] = "dash"
            elif "slash" in sid_l:
                parts["kind"] = "slash"
            elif "lightning" in sid_l:
                parts["kind"] = "lightning"
            elif "firework" in sid_l:
                parts["kind"] = "firework"
            elif "smoke_emitter" in sid_l:
                parts["kind"] = "smoke_emitter"
            elif "smoke" in sid_l:
                parts["kind"] = "smoke"
            elif "flame" in sid_l:
                parts["kind"] = "arcane_flame"
            elif "teleport" in sid_l:
                parts["kind"] = "teleport"
            elif "shield" in sid_l:
                parts["kind"] = "aura"
    return parts, source_meta


def build_preview_for_definition(defn: Dict[str, Any]):
    """Construye un objeto de preview para una definición (spell o preset) usando presets si aplica.

    Devuelve un objeto que implementa `.render((w,h), dt_ms)` o `None` si no puede construirlo.
    """
    try:
        parts, meta = _resolve_particles_dict_from_definition(defn)
        kind = parts.get("kind")
        # Color/paleta opcional común
        color = None
        color_explicit = False
        palette_colors = []
        try:
            color_tuple = parts.get("color")
            if isinstance(color_tuple, (list, tuple)) and len(color_tuple) >= 3:
                color = (int(color_tuple[0]), int(color_tuple[1]), int(color_tuple[2]))
                color_explicit = True
            else:
                colors_list = parts.get("colors")
                if isinstance(colors_list, (list, tuple)) and len(colors_list) > 0:
                    c0 = colors_list[0]
                    if isinstance(c0, (list, tuple)) and len(c0) >= 3:
                        color = (int(c0[0]), int(c0[1]), int(c0[2]))
                        color_explicit = True
                        for c in colors_list:
                            if isinstance(c, (list, tuple)) and len(c) >= 3:
                                palette_colors.append((int(c[0]), int(c[1]), int(c[2])))
        except Exception:
            pass

        # Construcción por tipo
        if kind in (None, "smoke_emitter"):
            emit_rate = 2
            er = parts.get("emit_rate")
            if isinstance(er, int) and er > 0:
                emit_rate = er
            else:
                cnt = parts.get("count")
                if isinstance(cnt, int) and cnt > 0:
                    emit_rate = max(1, min(8, cnt // 2))
            # Optional parameters used by trail-like presets
            spd = parts.get("speed")
            speed = float(spd) if isinstance(spd, (int, float)) else 1.0
            life = parts.get("lifespan")
            lifespan = float(life) if isinstance(life, (int, float)) else 100.0
            sr = parts.get("size_range") if isinstance(parts.get("size_range"), (list, tuple)) and len(parts.get("size_range")) >= 2 else None
            # Map degrees-based dispersion (if provided) to a gaussian jitter sigma
            disp = parts.get("dispersion")
            dispersion = float(disp) * 0.025 if isinstance(disp, (int, float)) else 0.3
            warm_steps = min(24, 6 + emit_rate * 2)
            palette = palette_colors if palette_colors else None
            return ParticlePreviewSmoke(
                color=color if color_explicit else (200, 200, 200),
                emit_rate=emit_rate,
                warm_start_steps=warm_steps,
                palette=palette,
                speed=speed,
                lifespan=lifespan,
                size_range=sr,
                dispersion=dispersion,
            )
        if kind in ("smoke",):
            cnt = parts.get("count") if isinstance(parts.get("count"), int) else 12
            cnt = max(1, min(40, cnt))
            direction = parts.get("direction") if isinstance(parts.get("direction"), (list, tuple)) and len(parts.get("direction")) >= 2 else (0.0, -1.0)
            warm_steps = min(18, 6 + cnt // 4)
            return ParticlePreviewSmokeBurst(color=color if color_explicit else (200, 200, 200), count=int(cnt), direction=direction, warm_start_steps=warm_steps)
        if kind in ("firework", "firework_launch"):
            speed = parts.get("speed")
            if not isinstance(speed, (int, float)):
                speed = 12.0
            return ParticlePreviewFirework(color=color if color_explicit else None, speed=float(speed))
        if kind in ("lightning",):
            segments = parts.get("segments") if isinstance(parts.get("segments"), int) else 10
            offset = parts.get("offset") if isinstance(parts.get("offset"), int) else 10
            lifetime = parts.get("lifetime") if isinstance(parts.get("lifetime"), int) else 8
            thickness = parts.get("thickness") if isinstance(parts.get("thickness"), int) else 2
            return ParticlePreviewLightning(color=color if color_explicit else (120, 200, 255), segments=segments, offset=offset, lifetime=lifetime, thickness=thickness)
        if kind in ("aura",):
            radius = parts.get("radius") if isinstance(parts.get("radius"), int) else None
            # Heuristic: healing-like when has emit params
            has_emit = any(k in parts for k in ("emit_rate", "lifespan", "size_range"))
            default_aura_color = (80, 200, 120)
            if has_emit:
                emit_rate = parts.get("emit_rate") if isinstance(parts.get("emit_rate"), int) and parts.get("emit_rate") > 0 else 3
                speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 1.0
                lifespan = parts.get("lifespan") if isinstance(parts.get("lifespan"), int) else 60
                size_range = parts.get("size_range") if isinstance(parts.get("size_range"), (list, tuple)) else (4, 8)
                palette = palette_colors if len(palette_colors) > 0 else None
                warm_steps = min(24, 6 + int(emit_rate) * 2)
                return ParticlePreviewHealingAura(
                    color=color if color_explicit else default_aura_color,
                    palette=palette,
                    radius=radius,
                    emit_rate=int(emit_rate),
                    speed=float(speed),
                    lifespan=int(lifespan),
                    size_range=size_range,
                    warm_start_steps=warm_steps,
                )
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 1.0
            if isinstance(parts.get("count"), int):
                count = int(parts.get("count"))
            else:
                er = parts.get("emit_rate")
                count = max(8, min(40, int(er) * 8)) if isinstance(er, int) and er > 0 else 24
            palette = palette_colors if len(palette_colors) > 0 else None
            return ParticlePreviewAura(color=color if color_explicit else default_aura_color, radius=radius, speed=float(speed), count=int(count), palette=palette)
        if kind in ("dash",):
            speed_px = parts.get("speed_px") if isinstance(parts.get("speed_px"), (int, float)) else 60.0
            return ParticlePreviewDash(color=color if color_explicit else (180, 220, 255), speed_px=float(speed_px))
        if kind in ("slash",):
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 2.5
            return ParticlePreviewSlash(color=color if color_explicit else (100, 220, 255), speed=float(speed))
        if kind in ("laser",):
            return ParticlePreviewLaser(color=color if color_explicit else (0, 255, 255))
        if kind in ("arcane_flame",):
            duration = defn.get("effect", {}).get("duration") if isinstance(defn.get("effect", {}), dict) and isinstance(defn.get("effect", {}).get("duration"), (int, float)) else 5.0
            seed = parts.get("seed") if isinstance(parts.get("seed"), int) else 0
            cnt = parts.get("count") if isinstance(parts.get("count"), int) else 20
            spark_rate = max(2, min(14, int(cnt * 0.5)))
            spd = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 100.0
            spark_speed = max(0.6, min(2.5, float(spd) / 90.0))
            life = parts.get("lifespan") if isinstance(parts.get("lifespan"), int) else 60
            spark_life = max(12, min(60, int(life * 0.5)))
            sr = parts.get("size_range") if isinstance(parts.get("size_range"), (list, tuple)) and len(parts.get("size_range")) == 2 else (2, 6)
            smin = max(1, min(3, int(sr[0])))
            smax = max(smin, min(4, int(sr[1])))
            return ParticlePreviewArcaneFlame(
                duration=float(duration),
                seed=int(seed),
                spark_rate=int(spark_rate),
                spark_speed=float(spark_speed),
                spark_size_range=(smin, smax),
                spark_lifespan=int(spark_life),
            )
        if kind in ("teleport",):
            life = defn.get("effect", {}).get("lifetime") if isinstance(defn.get("effect", {}), dict) and isinstance(defn.get("effect", {}).get("lifetime"), (int, float)) else None
            if isinstance(life, (int, float)):
                cycle_ms = int(max(300, min(900, float(life) * 1000)))
            else:
                cycle_ms = 600
            return ParticlePreviewTeleport(color=color if color_explicit else (0, 200, 255), cycle_ms=cycle_ms)
        if kind in ("water_fountain", "fountain"):
            # Map configurable parameters to preview constructor
            spouts = parts.get("spouts")
            if not isinstance(spouts, (list, tuple)) or len(spouts) == 0:
                spouts = [0.34, 0.5, 0.66]
            try:
                spouts = [float(max(0.05, min(0.95, s))) for s in spouts]
            except Exception:
                spouts = [0.34, 0.5, 0.66]
            emit_rate = parts.get("emit_rate") if isinstance(parts.get("emit_rate"), int) and parts.get("emit_rate") > 0 else 5
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 2.0
            gravity = parts.get("gravity") if isinstance(parts.get("gravity"), (int, float)) else 0.25
            droplet_size = parts.get("droplet_size") if isinstance(parts.get("droplet_size"), int) else 2
            splash_count = parts.get("splash_count") if isinstance(parts.get("splash_count"), int) else 2
            return ParticlePreviewWaterFountain(
                color=color if color_explicit else (100, 180, 255),
                spouts=spouts,
                emit_rate=int(emit_rate),
                speed=float(speed),
                gravity=float(gravity),
                droplet_size=int(droplet_size),
                splash_count=int(splash_count),
            )
        if kind in ("falling_leaf", "leaf"):
            # One leaf at a configurable interval (ms). Defaults to very sparse.
            interval_ms = parts.get("interval_ms")
            if not isinstance(interval_ms, int) or interval_ms <= 0:
                # allow seconds aliases
                sec = parts.get("interval_s") if isinstance(parts.get("interval_s"), (int, float)) else None
                interval_ms = int(float(sec) * 1000.0) if isinstance(sec, (int, float)) else 30000
            life_ms = parts.get("life_ms")
            if not isinstance(life_ms, int) or life_ms <= 0:
                if isinstance(parts.get("lifespan_ms"), int):
                    life_ms = int(parts.get("lifespan_ms"))
                elif isinstance(parts.get("lifespan"), int):
                    # lifespan in 33ms steps (compat)
                    life_ms = int(parts.get("lifespan")) * 33
                else:
                    life_ms = 6000
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 0.5
            gravity = parts.get("gravity") if isinstance(parts.get("gravity"), (int, float)) else 0.06
            sway_amp = parts.get("sway_amp") if isinstance(parts.get("sway_amp"), (int, float)) else 0.6
            sway_speed = parts.get("sway_speed") if isinstance(parts.get("sway_speed"), (int, float)) else 0.15
            size = parts.get("size") if isinstance(parts.get("size"), (list, tuple)) and len(parts.get("size")) >= 2 else (3, 2)
            return ParticlePreviewFallingLeaf(
                color=color if color_explicit else (120, 200, 80),
                interval_ms=int(interval_ms),
                life_ms=int(life_ms),
                speed=float(speed),
                gravity=float(gravity),
                sway_amp=float(sway_amp),
                sway_speed=float(sway_speed),
                size=(int(size[0]), int(size[1])) if isinstance(size, (list, tuple)) else (3, 2),
            )
        if kind in ("explosion",):
            palette = palette_colors if len(palette_colors) > 0 else None
            base_color = color if color_explicit else (255, 180, 60)
            cnt = parts.get("count") if isinstance(parts.get("count"), int) else 24
            spd = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else None
            if isinstance(spd, (int, float)):
                lo = max(0.6, float(spd) * 0.012)
                hi = max(lo + 0.4, float(spd) * 0.024)
                speed_range = (lo, hi)
            else:
                speed_range = (0.8, 2.5)
            return ParticlePreviewExplosion(color=base_color, palette=palette, count=int(cnt), speed_range=speed_range)
        if kind in ("water_flow", "water"):
            # Flowing water tile: uses base+highlight colors and a flow direction
            # Default colors if not specified
            base_col = color if color_explicit else (20, 40, 80)
            hl = parts.get("highlight_color")
            if not (isinstance(hl, (list, tuple)) and len(hl) >= 3):
                if len(palette_colors) >= 2:
                    hl = palette_colors[1]
                else:
                    hl = (60, 110, 160)
            direction = parts.get("direction") if isinstance(parts.get("direction"), (list, tuple)) and len(parts.get("direction")) >= 2 else (1.0, 0.0)
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 0.6
            stripe_gap = parts.get("stripe_gap") if isinstance(parts.get("stripe_gap"), int) else 8
            ripple_amp = parts.get("ripple_amp") if isinstance(parts.get("ripple_amp"), (int, float)) else 0.6
            alpha_base = parts.get("alpha_base") if isinstance(parts.get("alpha_base"), int) else 120
            alpha_wave = parts.get("alpha_wave") if isinstance(parts.get("alpha_wave"), int) else 80
            return ParticlePreviewWaterFlow(
                base_color=base_col,
                highlight_color=(int(hl[0]), int(hl[1]), int(hl[2])),
                direction=(float(direction[0]), float(direction[1])),
                speed=float(speed),
                stripe_gap=int(stripe_gap),
                ripple_amp=float(ripple_amp),
                alpha_base=int(alpha_base),
                alpha_wave=int(alpha_wave),
            )
        # Fallback a humo
        emit_rate = 2
        er = parts.get("emit_rate")
        if isinstance(er, int) and er > 0:
            emit_rate = er
        return ParticlePreviewSmoke(color=color if color_explicit else (200, 200, 200), emit_rate=emit_rate)
    except Exception:
        logger.exception("[preview_builder] Failed to build preview for definition: %s", defn)
        return None
