"""
Module: player_factory.py
Builder para crear la entidad jugador con todos sus componentes ECS.
"""

import time
import json
from pathlib import Path

import pygame

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.camera_follow import CameraFollowComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.rendering.trail_component import TrailComponent, TrailConfig
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.systems.config_z_layer import Z_LAYERS
from roguelike_game.ecs.assets.player_assets import PlayerAssets
from roguelike_engine.config.config_tiles import TILE_SIZE

# --------------------------------------------
# Configuración global: carga de JSON y constantes
# --------------------------------------------

# Ruta absoluta al JSON con la configuración de jugadores
_config_path = Path(__file__).resolve().parents[4] / "data" / "players.json"

# Cargar configuración solo una vez
with open(_config_path, encoding="utf-8") as _f:
    _player_cfg = json.load(_f)

# Tamaño original y renderizado de sprites
ORIGINAL_SPRITE_SIZE = tuple(_player_cfg["ORIGINAL_SPRITE_SIZE"])
RENDERED_SPRITE_SIZE = tuple(_player_cfg["RENDERED_SPRITE_SIZE"])

# Estadísticas detalladas por clase de jugador
PLAYER_STATS = _player_cfg["PLAYER_STATS"]

# Valores por defecto (ahora obligatorios en el JSON)
DEFAULT_CLASS = _player_cfg["DEFAULT_CLASS"]
DEFAULT_SCALE = _player_cfg["DEFAULT_SCALE"]
DEFAULT_SPEED = _player_cfg["DEFAULT_SPEED"]
ANIMATION_INTERVAL = _player_cfg["ANIMATION_INTERVAL"]
INITIAL_ANIMATION_STATE = _player_cfg["INITIAL_ANIMATION_STATE"]
MELEE_WEAPON_CFG = _player_cfg["MELEE_WEAPON"]
DEFAULT_TRAIL = _player_cfg["DEFAULT_TRAIL"]
FEET_WIDTH_DIVISOR = _player_cfg["FEET_WIDTH_DIVISOR"]
FEET_HEIGHT_DIVISOR = _player_cfg["FEET_HEIGHT_DIVISOR"]


# --------------------------------------------
# Funciones auxiliares internas
# --------------------------------------------

def _load_and_scale_sprites(class_player: str) -> dict[str, dict[str, list[pygame.Surface]]]:
    """
    Carga los sprites de la clase indicada y, si corresponde, los escala según el factor 'scale'.
    
    Args:
        class_player: Identificador de la clase (p.ej. "dwarf", "valkyrie", etc.)
    
    Retorna:
        sprites_dict: Diccionario anidado con la estructura:
            {
                "down": {"idle": [Surface, ...], "walk": [Surface, ...]},
                "up":   {"idle": [...], "walk": [...]},
                "left": {...},
                "right": {...}
            }
    """
    sprites_dict, _ = PlayerAssets(class_player, ORIGINAL_SPRITE_SIZE).get_sprites()

    # Determinar factor de escala (obligatorio en PLAYER_STATS)
    scale_factor = PLAYER_STATS[class_player]["scale"]

    if scale_factor != 1.0:
        for direction, anims in sprites_dict.items():
            for state, frames in anims.items():
                scaled_frames: list[pygame.Surface] = []
                for frame in frames:
                    nuevo_ancho = int(frame.get_width() * scale_factor)
                    nuevo_alto = int(frame.get_height() * scale_factor)
                    scaled = pygame.transform.scale(frame, (nuevo_ancho, nuevo_alto))
                    scaled_frames.append(scaled)
                sprites_dict[direction][state] = scaled_frames

    return sprites_dict


def _extract_initial_frame(sprites_dict: dict[str, dict[str, list[pygame.Surface]]]) -> pygame.Surface | None:
    """
    Obtiene el primer fotograma de la animación 'down_idle', si existe.
    Sirve como sprite inicial estático.
    """
    down_idle_frames = sprites_dict["down"]["idle"]
    return down_idle_frames[0] if down_idle_frames else None


def _build_animator_map(sprites_dict: dict[str, dict[str, list[pygame.Surface]]]) -> dict[str, list[pygame.Surface]]:
    """
    Construye un diccionario plano de animaciones para Animator, con clave "<dirección>_<estado>".
    
    Ejemplo de salida:
        {
            "down_idle":  [...],
            "down_walk":  [...],
            "up_idle":    [...],
            "up_walk":    [...],
            "left_idle":  [...],
            "left_walk":  [...],
            "right_idle": [...],
            "right_walk": [...],
        }
    """
    anim_map: dict[str, list[pygame.Surface]] = {}
    for direction, states in sprites_dict.items():
        anim_map[f"{direction}_idle"] = states["idle"]
        anim_map[f"{direction}_walk"] = states["walk"]
    return anim_map


def _create_body_and_feet(sprite_surface: pygame.Surface) -> MultiCollider:
    """
    Genera un MultiCollider que contiene:
      - "body": MaskCollider basado en la máscara de píxeles opacos del sprite.
      - "feet": Collider rectangular en la parte inferior del sprite.

    Args:
        sprite_surface: Surface de pygame con la imagen del jugador.

    Retorna:
        MultiCollider({"body": MaskCollider, "feet": Collider})
    """
    # Body
    mascara = pygame.mask.from_surface(sprite_surface)
    body_collider = MaskCollider(mascara, offset_x=0, offset_y=0)

    # Feet
    w_img, h_img = sprite_surface.get_size()
    feet_width = w_img // FEET_WIDTH_DIVISOR
    feet_height = h_img // FEET_HEIGHT_DIVISOR

    offset_x = (w_img - feet_width) // 2
    offset_y = h_img - feet_height

    feet_collider = Collider(feet_width, feet_height, offset_x, offset_y)

    return MultiCollider({"body": body_collider, "feet": feet_collider})


