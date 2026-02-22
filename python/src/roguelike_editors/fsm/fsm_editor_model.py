from dataclasses import dataclass
import roguelike_engine.config.config as config


@dataclass
class FMSModel:
    """
    Modelo del FSM editor que centraliza el estado del toggle de entidades.
    """
    debug_entities_enabled: bool = False
    frame_skip: int = 2

    @classmethod
    def from_config(cls) -> "FMSModel":
        return cls(
            debug_entities_enabled=getattr(config, "DEBUG_ENTITIES", False),
            frame_skip=getattr(config, "DEBUG_ENTITIES_FRAME_SKIP", 2),
        )

    def apply_to_config(self) -> None:
        """Sincroniza el estado actual con el config global."""
        config.DEBUG_ENTITIES = bool(self.debug_entities_enabled)
        if hasattr(config, "DEBUG_ENTITIES_FRAME_SKIP"):
            try:
                config.DEBUG_ENTITIES_FRAME_SKIP = int(self.frame_skip)
            except Exception:
                # Mantener el valor anterior si no es convertible
                pass
