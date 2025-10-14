"""Map Editor events facade.

Re-exports MapEditorEventHandler from the modular events package
to keep backward compatibility.
"""
from roguelike_editors.map.events.handler import MapEditorEventHandler

__all__ = ["MapEditorEventHandler"]