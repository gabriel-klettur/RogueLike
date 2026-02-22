import os
import math
from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.managers.map.item_drop_manager import ItemDropManager


class OrbAttractionSystem:
    """
    Atrae orbes de experiencia al jugador y las absorbe al contacto.
    """
    def __init__(self, perf_log=None, items_path=None, attract_radius: float = 100.0, speed: float = 5.0):
        self.perf_log = perf_log
        # Load items from SQLite
        self.items, _assets = ItemsLoader().load()
        self.attract_radius = attract_radius
        self.speed = speed
        # Gestor de drops en mapa para persistir orbes recogidos
        path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.drop_manager = ItemDropManager(path)

    def update(self, world, *args):
        comps = world.components
        positions = comps.get('Position', {})
        phys_items = comps.get('PhysicalItemComponent', {})
        collectibles = comps.get('CollectibleComponent', {})
        xp_comps = comps.get('ExperienceComponent', {})
        player_tags = comps.get('PlayerTagComponent', {})

        # Encontrar entidad jugador
        player_eid = next(iter(player_tags), None)
        if player_eid is None:
            return
        player_pos = positions.get(player_eid)
        xp_comp = xp_comps.get(player_eid)
        if not player_pos or not xp_comp:
            return

        for eid, phys in list(phys_items.items()):
            if eid not in collectibles:
                continue
            model = self.items.get(phys.item_id)
            if not model:
                continue
            exp_value = getattr(model, 'experience', None) or 0
            if exp_value <= 0:
                continue
            orb_pos = positions.get(eid)
            if not orb_pos:
                continue
            dx = player_pos.x - orb_pos.x
            dy = player_pos.y - orb_pos.y
            dist_sq = dx * dx + dy * dy
            if dist_sq <= self.attract_radius ** 2:
                dist = math.sqrt(dist_sq) if dist_sq > 0 else 0
                # Mover el orbe hacia el jugador
                if dist > 0:
                    vx = dx / dist * self.speed
                    vy = dy / dist * self.speed
                    orb_pos.x += vx
                    orb_pos.y += vy
                # Absorber si está lo suficientemente cerca
                if dist <= self.speed:
                    qty = getattr(phys, 'quantity', 1)
                    exp_value = model.experience or 0
                    total_exp = qty * exp_value
                    xp_comp.xp += total_exp
                    # Subir de nivel si es necesario
                    while xp_comp.xp >= xp_comp.xp_to_next_level:
                        xp_comp.xp -= xp_comp.xp_to_next_level
                        xp_comp.level += 1
                    # Persistir eliminación del orbe recogido
                    self.drop_manager.pick_up(phys.drop_id)
                    world.remove_entity(eid)
