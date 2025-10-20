from dataclasses import dataclass

@dataclass
class AllowFriendlyFire:
    """
    Flag component that allows an entity to damage same-faction allies.
    Presence of this component on the ATTACKER bypasses friendly-fire filters.
    Future extension: add TTL/expires_at to make it temporary (provocation).
    """
    enabled: bool = True
