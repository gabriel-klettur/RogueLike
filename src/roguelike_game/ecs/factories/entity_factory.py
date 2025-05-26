import json
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from ..components.position import Position
from ..components.sprite import Sprite
from ..components.health import Health
from ..components.movement_speed import MovementSpeed
from ..components.scale import Scale
from ..components.velocity import Velocity
from ..components.patrol import Patrol
from ..components.animator import Animator
from ..components.multi_collider import MultiCollider
from ..components.mask_collider import MaskCollider
from ..components.collider import Collider
from ..components.identity import Identity, Faction
from ..components.z_layer import ZLayer
from roguelike_game.systems.config_z_layer import Z_LAYERS

_defs = json.load(open("data/monsters.json", "r"))
# Caches vacíos hasta inicialización
_SPRITE_SURFACES = {}
_DEATH_SURFACES = {}
_caches_loaded = False

def _load_caches_once():
    global _caches_loaded
    if _caches_loaded:
        return
    for mtype, cfg in _defs.items():
        # Sprites por dirección
        dir_map = {}
        for d, path in cfg["sprites"].items():
            surf = pygame.image.load(path).convert_alpha()
            # Pre-scale sprite surface based on config
            scale_val = cfg.get("scale", 1.0)
            if scale_val != 1.0:
                w, h = surf.get_size()
                surf = pygame.transform.scale(surf, (int(w * scale_val), int(h * scale_val)))
            dir_map[d] = surf
        _SPRITE_SURFACES[mtype] = dir_map
        # Death sprite opcional
        dpath = cfg.get("death_sprite")
        if dpath:
            ds = pygame.image.load(dpath).convert_alpha()
            if (scl := cfg.get("death_scale")):
                w,h = ds.get_size(); ds = pygame.transform.scale(ds,(int(w*scl),int(h*scl)))
            _DEATH_SURFACES[mtype] = ds
        else:
            _DEATH_SURFACES[mtype] = None
    _caches_loaded = True

def spawn_monster(world, monster_type: str, tile_x: int, tile_y: int):
    """
    Crea una entidad según la entrada monster_type de monsters.json
    """
    # Asegurar que el display está inicializado antes de cargar imágenes
    _load_caches_once()
    cfg = _defs[monster_type]
    eid = world.create_entity()

    # 1) Sprite principal y DeathImage desde caché
    base_map = _SPRITE_SURFACES.get(monster_type, {})
    sprite = Sprite(base_map.get("down", {}).copy())
    dsurf = _DEATH_SURFACES.get(monster_type)
    if dsurf:
        sprite.death_image = dsurf
    world.components["Sprite"][eid] = sprite

    # 2) Posición válida sobre mapa
    #    Reutiliza el método find_valid_spawn de tu world
    tx, ty = tile_x, tile_y
    # Debug: almacenar tile exacto de spawn para dibujar marcador
    if not hasattr(world, 'spawn_tiles'):
        world.spawn_tiles = []
    world.spawn_tiles.append((tx, ty, eid))  # incluir NPC id para dibujar número
    # Calcular bottom-center del sprite escalado en el tile
    scale_val = cfg["scale"]
    orig_w, orig_h = sprite.image.get_size()
    w_s = int(orig_w * scale_val)
    h_s = int(orig_h * scale_val)
    px = tx * TILE_SIZE + (TILE_SIZE - w_s) // 2
    py = (ty + 1) * TILE_SIZE - h_s    

    world.components["Position"][eid] = Position(px, py)

    # 3) Patrol + Animator: usar caché de sprites pre-cargados
    sprites = {d: [surf.copy()] for d, surf in _SPRITE_SURFACES.get(monster_type, {}).items()}
    patrol = Patrol((px, py), sprites_by_direction=sprites)
    # default_sprite debe ser un Surface, usamos el primer frame
    patrol.default_sprite = sprites.get("down", [])[0]
    world.components["Patrol"][eid] = patrol
    world.components["MovementSpeed"][eid] = MovementSpeed(speed=cfg["speed"])
    world.components["Animator"][eid] = Animator(animations=sprites, current_state="down")

    # 4) Scale, Velocity
    world.components["Scale"][eid] = Scale(scale=cfg["scale"])
    world.components["Velocity"][eid] = Velocity(0, 0)

    # 5) Colliders (cuerpo + pies)
    #    Reusa tu lógica actual de máscara + rect
    mask_surf = sprite.image
    scale_v = cfg["scale"]
    if scale_v != 1.0:
        mask_surf = pygame.transform.scale(
            mask_surf, 
            (int(mask_surf.get_width()*scale_v), int(mask_surf.get_height()*scale_v))
        )
    body = MaskCollider(pygame.mask.from_surface(mask_surf), 0, 0)
    w, h = mask_surf.get_size()
    feet = Collider(int(w*0.5), int(h*0.2), (w - int(w*0.5))//2, h - int(h*0.2))
    world.components["MultiCollider"][eid] = MultiCollider({"body": body, "feet": feet})

    # 6) ZLayer
    faction = getattr(Faction, cfg["faction"])
    world.components["ZLayer"][eid] = ZLayer(Z_LAYERS["monster"])

    # 7) Health & Identity
    world.components["Health"][eid] = Health(cfg["hp"], cfg["hp"])
    world.components["Scale"][eid] = Scale(cfg["scale"])
    world.components["Identity"][eid] = Identity(
        id=eid,
        name=monster_type.capitalize(),
        title="",
        faction=faction,
    )

    return eid