import json
from pathlib import Path

def test_defaults_cover_all_monsters():
    # Ruta al directorio raíz del proyecto
    project_root = Path(__file__).resolve().parents[1]
    monsters_path = project_root / "data" / "monsters.json"
    defaults_path = project_root / "data" / "defaults" / "inventory_monsters.json"

    monsters = set(json.loads(monsters_path.read_text()).keys())
    defaults = set(json.loads(defaults_path.read_text()).keys())

    missing = monsters - defaults
    extra = defaults - monsters

    assert not missing, f"Faltan drops para: {missing}"
    assert not extra, f"Drops inesperados para monstruos desconocidos: {extra}"
