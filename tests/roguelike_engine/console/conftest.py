import os
from types import SimpleNamespace
import pytest

# Asegura entorno sin ventana para pygame si en algún momento se inicializa
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")


class FakeStack:
    def __init__(self, item_id: str, quantity: int) -> None:
        self.item_id = item_id
        self.quantity = quantity


class FakeInventory:
    def __init__(self, capacity: int = 10) -> None:
        self.capacity = capacity
        self.slots: list[FakeStack | None] = [None] * capacity

    def _find_slot_with(self, item_id: str) -> int | None:
        for i, s in enumerate(self.slots):
            if s and s.item_id == item_id:
                return i
        return None

    def _first_empty(self) -> int | None:
        for i, s in enumerate(self.slots):
            if s is None:
                return i
        return None

    def has(self, item_id: str, qty: int) -> bool:
        idx = self._find_slot_with(item_id)
        return bool(idx is not None and self.slots[idx].quantity >= qty)

    def add(self, item_id: str, qty: int) -> bool:
        idx = self._find_slot_with(item_id)
        if idx is not None:
            self.slots[idx].quantity += qty
            return True
        idx = self._first_empty()
        if idx is None:
            return False
        self.slots[idx] = FakeStack(item_id, qty)
        return True

    def remove(self, item_id: str, qty: int) -> bool:
        idx = self._find_slot_with(item_id)
        if idx is None or self.slots[idx].quantity < qty:
            return False
        self.slots[idx].quantity -= qty
        if self.slots[idx].quantity == 0:
            self.slots[idx] = None
        return True


@pytest.fixture()
def fake_game_with_inventory() -> SimpleNamespace:
    inv = FakeInventory(capacity=1)
    # Datos disponibles en el juego (conjunto de ítems válidos)
    items = {"potion_small", "weapons_sword", "armor_leather"}

    ecs_world = SimpleNamespace(
        player_entity=1,
        components={"InventoryComponent": {1: inv}},
        state=SimpleNamespace(godmode=False),
    )
    ecs = SimpleNamespace(ecs_world=ecs_world)
    # Estado a nivel de juego
    state = SimpleNamespace(godmode=False)

    return SimpleNamespace(items=items, ecs=ecs, state=state)


@pytest.fixture()
def empty_game_for_permissions() -> SimpleNamespace:
    # Simula que no se pasó game al registrar comandos de inventario
    return SimpleNamespace()


@pytest.fixture()
def registry_with_game(fake_game_with_inventory):
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.command_sets import register_commands

    reg = CommandRegistry()
    register_commands(reg, fake_game_with_inventory)
    return reg


@pytest.fixture()
def registry_without_game():
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.command_sets import register_commands

    reg = CommandRegistry()
    register_commands(reg, None)
    return reg


@pytest.fixture()
def patched_pygame(monkeypatch):
    import pygame

    posted: list[pygame.event.Event] = []

    def _nop(*_a, **_k):
        return None

    monkeypatch.setattr(pygame.key, "start_text_input", _nop, raising=False)
    monkeypatch.setattr(pygame.key, "stop_text_input", _nop, raising=False)
    monkeypatch.setattr(pygame.event, "post", lambda e: posted.append(e), raising=False)

    return SimpleNamespace(posted=posted)
