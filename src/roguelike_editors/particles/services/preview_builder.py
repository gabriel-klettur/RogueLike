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


def _warn_curve(name: str, curve) -> None:
    if not isinstance(curve, (list, tuple)):
        return
    last_t = -1e9
    bad = False
    for pt in curve:
        try:
            t = float(pt[0])
        except Exception:
            bad = True
            continue
        if not (0.0 <= t <= 1.0):
            bad = True
        if t < last_t:
            bad = True
        last_t = t
    if bad:
        try:
            logger.warning("[particles.preview] curve '%s' has unsorted/out-of-range keys; expected t in [0,1] ascending", name)
        except Exception:
            pass


def _warn_emission(kind: str, shape: Optional[str], extent) -> None:
    if not isinstance(shape, str):
        return
    shp = shape.lower()
    known = {"point", "circle", "ring", "line", "box", "cone", "mesh"}
    if shp not in known:
        try:
            logger.warning("[particles.preview] unknown emission_shape '%s' for kind=%s", shape, kind)
        except Exception:
            pass
        return
    if shp == "mesh":
        try:
            logger.warning("[particles.preview] emission_shape 'mesh' not simulated in preview; falling back to default distribution")
        except Exception:
            pass
    # Basic extent sanity
    try:
        if shp in ("circle", "cone"):
            if isinstance(extent, (int, float)) and float(extent) < 0:
                logger.warning("[particles.preview] negative extent radius for %s", shp)
        if shp == "ring":
            if isinstance(extent, (list, tuple)) and len(extent) >= 2:
                if float(extent[0]) > float(extent[1]):
                    logger.warning("[particles.preview] ring extent inner>outer; values=%s", extent)
        if shp == "box":
            if isinstance(extent, (list, tuple)) and len(extent) >= 2:
                if float(extent[0]) <= 0 or float(extent[1]) <= 0:
                    logger.warning("[particles.preview] non-positive box extent; values=%s", extent)
    except Exception:
        pass


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
            # Optional advanced params (ignore if not present)
            gv = parts.get("gravity")
            if isinstance(gv, (int, float)):
                gravity = (0.0, float(gv))
            elif isinstance(gv, (list, tuple)) and len(gv) >= 2:
                gravity = (float(gv[0]), float(gv[1]))
            else:
                gravity = None
            drag = parts.get("drag") if isinstance(parts.get("drag"), (int, float)) else None
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            sol = parts.get("size_over_life") if isinstance(parts.get("size_over_life"), (list, tuple)) else None
            aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            col = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            return ParticlePreviewSmoke(
                color=color if color_explicit else (200, 200, 200),
                emit_rate=emit_rate,
                warm_start_steps=warm_steps,
                palette=palette,
                speed=speed,
                lifespan=lifespan,
                size_range=sr,
                dispersion=dispersion,
                gravity=gravity,
                drag=drag,
                blend_mode=blend_mode,
                size_over_life=sol,
                alpha_over_life=aol,
                color_over_life=col,
                texture_path=parts.get("texture_path") if isinstance(parts.get("texture_path"), str) else None,
                flipbook=parts.get("flipbook") if isinstance(parts.get("flipbook"), dict) else None,
                speed_variance=parts.get("speed_variance") if isinstance(parts.get("speed_variance"), (int, float)) else None,
                lifetime_jitter=parts.get("lifetime_jitter") if isinstance(parts.get("lifetime_jitter"), (int, float)) else None,
                size_start=parts.get("size_start") if isinstance(parts.get("size_start"), (int, float, list, tuple)) else None,
            )
        if kind in ("smoke",):
            cnt = parts.get("count") if isinstance(parts.get("count"), int) else 12
            cnt = max(1, min(40, cnt))
            direction = parts.get("direction") if isinstance(parts.get("direction"), (list, tuple)) and len(parts.get("direction")) >= 2 else (0.0, -1.0)
            warm_steps = min(18, 6 + cnt // 4)
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            return ParticlePreviewSmokeBurst(
                color=color if color_explicit else (200, 200, 200),
                count=int(cnt),
                direction=direction,
                warm_start_steps=warm_steps,
                blend_mode=blend_mode,
                texture_path=parts.get("texture_path") if isinstance(parts.get("texture_path"), str) else None,
                flipbook=parts.get("flipbook") if isinstance(parts.get("flipbook"), dict) else None,
            )
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
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            col_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            _warn_curve("alpha_over_life", aol)
            _warn_curve("color_over_life", col_ol)
            return ParticlePreviewLightning(
                color=color if color_explicit else (120, 200, 255),
                segments=segments,
                offset=offset,
                lifetime=lifetime,
                thickness=thickness,
                blend_mode=blend_mode,
                alpha_over_life=aol,
                color_over_life=col_ol,
            )
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
                blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
                sol = parts.get("size_over_life") if isinstance(parts.get("size_over_life"), (list, tuple)) else None
                aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
                col_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
                # AAA emitter/init subset (optional)
                emission_shape = parts.get("emission_shape") if isinstance(parts.get("emission_shape"), str) else None
                emission_extent = parts.get("emission_extent") if isinstance(parts.get("emission_extent"), (list, tuple, int, float)) else None
                emission_direction = parts.get("emission_direction") if isinstance(parts.get("emission_direction"), (list, tuple)) else None
                angle_spread_deg = parts.get("emission_angle_spread_deg") if isinstance(parts.get("emission_angle_spread_deg"), (int, float)) else None
                speed_variance = parts.get("speed_variance") if isinstance(parts.get("speed_variance"), (int, float)) else None
                lifetime_jitter = parts.get("lifetime_jitter") if isinstance(parts.get("lifetime_jitter"), (int, float)) else None
                size_start = parts.get("size_start") if isinstance(parts.get("size_start"), (int, float, list, tuple)) else None
                _warn_curve("size_over_life", sol)
                _warn_curve("alpha_over_life", aol)
                _warn_curve("color_over_life", col_ol)
                _warn_emission("aura", emission_shape, emission_extent)
                bursts = parts.get("bursts") if isinstance(parts.get("bursts"), (list, tuple)) else None
                return ParticlePreviewHealingAura(
                    color=color if color_explicit else default_aura_color,
                    palette=palette,
                    radius=radius,
                    emit_rate=int(emit_rate),
                    speed=float(speed),
                    lifespan=int(lifespan),
                    size_range=size_range,
                    warm_start_steps=warm_steps,
                    blend_mode=blend_mode,
                    size_over_life=sol,
                    alpha_over_life=aol,
                    color_over_life=col_ol,
                    emission_shape=emission_shape,
                    emission_extent=emission_extent,
                    emission_direction=emission_direction,
                    emission_angle_spread_deg=angle_spread_deg,
                    speed_variance=speed_variance,
                    lifetime_jitter=lifetime_jitter,
                    size_start=size_start,
                    bursts=bursts,
                    texture_path=parts.get("texture_path") if isinstance(parts.get("texture_path"), str) else None,
                    flipbook=parts.get("flipbook") if isinstance(parts.get("flipbook"), dict) else None,
                )
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 1.0
            if isinstance(parts.get("count"), int):
                count = int(parts.get("count"))
            else:
                er = parts.get("emit_rate")
                count = max(8, min(40, int(er) * 8)) if isinstance(er, int) and er > 0 else 24
            palette = palette_colors if len(palette_colors) > 0 else None
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            return ParticlePreviewAura(color=color if color_explicit else default_aura_color, radius=radius, speed=float(speed), count=int(count), palette=palette, blend_mode=blend_mode)
        if kind in ("dash",):
            speed_px = parts.get("speed_px") if isinstance(parts.get("speed_px"), (int, float)) else 60.0
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            return ParticlePreviewDash(color=color if color_explicit else (180, 220, 255), speed_px=float(speed_px), blend_mode=blend_mode)
        if kind in ("slash",):
            speed = parts.get("speed") if isinstance(parts.get("speed"), (int, float)) else 2.5
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            return ParticlePreviewSlash(color=color if color_explicit else (100, 220, 255), speed=float(speed), blend_mode=blend_mode)
        if kind in ("laser",):
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            return ParticlePreviewLaser(color=color if color_explicit else (0, 255, 255), blend_mode=blend_mode)
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
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            sol = parts.get("size_over_life") if isinstance(parts.get("size_over_life"), (list, tuple)) else None
            col_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            emission_shape = parts.get("emission_shape") if isinstance(parts.get("emission_shape"), str) else None
            emission_extent = parts.get("emission_extent") if isinstance(parts.get("emission_extent"), (list, tuple, int, float)) else None
            speed_variance = parts.get("speed_variance") if isinstance(parts.get("speed_variance"), (int, float)) else None
            _warn_curve("alpha_over_life", aol)
            _warn_curve("size_over_life", sol)
            _warn_curve("color_over_life", col_ol)
            _warn_emission("water_fountain", emission_shape, emission_extent)
            return ParticlePreviewWaterFountain(
                color=color if color_explicit else (100, 180, 255),
                spouts=spouts,
                emit_rate=int(emit_rate),
                speed=float(speed),
                gravity=float(gravity),
                droplet_size=int(droplet_size),
                splash_count=int(splash_count),
                blend_mode=blend_mode,
                alpha_over_life=aol,
                size_over_life=sol,
                color_over_life=col_ol,
                emission_shape=emission_shape,
                emission_extent=emission_extent,
                speed_variance=speed_variance,
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
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            col_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            lifetime_jitter = parts.get("lifetime_jitter") if isinstance(parts.get("lifetime_jitter"), (int, float)) else None
            size_start = parts.get("size_start") if isinstance(parts.get("size_start"), (int, float, list, tuple)) else None
            _warn_curve("alpha_over_life", aol)
            _warn_curve("color_over_life", col_ol)
            return ParticlePreviewFallingLeaf(
                color=color if color_explicit else (120, 200, 80),
                interval_ms=int(interval_ms),
                life_ms=int(life_ms),
                speed=float(speed),
                gravity=float(gravity),
                sway_amp=float(sway_amp),
                sway_speed=float(sway_speed),
                size=(int(size[0]), int(size[1])) if isinstance(size, (list, tuple)) else (3, 2),
                blend_mode=blend_mode,
                alpha_over_life=aol,
                color_over_life=col_ol,
                lifetime_jitter=lifetime_jitter,
                size_start=size_start,
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
            # Advanced optional params
            blend_mode = parts.get("blend_mode") if isinstance(parts.get("blend_mode"), str) else None
            sol = parts.get("size_over_life") if isinstance(parts.get("size_over_life"), (list, tuple)) else None
            aol = parts.get("alpha_over_life") if isinstance(parts.get("alpha_over_life"), (list, tuple)) else None
            col_ol = parts.get("color_over_life") if isinstance(parts.get("color_over_life"), (list, tuple)) else None
            return ParticlePreviewExplosion(
                color=base_color,
                palette=palette,
                count=int(cnt),
                speed_range=speed_range,
                blend_mode=blend_mode,
                size_over_life=sol,
                alpha_over_life=aol,
                color_over_life=col_ol,
            )
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
