# Path: src/roguelike_game/systems/combat/spells/fireball/controller.py
from pygame import Rect
from roguelike_game.systems.combat.spells.fireball.model import FireballModel
from roguelike_game.systems.combat.explosions.fire import FireExplosion

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
        npc_world
    ):
        self.model = model
        self.tiles = tiles
        self.npc_world = npc_world
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

        # Colisión con NPCs
        rect = Rect(model.x, model.y, *model.size)
        for eid in self.npc_world.get_entities_with('Position','MultiCollider','Health'):
            multi = self.npc_world.components['MultiCollider'][eid]
            body = multi.colliders.get('body')
            if body:
                pos = self.npc_world.components['Position'][eid]
                # Si es MaskCollider, usar mask.get_size(), si no, usar width/height
                if hasattr(body, 'mask'):
                    w, h = body.mask.get_size()
                else:
                    w, h = body.width, body.height
                br = Rect(pos.x + body.offset_x, pos.y + body.offset_y, w, h)
                if rect.colliderect(br):
                    hp = self.npc_world.components['Health'][eid]
                    hp.current_hp -= model.damage
                    if hp.current_hp < 0: hp.current_hp = 0
                    model.on_explode(model.x, model.y)
                    model.alive = False
                    return

        # Colisión con tiles sólidos
        rect = Rect(model.x, model.y, *model.size)
        for t in self.tiles:
            if t.solid and rect.colliderect(t.rect):
                model.on_explode(model.x, model.y)
                model.alive = False
                return
