# src/roguelike_game/entities/npc/factory.py

import yaml
from pathlib import Path

from .base.model import BaseNPCModel
from .base.controller import BaseNPCController
from .base.view import BaseNPCView
from .interfaces import IEntity
from roguelike_engine.utils.loader import load_image

# Importamos la fábrica vieja para fallback
from .factory_old import NPCFactoryStatic

# ——— CARGA DINÁMICA DE CONFIGURACIONES ———————————————
_CONFIGS: dict[str, dict] = {}

def _load_configs():
    """
    Busca recursivamente src/.../npc/types/*/config.yaml y carga en _CONFIGS.
    Cada entrada queda como:
      _CONFIGS["monster"] = {
          "stats": {"health": ..., "max_health": ..., "speed": ...},
          "sprite_path": "npc/monster/monster_spritesheet.png"
      }
    """
    types_dir = Path(__file__).parent / "types"
    if not types_dir.exists():  # si aún no creaste types/, salen vacíos
        return

    for cfg_file in types_dir.glob("*/config.yaml"):
        try:
            cfg = yaml.safe_load(cfg_file.read_text(encoding="utf-8"))
            t = cfg.get("type")
            if not t:
                continue
            # Extraemos solo las claves esperadas
            stats = { k: cfg[k] for k in ("health", "max_health", "speed") }
            sprite = cfg.get("sprite_path")
            _CONFIGS[t] = {"stats": stats, "sprite_path": sprite}
        except Exception as e:
            print(f"⚠️ Error loading NPC config {cfg_file}: {e}")

# Al importar este módulo, cargamos la configuración
_load_configs()

# ——— WRAPPER DE ENTIDAD —————————————————————————————————————————
class NPC(IEntity):
    """
    Wrapper que unifica Model, Controller y View en una sola entidad.
    """
    def __init__(self, model, controller, view):
        self.model = model
        self.controller = controller
        self.view = view

    @property
    def x(self): return self.model.x
    @property
    def y(self): return self.model.y
    @property
    def sprite_size(self):
        return getattr(self.model, "sprite_size", None)
    @property
    def mask(self):
        return getattr(self.view, "mask", None)

    def update(self, state):
        self.controller.update(state)
    def render(self, screen, camera):
        self.view.render(screen, camera)

# ——— FÁBRICA PRINCIPAL —————————————————————————————————————————
class NPCFactory:
    @staticmethod
    def create(npc_type: str, x: float, y: float, **kwargs) -> IEntity:
        # 1) Si está en nuestras configs dinámicas, usar BaseNPC*
        if npc_type in _CONFIGS:
            cfg = _CONFIGS[npc_type]
            stats = cfg["stats"]
            # Modelo genérico
            model = BaseNPCModel(x, y, npc_type)
            model.health     = stats["health"]
            model.max_health = stats["max_health"]
            model.speed      = stats["speed"]
            # Controlador base
            controller = BaseNPCController(model)
            # Vista base: cargamos sprite y lo pasamos
            sprite = load_image(
                cfg["sprite_path"],
                getattr(model, "sprite_size", (64, 64))
            )
            view = BaseNPCView(model, sprite)
            print(f"🆕 NPC '{npc_type}' loaded from dynamic config.")
            return NPC(model, controller, view)

        # 2) Si no, fallback a la fábrica estática original
        print(f"⚠️ NPC type '{npc_type}' not found in dynamic configs. Using static factory.")
        return NPCFactoryStatic.create(npc_type, x, y, **kwargs)