# --------------------------------------------
# Funciones principales: spawn_player y spawn_player_tile
# --------------------------------------------

def spawn_player(world, x: int, y: int, class_player: str = DEFAULT_CLASS) -> int:
    """
    Crea la entidad jugador y añade todos los componentes ECS necesarios.

    Args:
        world: instancia de ECSWorld.
        x, y: coordenadas iniciales en píxeles donde se ubica al jugador.
        class_player: Identificador de la clase/asset del jugador (por defecto DEFAULT_CLASS).

    Retorna:
        eid (int): ID de la entidad recién creada.
    """
    # 1) Crear entidad vacía
    eid = world.create_entity()

    # 2) Componente Position
    world.components["Position"][eid] = Position(x, y)

    # 3) Etiqueta de jugador (PlayerTag) y cámara (CameraFollow)
    world.components["PlayerTagComponent"][eid] = PlayerTagComponent()
    world.components["CameraFollowComponent"][eid] = CameraFollowComponent()

    # 4) Componente de entrada para el jugador
    world.components["InputComponent"][eid] = InputComponent()

    # 5) Capa Z para renderizado (asegura orden correcto en pantalla)
    world.components["ZLayer"][eid] = ZLayer(Z_LAYERS["player"])

    # ------------------------------------------------
    # 6) Sprites y animaciones
    # ------------------------------------------------

    sprites_dict = _load_and_scale_sprites(class_player)

    initial_frame = _extract_initial_frame(sprites_dict)
    if initial_frame:
        world.components["Sprite"][eid] = Sprite(initial_frame)

    anim_map = _build_animator_map(sprites_dict)
    world.components["Animator"][eid] = Animator(
        animations=anim_map,
        current_state=INITIAL_ANIMATION_STATE,
    )

    world.components["AnimationTimer"][eid] = AnimationTimer(
        last_time=time.time(),
        interval=ANIMATION_INTERVAL,
    )

    # ------------------------------------------------
    # 7) Movimiento: velocidad y vector de velocidad
    # ------------------------------------------------

    speed_value = PLAYER_STATS[class_player]["speed"]
    world.components["MovementSpeed"][eid] = MovementSpeed(speed_value)

    world.components["Velocity"][eid] = Velocity(0, 0)

    # ------------------------------------------------
    # 8) Colisiones: cuerpo (mask) y pies (rect)
    # ------------------------------------------------

    if initial_frame:
        multi_collider = _create_body_and_feet(initial_frame)
        world.components["MultiCollider"][eid] = multi_collider

    # ------------------------------------------------
    # 9) Salud y estadísticas de combate
    # ------------------------------------------------

    max_hp = PLAYER_STATS[class_player]["max_health"]
    world.components["Health"][eid] = Health(max_hp, max_hp)

    # Si attack y defense están definidos en PLAYER_STATS, úsalos; 
    # de lo contrario, asumimos valores mínimos (1 y 0).
    attack_value = PLAYER_STATS[class_player]["attack"]
    defense_value = PLAYER_STATS[class_player]["defense"]
    world.components["CombatStats"][eid] = CombatStats(
        current_hp=max_hp,
        max_hp=max_hp,
        power=attack_value,
        defense=defense_value,
    )

    # ------------------------------------------------
    # 10) Arma cuerpo a cuerpo
    # ------------------------------------------------

    world.components["MeleeWeapon"][eid] = MeleeWeapon(
        damage=MELEE_WEAPON_CFG["damage"],
        cooldown=MELEE_WEAPON_CFG["cooldown"],
    )

    # ------------------------------------------------
    # 11) Efecto visual: Trail de sombra
    # ------------------------------------------------

    # Tomamos directamente del JSON: primero buscamos en la clase,
    # si no estuviera, heredamos de DEFAULT_TRAIL (forzando que esté definido allí).
    trail_params = PLAYER_STATS[class_player].get("trail", DEFAULT_TRAIL)
    trail_cfg = TrailConfig(
        interval=trail_params["interval"],
        life_time=trail_params["life_time"],
        max_trails=trail_params["max_trails"],
    )
    world.components["TrailComponent"][eid] = TrailComponent(config=trail_cfg)

    return eid


def spawn_player_tile(world, tile_x: int, tile_y: int, class_player: str = DEFAULT_CLASS) -> int:
    """
    Crea la entidad jugador usando coordenadas de tile en lugar de píxeles.
    Calcula la posición de píxeles para alinear el collider 'feet' al centro del tile.

    Args:
        world: instancia de ECSWorld.
        tile_x, tile_y: coordenadas en tiles (int).
        class_player: clase/asset del jugador.

    Retorna:
        eid (int): ID de la entidad creada.
    """
    # 1) Cargar sprites sin escalar (solo necesitamos el primer frame 'down_idle' para medir)
    sprites_dict, _ = PlayerAssets(class_player, ORIGINAL_SPRITE_SIZE).get_sprites()
    down_idle_frames = sprites_dict["down"]["idle"]

    if not down_idle_frames:
        # Si no existen sprites, usar esquina superior del tile
        px = tile_x * TILE_SIZE
        py = tile_y * TILE_SIZE
    else:
        first_frame = down_idle_frames[0]
        w_img, h_img = first_frame.get_size()

        feet_height = h_img // FEET_HEIGHT_DIVISOR
        half_feet = feet_height // 2

        cx = tile_x * TILE_SIZE + TILE_SIZE // 2
        cy = tile_y * TILE_SIZE + TILE_SIZE // 2

        px = cx - (w_img // 2)
        py = cy - (h_img - half_feet)

    return spawn_player(world, px, py, class_player)
