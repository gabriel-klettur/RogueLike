import json
import shutil
from pathlib import Path
from datetime import datetime

PROB = 0.25
PROJECT_ROOT = Path(__file__).resolve().parents[1]
HOSTILES_PATH = PROJECT_ROOT / 'data' / 'entities' / 'new_hostiles.json'


def main():
    path = HOSTILES_PATH
    if not path.exists():
        raise SystemExit(f"No existe el archivo: {path}")

    # Backup con timestamp
    ts = datetime.now().strftime('%Y%m%d_%H%M%S')
    backup_path = path.with_suffix(f'.backup_{ts}.json')
    shutil.copyfile(path, backup_path)

    # Cargar JSON
    with open(path, 'r', encoding='utf-8-sig') as f:
        data = json.load(f)

    classes = (data.get('hostiles') or {}).get('classes') or {}
    if not classes:
        raise SystemExit('No se encontraron clases en hostiles.classes')

    count = 0
    for cls_name, cls_cfg in classes.items():
        stats = cls_cfg.setdefault('stats', {})
        # Inyectar valor en todos (forzar a PROB)
        stats['damage_stop_probability'] = float(PROB)
        count += 1

    # Guardar JSON formateado
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write('\n')

    print(f'Actualizado damage_stop_probability={PROB} en {count} clases. Backup: {backup_path.name}')


if __name__ == '__main__':
    main()
