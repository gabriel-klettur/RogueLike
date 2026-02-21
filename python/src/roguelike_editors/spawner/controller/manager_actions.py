from __future__ import annotations

"""Acciones vinculadas al gestor de plantillas (Templates Manager)."""
from typing import Any
import logging


def after_delete_template(controller: Any, template_id: str, removed_instances: int) -> None:
    """Refresca la lista de instancias y registra información tras eliminar un template."""
    try:
        controller.spawner_instances.refresh_from_disk()
    except Exception:
        pass
    try:
        logging.getLogger("roguelike_editors.spawner").info(
            "[SpawnerEditor] Template '%s' deleted. Removed %d instance(s).",
            template_id,
            int(removed_instances or 0),
        )
    except Exception:
        pass
