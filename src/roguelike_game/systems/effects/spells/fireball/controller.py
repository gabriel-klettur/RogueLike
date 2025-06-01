# Path: src/roguelike_game/systems/effects/spells/fireball/controller.py
from pygame import Rect
from roguelike_game.systems.effects.spells.fireball.model import FireballModel
from roguelike_game.systems.effects.explosions.fire import FireExplosion

class FireballController:
    """
    Actualiza la posición del fireball, maneja colisiones y
    genera la explosión en el punto de impacto.
    """
    def __init__(
        self,
        model: FireballModel,
        tiles: list,
        explosions_list,
        ecs_world
    ):
        self.model = model
        self.tiles = tiles
        self.ecs_world = ecs_world
        # override del callback para agregar la explosion
        def _explode_callback(ex, ey):
            # Crear explosión en la posición de impacto
            self.model.explosion = FireExplosion(ex, ey)
            explosions_list.add_explosion(self.model.explosion)
        self.model.on_explode = _explode_callback

    def update(self):
        model = self.model
        if not model.alive:
            return

        # Mover
        model.x += model.dx
        model.y += model.dy
        model.age += 1

        # Vida
        if model.age >= model.lifespan:
            model.alive = False
            return

        # Colisión con NPCs (pixel-perfect vs bbox según DEBUG)
        rect = Rect(model.x, model.y, *model.size)
        for eid in self.ecs_world.get_entities_with('Position','MultiCollider','Health'):
            multi = self.ecs_world.components['MultiCollider'][eid]
            body = multi.colliders.get('body')
            if body:
                pos = self.ecs_world.components['Position'][eid]
                # pixel-perfect si existe máscara
                if hasattr(body, 'mask'):
                    offset = (
                        int(pos.x + body.offset_x - model.x),
                        int(pos.y + body.offset_y - model.y)
                    )
                    if model.mask.overlap(body.mask, offset):
                        hp = self.ecs_world.components['Health'][eid]
                        hp.current_hp -= model.damage
                        if hp.current_hp < 0: hp.current_hp = 0
                        model.on_explode(model.x, model.y)
                        model.alive = False
                        return
                else:
                    # bounding box collision
                    w = body.width
                    h = body.height
                    br = Rect(pos.x + body.offset_x, pos.y + body.offset_y, w, h)
                    if rect.colliderect(br):
                        hp = self.ecs_world.components['Health'][eid]
                        hp.current_hp -= model.damage
                        if hp.current_hp < 0: hp.current_hp = 0
                        model.on_explode(model.x, model.y)
                        model.alive = False
                        return

        # Colisión con tiles sólidos (optimized)
        rect = Rect(model.x, model.y, *model.size)
        # Spatial query: solo rects cercanos
        nearby = self.ecs_world.get_solid_tiles_for_rect(rect)
        if nearby and rect.collidelist(nearby) != -1:
            model.on_explode(model.x, model.y)
            model.alive = False
            return
