# Path: src/roguelike_game/entities/npc/factory.py

import glob
import importlib
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    raise ImportError(
        "❌ PyYAML no está instalado. Ejecuta: pip install pyyaml"
    )

from roguelike_game.entities.npc.interfaces import IEntity

# ---------------------------------------------------------------------
# 1) Cargamos dinámicamente todos los config.yaml bajo npc/types/*
# ---------------------------------------------------------------------
TYPES_DIR      = Path(__file__).parent / "types"
CONFIG_PATTERN = str(TYPES_DIR / "*" / "config.yaml")

_CONFIGS: dict[str, dict] = {}
for cfg_path in glob.glob(CONFIG_PATTERN):
    try:
        with open(cfg_path, encoding="utf-8") as f:
            cfg = yaml.safe_load(f)
        key = cfg.get("type") or Path(cfg_path).parent.name
        _CONFIGS[key] = cfg
    except Exception as e:
        print(f"⚠️ Error cargando {cfg_path}: {e}", file=sys.stderr)

# ---------------------------------------------------------------------
# 2) Wrapper genérico que une Model, Controller y View en una entidad
# ---------------------------------------------------------------------
class NPC(IEntity):
    """
    Contenedor MVC para cualquier NPC.
    Delegamos update/render al controller/view,
    y exponemos propiedades x, y, sprite_size y mask.
    """
    def __init__(self, model, controller, view):
        self.model      = model
        self.controller = controller
        self.view       = view

    @property
    def x(self):
        return self.model.x

    @property
    def y(self):
        return self.model.y

    @property
    def sprite_size(self):
        # Preferir SPRITE_SIZE de la view si existe
        size = getattr(self.view, 'SPRITE_SIZE', None)
        if size is not None:
            return size
        # Fallback al modelo
        return getattr(self.model, 'sprite_size', None)

    @property
    def mask(self):
        # Máscara para colisiones
        return getattr(self.view, 'mask', None)

    def update(self, state, map):
        self.controller.update(state, map)

    def render(self, screen, camera):
        self.view.render(screen, camera)

    def __getattr__(self, name):
        # Cualquier otro atributo lo delegamos al modelo
        return getattr(self.model, name)


# ---------------------------------------------------------------------
# 3) La fábrica que levanta clases según config.yaml o convención
# ---------------------------------------------------------------------
class NPCFactory:
    @staticmethod
    def create(npc_type: str, x: float, y: float, **kwargs) -> IEntity:
        cfg = _CONFIGS.get(npc_type, {})

        # paquetes dinámicos
        pkg = f"roguelike_game.entities.npc.types.{npc_type}"
        m_mod = importlib.import_module(pkg + ".model")
        c_mod = importlib.import_module(pkg + ".controller")
        v_mod = importlib.import_module(pkg + ".view")

        # clases por convención o override en YAML
        ModelCls      = getattr(m_mod,       cfg.get("model_class",      npc_type.capitalize() + "Model"))
        ControllerCls = getattr(c_mod,       cfg.get("controller_class", npc_type.capitalize() + "Controller"))
        ViewCls       = getattr(v_mod,       cfg.get("view_class",       npc_type.capitalize() + "View"))

        # 1) Instanciar modelo y aplicar stats de YAML
        model = ModelCls(x, y, **kwargs)
        for stat in ("health", "max_health", "speed"):
            if stat in cfg:
                setattr(model, stat, cfg[stat])

        # 2) Instanciar controller
        controller = ControllerCls(model)

        # 3) Instanciar vista pasando rutas y tamaño de sprite
        sprite_paths = cfg.get("sprite_paths", {})
        sprite_size  = tuple(cfg["sprite_size"]) if "sprite_size" in cfg else None

        if sprite_size is not None:
            view = ViewCls(model, sprite_paths, sprite_size)
        else:
            view = ViewCls(model, sprite_paths)

        # 4) Devolver la entidad completa
        return NPC(model, controller, view)
