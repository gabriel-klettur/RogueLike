import types

import roguelike_game.ecs.systems.physics.coin_pickup_system as cps
from roguelike_game.ecs.components.inventory_component import InventoryComponent


class FakeDropMgr:
    def __init__(self, path):
        self.path = path
        self.picked = []
    def pick_up(self, drop_id):
        self.picked.append(drop_id)


class FakePersistSys:
    def __init__(self):
        self.calls = []
    def _persist_inventory(self, player_eid, inv):
        self.calls.append((player_eid, inv.serialize()))


def test_coin_pickup_adds_to_inventory_and_removes_entity(monkeypatch):
    # Forzar TILE_SIZE y catálogo de items
    monkeypatch.setattr(cps, 'TILE_SIZE', 32, raising=False)
    monkeypatch.setattr(cps, 'load_items', lambda p: {'gold': types.SimpleNamespace(experience=None)}, raising=True)
    # Evitar I/O de ItemDropManager
    monkeypatch.setattr(cps, 'ItemDropManager', FakeDropMgr, raising=True)

    # Mundo con jugador y moneda a distancia <= TILE_SIZE
    player_eid = 1
    coin_eid = 2
    inv = InventoryComponent(capacity=5, player_id='p')
    # Pre-cargar un poco de oro para ver suma
    inv.add('gold', 1)

    removed = []
    def remove_entity(eid):
        removed.append(eid)

    world = types.SimpleNamespace(
        update_systems=[FakePersistSys()],
        remove_entity=remove_entity,
        components={
            'PlayerTagComponent': {player_eid: object()},
            'InventoryComponent': {player_eid: inv},
            'Position': {
                player_eid: types.SimpleNamespace(x=0, y=0),
                coin_eid: types.SimpleNamespace(x=16, y=0),  # dentro de 32
            },
            'PhysicalItemComponent': {coin_eid: types.SimpleNamespace(item_id='gold', quantity=3, drop_id='d1')},
            'CollectibleComponent': {coin_eid: object()},
        }
    )

    sys = cps.CoinPickupSystem(items_path='ignored.json')
    sys.update(world)

    # Inventario incrementado
    assert inv.has('gold', 4)
    # Persistencias realizadas
    fps = world.update_systems[0]
    assert fps.calls and fps.calls[0][0] == player_eid
    # Drop registrado y entidad moneda eliminada
    assert isinstance(sys.drop_manager, FakeDropMgr)
    assert sys.drop_manager.picked == ['d1']
    assert removed == [coin_eid]
