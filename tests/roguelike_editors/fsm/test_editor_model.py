from __future__ import annotations

import importlib

from roguelike_editors.fsm.fsm_editor_model import FMSModel


def test_model_from_config_reads_flags():
    # Ensure config has defaults
    cfg = importlib.import_module("roguelike_engine.config.config")
    cfg.DEBUG_ENTITIES = False
    cfg.DEBUG_ENTITIES_FRAME_SKIP = 2

    m = FMSModel.from_config()
    assert m.debug_entities_enabled is False
    assert m.frame_skip == 2


def test_model_apply_to_config_writes_flags():
    cfg = importlib.import_module("roguelike_engine.config.config")
    cfg.DEBUG_ENTITIES = False
    cfg.DEBUG_ENTITIES_FRAME_SKIP = 2

    m = FMSModel(debug_entities_enabled=True, frame_skip=5)
    m.apply_to_config()

    assert cfg.DEBUG_ENTITIES is True
    assert cfg.DEBUG_ENTITIES_FRAME_SKIP == 5
