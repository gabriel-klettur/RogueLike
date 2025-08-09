"""
Carga y prepara sprites para el jugador.
"""
import pygame
import importlib
from roguelike_game.factories.player.assets.player_assets import PlayerAssets
import roguelike_game.factories.player.config as player_cfg


def load_and_scale_sprites(class_player: str) -> dict[str, dict[str, list[pygame.Surface]]]:
    """
    Carga y escala sprites según configuración.
    """
    # Reload player config each time to reflect editor changes
    importlib.reload(player_cfg)
    sprites_dict, _ = PlayerAssets(class_player, player_cfg.ORIGINAL_SPRITE_SIZE).get_sprites()
    # Usar DEFAULT_SCALE del JSON raíz
    scale_factor = player_cfg.DEFAULT_SCALE
    if scale_factor != player_cfg.DEFAULT_SCALE:
        for direction, anims in sprites_dict.items():
            for state, frames in anims.items():
                sprites_dict[direction][state] = [pygame.transform.scale(
                    frame,
                    (int(frame.get_width()*scale_factor), int(frame.get_height()*scale_factor))
                ) for frame in frames]
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
