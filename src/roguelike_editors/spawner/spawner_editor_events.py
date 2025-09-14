# Facade module: re-export minimal handler implementation
# This keeps external imports stable: from ...spawner_editor_events import SpawnerEditorEventHandler

from __future__ import annotations

from .events.handler import SpawnerEditorEventHandler  # noqa: F401
