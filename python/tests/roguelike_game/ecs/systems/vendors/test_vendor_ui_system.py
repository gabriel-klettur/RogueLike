import os
import types
from typing import Any, Dict, Tuple

import pygame
import pytest

# Módulo bajo prueba
import roguelike_game.ecs.systems.vendors.vendor_ui_system as vendor_ui
from roguelike_game.managers.core.state import GameState


@pytest.fixture(scope="module")
def pygame_headless():
    os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
    pygame.init()
    try:
        pygame.display.set_mode((1024, 640))
        yield
    finally:
        pygame.quit()


@pytest.fixture()
def screen(pygame_headless):
    surf = pygame.display.get_surface()
    if surf is None:
        surf = pygame.display.set_mode((1024, 640))
    return surf


class _Item:
    def __init__(self, name: str) -> None:
        self.name = name
        self.icon_small = None
        self.icon = None


class _Stack:
    def __init__(self, item_id: str, quantity: int) -> None:
        self.item_id = item_id
        self.quantity = quantity


class _Inventory:
    def __init__(self, stacks):
        self.slots = stacks


class _VTSStub:
    def __init__(self, price_buy: int = 1, price_sell: int = 1):
        self.calls: list[Tuple[str, int, str, int]] = []
        self.price_buy = price_buy
        self.price_sell = price_sell

    def _get_price(self, world: Any, vendor_eid: int, item_id: str, op: str) -> int | None:
        return self.price_buy if op == "buy" else self.price_sell

    def buy(self, world: Any, vendor_eid: int, item_id: str, qty: int) -> str:
        self.calls.append(("buy", vendor_eid, item_id, qty))
        return f"OK buy {qty} {item_id}"

    def sell(self, world: Any, vendor_eid: int, item_id: str, qty: int) -> str:
        self.calls.append(("sell", vendor_eid, item_id, qty))
        return f"OK sell {qty} {item_id}"


def _make_world(vendor_items: Dict[str, Tuple[str, int]], *, chat_open: bool = True):
    """Crea un mundo mínimo con state, components e inventario de vendedor.
    vendor_items: dict[item_id] = (display_name, quantity)
    """
    world = types.SimpleNamespace()
    world.update_systems = []
    state = GameState()
    state.chat_open = chat_open
    vendor_eid = 42
    state.chat_bind_target(vendor_eid)
    world.state = state
    # Components
    items = {}
    assets = {}
    # Dummy surfaces para que haya iconos
    for iid, (name, _qty) in vendor_items.items():
        items[iid] = _Item(name)
        surf = pygame.Surface((16, 16), pygame.SRCALPHA)
        surf.fill((200, 100, 50, 255))
        assets[iid] = surf
    inv = _Inventory([_Stack(iid, qty) for iid, (_name, qty) in vendor_items.items()])
    world.components = {
        'InventoryComponent': {vendor_eid: inv},
    }
    world.player_entity = 1
    return world, vendor_eid, items, assets


@pytest.fixture()
def vts_stub():
    return _VTSStub()


@pytest.fixture()
def ui(monkeypatch, vts_stub):
    # Monkeypatch de assets/items para aislar DB
    def fake_loader(_items_path: str):
        # Retorna vacíos; se inyectarán en cada test a la instancia
        return {}, {}
    monkeypatch.setattr(vendor_ui, "load_items_and_icons", fake_loader)

    # Parcheo del VTS: método de instancia que usa la UI
    monkeypatch.setattr(vendor_ui.VendorUISystem, "_get_vts", lambda self, _w: vts_stub)

    ui = vendor_ui.VendorUISystem()
    # Inyectar items y assets (evitamos DB)
    ui.items = {}
    ui.icon_surfaces = {}
    return ui


