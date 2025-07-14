from roguelike_game.ecs.components.items.item_component import ItemComponent
from roguelike_game.ecs.components.items.teleport_component import TeleportComponent
from roguelike_game.ecs.components.items.healing_component import HealingComponent
from roguelike_game.ecs.components.items.buff_component import BuffComponent
from roguelike_game.ecs.components.transform.position import Position


class ItemFactory:
    """
    Fábrica de entidades de ítems según instancia de ítem.
    """
    @staticmethod
    def create(world, instance_id: str, instance_data: dict, items: dict):
        """
        Crea una entidad ítem en ECS.
        :param world: ECSWorld
        :param instance_id: identificador único de instancia
        :param instance_data: datos de instancia validados (item_id, params, position/tile, schema_version)
        :param items: diccionario de modelos de ítems cargados (id -> modelo Pydantic)
        :return: entity id creado
        """
        item_id = instance_data['item_id']
        # Crear entidad
        eid = world.create_entity()
        # Componente de item
        world.components['ItemComponent'][eid] = ItemComponent(item_id)
        # Componente de posición
        pos_data = instance_data.get('position') or instance_data.get('tile')
        world.components['Position'][eid] = Position(pos_data['x'], pos_data['y'])
        # Componentes específicos según params
        params = instance_data.get('params', {})
        if 'dest_map' in params:
            world.components['TeleportComponent'][eid] = TeleportComponent(
                params['dest_map'], params['dest_x'], params['dest_y']
            )
        if 'healing' in params:
            world.components['HealingComponent'][eid] = HealingComponent(params['healing'])
        if 'buff_stat' in params:
            world.components['BuffComponent'][eid] = BuffComponent(
                params['buff_stat'], params['buff_value'], params['duration']
            )
        return eid
