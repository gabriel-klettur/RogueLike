"""
Carga y prepara sprites para el jugador.
"""
import pygame
from roguelike_game.factories.player.assets.player_assets import PlayerAssets
from roguelike_game.factories.player.config import ORIGINAL_SPRITE_SIZE, PLAYER_STATS, DEFAULT_SCALE


def load_and_scale_sprites(class_player: str) -> dict[str, dict[str, list[pygame.Surface]]]:
    """
    Carga y escala sprites según configuración.
    """
    sprites_dict, _ = PlayerAssets(class_player, ORIGINAL_SPRITE_SIZE).get_sprites()
    scale_factor = PLAYER_STATS[class_player]["scale"]
    if scale_factor != DEFAULT_SCALE:
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
