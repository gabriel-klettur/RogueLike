import dataclasses

from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent
from roguelike_game.ecs.systems.rendering.combat.spells.teleport.model import TeleportModel


def test_teleport_component_asdict_serialization_embeds_model():
    model = TeleportModel(start_pos=(1, 2), end_pos=(3, 4), lifespan=0.2)
    comp = TeleportComponent(model=model)
    data = dataclasses.asdict(comp)
    # dataclasses.asdict performs a deep copy of values; ensure semantics, not identity
    restored = data["model"]
    assert isinstance(restored, TeleportModel)
    assert restored.start_pos == model.start_pos
    assert restored.end_pos == model.end_pos
    assert restored.lifespan == model.lifespan
