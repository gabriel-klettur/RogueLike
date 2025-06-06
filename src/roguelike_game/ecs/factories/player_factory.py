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

# Valores por defecto (se usan si no se especifica en PLAYER_STATS)
DEFAULT_CLASS = _player_cfg.get("DEFAULT_CLASS", "dwarf")
DEFAULT_SCALE = _player_cfg.get("DEFAULT_SCALE", 1.0)
DEFAULT_SPEED = _player_cfg.get("DEFAULT_SPEED", 5)
ANIMATION_INTERVAL = _player_cfg.get("ANIMATION_INTERVAL", 0.15)
INITIAL_ANIMATION_STATE = _player_cfg.get("INITIAL_ANIMATION_STATE", "down_idle")
MELEE_WEAPON_CFG = _player_cfg.get("MELEE_WEAPON", {})
DEFAULT_TRAIL = _player_cfg.get("DEFAULT_TRAIL", {})
FEET_WIDTH_DIVISOR = _player_cfg.get("FEET_WIDTH_DIVISOR", 2)
FEET_HEIGHT_DIVISOR = _player_cfg.get("FEET_HEIGHT_DIVISOR", 4)


# --------------------------------------------
# Funciones auxiliares internas
# --------------------------------------------

def _load_and_scale_sprites(class_player: str) -> dict[str, dict[str, list[pygame.Surface]]]:
    """
    Carga los sprites de la clase indicada y, si corresponde, los escala según el factor 'scale'.
    
    Args:
        class_player: Identificador de la clase (p.ej. "dwarf", "human", etc.)
    
    Retorna:
        sprites_dict: Diccionario anidado con la estructura:
            {
                "down": {"idle": [Surface, ...], "walk": [Surface, ...]},
                "up":   {"idle": [...], "walk": [...]},
                "left": {...},
                "right": {...}
            }
    """
    # 1) Obtener sprites brutos desde PlayerAssets
    sprites_dict, _ = PlayerAssets(class_player, ORIGINAL_SPRITE_SIZE).get_sprites()

    # 2) Determinar factor de escala (override por clase en PLAYER_STATS)
    scale_factor = PLAYER_STATS.get(class_player, {}).get("scale", DEFAULT_SCALE)

    # 3) Si hay que escalar, recorrer todas las animaciones y frames
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
    down_idle_frames = sprites_dict.get("down", {}).get("idle", [])
    if down_idle_frames:
        return down_idle_frames[0]
    return None


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
        idle_frames = states.get("idle", [])
        walk_frames = states.get("walk", [])
        anim_map[f"{direction}_idle"] = idle_frames
        anim_map[f"{direction}_walk"] = walk_frames
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
    # ——— Body ———
    mascara = pygame.mask.from_surface(sprite_surface)
    body_collider = MaskCollider(mascara, offset_x=0, offset_y=0)

    # ——— Feet ———
    w_img, h_img = sprite_surface.get_size()
    feet_width = w_img // FEET_WIDTH_DIVISOR
    feet_height = h_img // FEET_HEIGHT_DIVISOR

    # Centrar horizontalmente:
    offset_x = (w_img - feet_width) // 2
    # Alinear verticalmente en la base del sprite:
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

    # 6.1) Cargar y escalar todos los sprites de la clase
    sprites_dict = _load_and_scale_sprites(class_player)

    # 6.2) Sprite inicial estático: primer frame 'down_idle'
    initial_frame = _extract_initial_frame(sprites_dict)
    if initial_frame:
        world.components["Sprite"][eid] = Sprite(initial_frame)

    # 6.3) Construir el mapa de animaciones para Animator
    anim_map = _build_animator_map(sprites_dict)
    world.components["Animator"][eid] = Animator(
        animations=anim_map,
        current_state=INITIAL_ANIMATION_STATE,
    )

    # 6.4) Temporizador para controlar velocidad de animación
    world.components["AnimationTimer"][eid] = AnimationTimer(
        last_time=time.time(),
        interval=ANIMATION_INTERVAL,
    )

    # ------------------------------------------------
    # 7) Movimiento: velocidad y vector de velocidad
    # ------------------------------------------------

    # 7.1) Obtener velocidad desde PLAYER_STATS o usar DEFAULT_SPEED
    speed_value = PLAYER_STATS.get(class_player, {}).get("speed", DEFAULT_SPEED)
    world.components["MovementSpeed"][eid] = MovementSpeed(speed_value)

    # 7.2) Vector de velocidad inicial (0,0)
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

    # 9.1) Salud máxima desde PLAYER_STATS o 100 por defecto
    max_hp = PLAYER_STATS.get(class_player, {}).get("max_health", 100)
    world.components["Health"][eid] = Health(max_hp, max_hp)

    # 9.2) Estadísticas base de combate: (health, max_health, attack, defense)
    #      Por simplicidad se usan valores fijos para ataque y defensa.
    world.components["CombatStats"][eid] = CombatStats(
        current_hp=max_hp,
        max_hp=max_hp,
        power=PLAYER_STATS.get(class_player, {}).get("attack", 1),
        defense=PLAYER_STATS.get(class_player, {}).get("defense", 0),
    )

    # ------------------------------------------------
    # 10) Arma cuerpo a cuerpo
    # ------------------------------------------------

    world.components["MeleeWeapon"][eid] = MeleeWeapon(
        damage=MELEE_WEAPON_CFG.get("damage", 1),
        cooldown=MELEE_WEAPON_CFG.get("cooldown", 1.0),
    )

    # ------------------------------------------------
    # 11) Efecto visual: Trail de sombra
    # ------------------------------------------------

    trail_params = PLAYER_STATS.get(class_player, {}).get("trail", {})
    trail_cfg = TrailConfig(
        interval=trail_params.get("interval", DEFAULT_TRAIL.get("interval", 0.1)),
        life_time=trail_params.get("life_time", DEFAULT_TRAIL.get("life_time", 0.5)),
        max_trails=trail_params.get("max_trails", DEFAULT_TRAIL.get("max_trails", 10)),
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
    down_idle_frames = sprites_dict.get("down", {}).get("idle", [])

    if not down_idle_frames:
        # Si no existen sprites, usar esquina superior del tile
        px = tile_x * TILE_SIZE
        py = tile_y * TILE_SIZE
    else:
        # Obtener el primer frame para medidas
        first_frame = down_idle_frames[0]
        w_img, h_img = first_frame.get_size()

        # Altura del collider de pies según divisor de configuración
        feet_height = h_img // FEET_HEIGHT_DIVISOR
        half_feet = feet_height // 2

        # Centro del tile en píxeles
        cx = tile_x * TILE_SIZE + TILE_SIZE // 2
        cy = tile_y * TILE_SIZE + TILE_SIZE // 2

        # Calcular posición x,y de la esquina superior izquierda del sprite
        px = cx - (w_img // 2)
        py = cy - (h_img - half_feet)

    # 2) Delegar a spawn_player con las coordenadas en píxeles calculadas
    return spawn_player(world, px, py, class_player)
