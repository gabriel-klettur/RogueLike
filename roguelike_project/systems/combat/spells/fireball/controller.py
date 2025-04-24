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
        self.m = model
        self.tiles = tiles
        self.enemies = enemies
        # override del callback para agregar la explosion
        def _explode_callback(ex, ey):
            # Crear explosión en la posición de impacto
            self.m.explosion = FireExplosion(ex, ey)
            explosions_list.add_explosion(self.m.explosion)
        self.m.on_explode = _explode_callback

    def update(self):
        m = self.m
        if not m.alive:
            return

        # Mover
        m.x += m.dx
        m.y += m.dy
        m.age += 1

        # Vida
        if m.age >= m.lifespan:
            m.alive = False
            return

        # Colisión con tiles sólidos
        rect = Rect(m.x, m.y, *m.size)
        for t in self.tiles:
            if t.solid and rect.colliderect(t.rect):
                m.on_explode(m.x, m.y)
                m.alive = False
                return

        # Colisión con enemigos por máscara
        for e in self.enemies:
            if not hasattr(e, 'mask') or not e.alive:
                continue
            # Offset de máscara: posición relativa
            offset = (int(e.x - m.x), int(e.y - m.y))
            if m.mask.overlap(e.mask, offset):
                e.take_damage(m.damage)
                m.on_explode(m.x, m.y)
                m.alive = False
                return
