import os
from pathlib import Path


def test_no_legacy_vendor_files():
    """
    Garantiza que no existan archivos legacy de vendors fuera de la estructura nueva.

    - Cualquier archivo con patrón 'inventory_vendor*.json' debe vivir bajo 'data/vendors/'.
    - No deben quedar archivos de vendors en 'data/_legacy_removed/'.
    """
    repo_root = Path(__file__).resolve().parents[2]
    data_dir = repo_root / 'data'
    allowed_vendor_root = data_dir / 'vendors'

    offenses = []

    # 1) Archivos con patrón legacy fuera de la nueva raíz
    for p in data_dir.rglob('inventory_vendor*.json'):
        try:
            # Si está bajo allowed_vendor_root, es válido (semillas actuales)
            p.relative_to(allowed_vendor_root)
        except Exception:
            offenses.append(str(p))

    # 2) Nada de vendors dentro de _legacy_removed
    legacy_removed_dir = data_dir / '_legacy_removed'
    if legacy_removed_dir.exists():
        for p in legacy_removed_dir.rglob('*vendor*'):
            offenses.append(str(p))

    assert not offenses, (
        "Se encontraron archivos legacy de vendors fuera de la estructura permitida ("
        f"{allowed_vendor_root}).\n" + "\n".join(offenses)
    )
