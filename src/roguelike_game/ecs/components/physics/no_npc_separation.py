from dataclasses import dataclass


@dataclass
class NoNpcSeparation:
    """Marker component to exclude an entity from NPC separation pushes.
    Useful for vendors pinned to a strict anchor so they don't drift after spawn.
    """
    reason: str = "strict_anchor"
