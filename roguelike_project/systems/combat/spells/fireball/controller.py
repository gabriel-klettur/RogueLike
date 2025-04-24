# roguelike_project/systems/combat/spells/fireball/controller.py
from pygame import Rect
from roguelike_project.systems.combat.spells.fireball.model import FireballModel
from roguelike_project.systems.combat.view.effects.particles.explosions.fire import FireExplosion

class FireballController:
    """
    Actualiza la posición del fireball, maneja colisiones y
    genera la explosión en el punto de impacto.
    """
    def __init__(
        self,
        model: FireballModel,
        tiles: list,
        enemies: list,
        explosions_list
    ):
        self.model = model
        self.tiles = tiles
        self.enemies = enemies
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

        # Colisión con tiles sólidos
        rect = Rect(model.x, model.y, *model.size)
        for t in self.tiles:
            if t.solid and rect.colliderect(t.rect):
                model.on_explode(model.x, model.y)
                model.alive = False
                return

        # Colisión con enemigos por máscara
        for e in self.enemies:
            if not hasattr(e, 'mask') or not e.alive:
                continue
            # Offset de máscara: posición relativa
            offset = (int(e.x - model.x), int(e.y - model.y))
            if model.mask.overlap(e.mask, offset):
                e.take_damage(model.damage)
                model.on_explode(model.x, model.y)
                model.alive = False
                return
