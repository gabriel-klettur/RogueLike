
class MeleeCombatSystem:
    """
    Sistema que procesa eventos de WantsToMelee y aplica daño.
    """
    def update(self, world):
        for eid, evt in list(world.components['WantsToMelee'].items()):
            atk = world.components['CombatStats'][evt.attacker]
            defn = world.components['CombatStats'][evt.target]
            weapon = world.components['MeleeWeapon'].get(evt.attacker)
            extra = weapon.damage if weapon else 0
            dmg = max(0, atk.power + extra - defn.defense)
            defn.current_hp -= dmg
            # Limpia el evento
            del world.components['WantsToMelee'][eid]