def test_scrollbar_inside_panel_and_thumb_clamped(screen, ui):
    # Generar muchos ítems para garantizar que visible_rows < total
    many: dict[str, tuple[str, int]] = {}
    base = [
        ("borsh", "Borsh"), ("chilenito", "Completo Chileno"), ("muslo_pollo", "Muslo de Pollo"),
        ("hakarl", "Hakarl"), ("paella", "Paella"), ("perogi", "Perogi"), ("tortilla_spain", "Tortilla Spain"),
    ]
    for i in range(30):
        key, name = base[i % len(base)]
        iid = f"{key}_{i:02d}"
        many[iid] = (f"{name} {i:02d}", 100)
    world, vendor_eid, items, assets = _make_world(many)
    ui.items = items
    ui.icon_surfaces = assets

    # Forzar panel con poca altura para que haya scroll
    # Simulamos que el chat ocupa un rect bajo; el panel se ancla a la derecha y toma su altura
    world.state.chat_block_rect = pygame.Rect(10, 520, 520, 100)

    # Precondición: el recolector debe devolver filas
    rows = ui._collect_rows(world, world.state.chat_target_eid)
    assert len(rows) > 0, f"rows empty with {len(rows)}"

    ui.update(world, screen, camera=None)

    panel = world.state.vendor_ui_panel_rect
    sb = world.state.vendor_ui_scrollbar_rect
    thumb = world.state.vendor_ui_scrollbar_thumb_rect
    # Asegurar precondición de scroll: total > visibles
    assert world.state.vendor_ui_total_rows > world.state.vendor_ui_visible_rows, (
        world.state.vendor_ui_total_rows, world.state.vendor_ui_visible_rows
    )
    assert panel is not None
    assert sb is not None, "Scrollbar track should exist when there are many rows"
    assert thumb is not None
    # Dentro del panel (con margen de borde)
    assert sb.left >= panel.left + 1
    assert sb.right <= panel.right - 1
    assert sb.top >= panel.top + 1
    assert sb.bottom <= panel.bottom - 1
    # Thumb dentro del track
    assert thumb.left >= sb.left
    assert thumb.right <= sb.right
    assert thumb.top >= sb.top
    assert thumb.bottom <= sb.bottom


def test_scroll_clamped_in_render_and_events(screen, ui):
    world, vendor_eid, items, assets = _make_world({
        'borsh_01': ("Borsh 01", 100),
        'paella_01': ("Paella 01", 100),
        'perogi_01': ("Perogi 01", 100),
    })
    ui.items = items
    ui.icon_surfaces = assets

    # Forzar scroll fuera de rango y render clampa
    world.state.vendor_ui_scroll = 999
    ui.update(world, screen, camera=None)
    assert world.state.vendor_ui_scroll >= 0

    # Wheel sobre panel sube/baja con clamp
    panel = world.state.vendor_ui_panel_rect
    center = panel.center
    ev_up = pygame.event.Event(pygame.MOUSEWHEEL, y=1)
    pygame.mouse.set_pos(center)
    vendor_ui.handle_vendor_ui_events(world, [ev_up])
    after_up = world.state.vendor_ui_scroll
    ev_down = pygame.event.Event(pygame.MOUSEWHEEL, y=-1000)
    vendor_ui.handle_vendor_ui_events(world, [ev_down])
    assert world.state.vendor_ui_scroll == 0
    # Otro up para no exceder max
    vendor_ui.handle_vendor_ui_events(world, [ev_up])
    assert world.state.vendor_ui_scroll >= 0


def test_buttons_click_call_vts_and_append_chat(screen, ui, vts_stub):
    world, vendor_eid, items, assets = _make_world({
        'borsh_01': ("Borsh 01", 100),
    })
    ui.items = items
    ui.icon_surfaces = assets

    ui.update(world, screen, camera=None)
    rows_btns = world.state.vendor_ui_btn_rects
    assert rows_btns
    entry = rows_btns[0]
    buy_rect = entry['buy']
    sell_rect = entry['sell']

    # Simular clicks
    ev_buy = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=buy_rect.center)
    vendor_ui.handle_vendor_ui_events(world, [ev_buy])
    ev_sell = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=sell_rect.center)
    vendor_ui.handle_vendor_ui_events(world, [ev_sell])

    # Verificar llamadas
    ops = [c[0] for c in vts_stub.calls]
    assert "buy" in ops and "sell" in ops
    # Mensajes añadidos al chat
    assert any("OK buy" in t[1] or "OK sell" in t[1] for t in world.state.chat_messages)


def test_panel_expands_with_longer_names(screen, ui):
    world, vendor_eid, items, assets = _make_world({
        'short_01': ("Short", 100),
    })
    ui.items = items
    ui.icon_surfaces = assets

    ui.update(world, screen, camera=None)
    w_short = world.state.vendor_ui_panel_rect.width

    # Hacer el nombre más largo y re-renderizar
    ui.items['short_01'].name = "Nombre de Item Excepcionalmente Largo 01"
    ui.update(world, screen, camera=None)
    w_long = world.state.vendor_ui_panel_rect.width

    assert w_long >= w_short


def test_button_rects_stay_inside_panel(screen, ui):
    world, vendor_eid, items, assets = _make_world({
        'borsh_01': ("Borsh 01", 100),
        'paella_01': ("Paella 01", 100),
        'perogi_01': ("Perogi 01", 100),
    })
    ui.items = items
    ui.icon_surfaces = assets

    ui.update(world, screen, camera=None)

    panel = world.state.vendor_ui_panel_rect
    for entry in world.state.vendor_ui_btn_rects:
        for key in ("buy", "sell"):
            rect = entry[key]
            assert rect.left >= panel.left + 1
            assert rect.right <= panel.right - 1
            assert rect.top >= panel.top + 1
            assert rect.bottom <= panel.bottom - 1
