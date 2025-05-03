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

# Importamos el wrapper NPC de la antigua fábrica
from src.roguelike_game.entities.npc.factory_old import NPC

# Directorio donde alojamos cada sub-tipo (monster, elite, …)
TYPES_DIR      = Path(__file__).parent / "types"
CONFIG_PATTERN = str(TYPES_DIR / "*" / "config.yaml")

# Cargamos todos los config.yaml disponibles
_CONFIGS: dict[str, dict] = {}
for cfg_path in glob.glob(CONFIG_PATTERN):
    try:
        with open(cfg_path, encoding="utf-8") as f:
            cfg = yaml.safe_load(f)
        key = cfg.get("type") or Path(cfg_path).parent.name
        _CONFIGS[key] = cfg
    except Exception as e:
        print(f"⚠️ Error cargando {cfg_path}: {e}", file=sys.stderr)

class NPCFactory:
    """
    Crea NPCs usando config.yaml si existe, o delega a la antigua fábrica.
    """
    @staticmethod
    def create(npc_type: str, x: float, y: float, **kwargs):
        cfg = _CONFIGS.get(npc_type)
        if cfg is None:
            # Si no hay YAML, delegamos a la fábrica antigua
            from src.roguelike_game.entities.npc.factory_old import NPCFactory as OldFactory
            return OldFactory.create(npc_type, x, y, **kwargs)

        # import dinámico de model/controller/view
        pkg = f"src.roguelike_game.entities.npc.types.{npc_type}"
        m_mod = importlib.import_module(pkg + ".model")
        c_mod = importlib.import_module(pkg + ".controller")
        v_mod = importlib.import_module(pkg + ".view")

        ModelCls      = getattr(m_mod, cfg.get("model_class",      npc_type.capitalize() + "Model"))
        ControllerCls = getattr(c_mod, cfg.get("controller_class", npc_type.capitalize() + "Controller"))
        ViewCls       = getattr(v_mod, cfg.get("view_class",       npc_type.capitalize() + "View"))

        # 1) instanciamos el modelo
        model = ModelCls(x, y, **kwargs)

        # 2) Overwrite de stats desde YAML
        for stat in ("health", "max_health", "speed"):
            if stat in cfg:
                setattr(model, stat, cfg[stat])

        # 3) controller
        controller = ControllerCls(model)

        # 4) vista: pasamos sprite_paths y opcional sprite_size
        sprite_paths = cfg.get("sprite_paths", {})
        sprite_size  = tuple(cfg["sprite_size"]) if "sprite_size" in cfg else None

        # Aquí ViewCls debe aceptar (model, sprite_paths[, sprite_size])
        if sprite_size:
            view = ViewCls(model, sprite_paths, sprite_size)
        else:
            view = ViewCls(model, sprite_paths)

        return NPC(model, controller, view)
