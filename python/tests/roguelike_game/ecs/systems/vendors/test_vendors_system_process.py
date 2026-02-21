import types

from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem


class FakeTransfer:
    def __init__(self):
        self.calls = []
    def transfer(self, world, item_id, qty, src, dst):
        self.calls.append((item_id, qty, src, dst))


def test_vendor_buy_happy_path(monkeypatch):
    # Fake transfer system provider
    ft = FakeTransfer()
    monkeypatch.setattr(
        'roguelike_game.ecs.systems.vendors.services.get_transfer_system',
        lambda world: ft,
        raising=True,
    )

    # Mundo con player y vendor + inventarios y VendorComponent con override de precio
    player_eid = 1
    vendor_eid = 2

    from roguelike_game.ecs.components.inventory_component import InventoryComponent
    inv_p = InventoryComponent(capacity=5, player_id='p')
    inv_v = InventoryComponent(capacity=5, player_id='v')
    inv_p.add('gold', 100)
    inv_v.add('wood', 10)

    world = types.SimpleNamespace(
        player_entity=player_eid,
        components={
            'InventoryComponent': {player_eid: inv_p, vendor_eid: inv_v},
            'VendorComponent': {vendor_eid: types.SimpleNamespace(prices={'wood': {'buy': 2, 'sell': 1}})},
        }
    )

    sys = VendorTradeSystem()
    # Forzar normalización sencilla: item->wood, currency->gold
    class FakeNorm:
        def __init__(self):
            pass
        def normalize_ids(self, world, vendor_eid, item_id):
            return ('wood', 'gold')
    sys._id_normalizer = FakeNorm()

    out = sys.buy(world, vendor_eid, 'wood', 3)

    # Mensaje de éxito y dos transferencias (wood y gold)
    assert 'Compraste 3x wood' in out
    assert len(ft.calls) == 2
    assert ft.calls[0] == ('wood', 3, vendor_eid, player_eid)
    assert ft.calls[1] == ('gold', 6, player_eid, vendor_eid)
