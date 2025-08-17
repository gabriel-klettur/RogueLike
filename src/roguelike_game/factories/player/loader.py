"""
Carga y prepara sprites para el jugador.
"""
import pygame
import importlib
from roguelike_game.factories.player.assets.player_assets import PlayerAssets
import roguelike_game.factories.player.config as player_cfg


def _norm_scale(val) -> float:
    """Normaliza un valor de escala: None o inválido -> 1.0; <=0 -> 1.0."""
    try:
        f = float(val)
        return f if f > 0 else 1.0
    except Exception:
        return 1.0


def _scale_frames(frames: list[pygame.Surface], factor: float) -> list[pygame.Surface]:
    if factor == 1.0:
        return frames
    out: list[pygame.Surface] = []
    for frame in frames:
        w, h = frame.get_width(), frame.get_height()
        out.append(pygame.transform.scale(frame, (int(w * factor), int(h * factor))))
    return out


def load_and_scale_sprites(class_player: str) -> dict[str, dict[str, list[pygame.Surface]]]:
    """
    Carga y escala sprites según configuración.
    """
    # Reload player config each time to reflect editor changes
    importlib.reload(player_cfg)
    sprites_dict, _ = PlayerAssets(class_player, player_cfg.ORIGINAL_SPRITE_SIZE).get_sprites()
    # Escala base + escala por estado desde metadata de assets
    base_scale = _norm_scale(player_cfg.DEFAULT_SCALE)
    assets_entry = player_cfg.PLAYER_ASSETS.get(class_player)
    active = "sets"
    meta_states: dict[str, float] = {}
    if isinstance(assets_entry, dict):
        active = assets_entry.get("active_set", "sets")
        if active == "sets":
            meta = assets_entry.get("sets", {}).get("sprites_data_set", {})
        else:
            meta = assets_entry.get("no-sets", {}).get("sprites_data_no-set", {})
        # Construir mapa de escalas por estado (idle, walk, ...)
        meta_states = {k.removeprefix("scale_"): _norm_scale(v)
                       for k, v in meta.items() if k.startswith("scale_")}

    for direction, anims in sprites_dict.items():
        for state, frames in anims.items():
            state_scale = meta_states.get(state, 1.0)
            factor = base_scale * state_scale
            if factor != 1.0 and frames:
                sprites_dict[direction][state] = _scale_frames(frames, factor)
    return sprites_dict


def extract_initial_frame(sprites_dict: dict[str, dict[str, list[pygame.Surface]]]) -> pygame.Surface | None:
    """
    Primer fotograma de 'down_idle'.
    """
    frames = sprites_dict.get("down", {}).get("idle", [])
    return frames[0] if frames else None


def build_animator_map(sprites_dict: dict[str, dict[str, list[pygame.Surface]]]) -> dict[str, list[pygame.Surface]]:
    """
    Diccionario plano para Animator.
    """
    anim_map: dict[str, list[pygame.Surface]] = {}
    for direction, states in sprites_dict.items():
        anim_map[f"{direction}_idle"] = states.get("idle", [])
        anim_map[f"{direction}_walk"] = states.get("walk", [])
    return anim_map


def build_masks_map(sprites_dict: dict[str, dict[str, list[pygame.Surface]]]) -> dict[str, list[pygame.mask.Mask]]:
    """
    Precalcula máscaras para cada frame ya escalado.
    Claves igual que build_animator_map.
    """
    masks_map: dict[str, list[pygame.mask.Mask]] = {}
    for direction, states in sprites_dict.items():
        idle_frames = states.get("idle", [])
        walk_frames = states.get("walk", [])
        masks_map[f"{direction}_idle"] = [pygame.mask.from_surface(f) for f in idle_frames]
        masks_map[f"{direction}_walk"] = [pygame.mask.from_surface(f) for f in walk_frames]
    return masks_map
