import os
import json
from typing import Tuple, Any, Dict
from .stats_templates import MONSTER_STATS_TEMPLATE, PLAYER_STATS_TEMPLATE
from roguelike_ui.services.json_persistence import load_from_json


def load_entity_data(ent_id: str, player_stats: dict, monsters: dict) -> Tuple[str, dict, dict]:
    """
    Load the JSON data for the given entity id (player or monster).

    Returns:
        (path, data, entry): absolute json path, classes dict, and the entity entry dict.
    """
    # Decide source file by membership (players vs monsters)
    if ent_id in player_stats:
        path = os.path.join(os.getcwd(), "data", "entities", "new_players.json")
        root = load_from_json(path)
        classes = root.get("players", {}).get("classes", {})
        data = classes
    else:
        path = os.path.join(os.getcwd(), "data", "entities", "new_monsters.json")
        root = load_from_json(path)
        data = root.setdefault("monsters", {}).setdefault("classes", {})

    entry = data.setdefault(ent_id, {})
    return path, data, entry


def save_entity_data(ent_id: str, entry: dict, path: str, player_stats: dict, monsters: dict) -> None:
    """
    Persist the given entity entry into its corresponding JSON file.

    Notes:
    - For players: writes under players.classes[ent_id]
    - For monsters: writes under monsters.classes[ent_id]
    """
    if ent_id in player_stats:
        full = path
        root = load_from_json(full)
        # Sanitizar stats antes de completar el esqueleto
        entry = dict(entry or {})
        entry['stats'] = _sanitize_stats(entry.get('stats', {}))
        # Asegurar esqueleto completo para jugadores (persistir nulls explícitos)
        entry = ensure_player_skeleton(entry)
        root.setdefault("players", {}).setdefault("classes", {})[ent_id] = entry
        with open(full, "w", encoding="utf-8") as f:
            json.dump(root, f, ensure_ascii=False, indent=2)
    else:
        full = path
        root = load_from_json(full)
        # Sanitizar stats antes de completar el esqueleto
        entry = dict(entry or {})
        entry['stats'] = _sanitize_stats(entry.get('stats', {}))
        # Asegurar esqueleto completo para monstruos (persistir nulls explícitos)
        entry = ensure_monster_skeleton(entry)
        root.setdefault("monsters", {}).setdefault("classes", {})[ent_id] = entry
        with open(full, "w", encoding="utf-8") as f:
            json.dump(root, f, ensure_ascii=False, indent=2)


def convert_value(new_text: str, old_val: Any) -> Any:
    """
    Convert a string input back to the original type of the old value when possible.
    Supported: bool, int, float, str.
    Fallback to string if conversion fails.
    """
    try:
        s = (new_text or "").strip()
        # Eliminar caracteres de control no imprimibles (p.ej. \x7f)
        s = "".join(ch for ch in s if ch.isprintable())
        low = s.lower()
        # Common nulls
        if low in ("none", "null", ""):
            return None
        # Preserve bool edits if previous type was bool
        if isinstance(old_val, bool):
            return low in ("true", "1", "yes", "y", "t")
        # Numeric guess
        try:
            return int(s)
        except ValueError:
            pass
        try:
            return float(s)
        except ValueError:
            pass
        return s
    except Exception:
        return new_text


def _sanitize_scalar(val: Any) -> Any:
    """Limpia cadenas de control y convierte números si es posible."""
    if isinstance(val, str):
        return convert_value(val, None)
    return val


def _sanitize_stats(node: Any) -> Any:
    """Recorre recursivamente el diccionario de stats para limpiar cadenas y forzar números."""
    if isinstance(node, dict):
        out = {}
        for k, v in node.items():
            out[k] = _sanitize_stats(v)
        return out
    if isinstance(node, list):
        return [_sanitize_stats(v) for v in node]
    return _sanitize_scalar(node)


def ensure_monster_skeleton(entry: Dict[str, Any]) -> Dict[str, Any]:
    """
    Garantiza que la entrada del monstruo tenga todas las claves esperadas con valores explícitos,
    usando None para los que falten, de modo que al serializar queden como null.
    Estructura esperada:
    - stats: dict
    - assets:
        - active_set: 'no-sets' | 'sets'
        - no-sets:
            - {state}: {direction: path|None}
            - sprites_data_no-set: { scale_*: float|None, tint: [r,g,b]|None }
        - sets:
            - sprites_set: {state: [sheet_path]|[]}
            - sprites_data_set: { scale_*: float|None, tint: [r,g,b]|None }
    """
    states = ['idle', 'walk', 'chase', 'cast', 'attack', 'damage', 'death']
    directions = ['s', 'se', 'e', 'ne', 'n', 'nw', 'w', 'sw']

    entry = dict(entry or {})
    # Ensure stats keys exist and persist as null if missing
    stats = entry.setdefault('stats', {})
    # Deep-fill from template (handle nested dicts generically)
    def _fill_from_template(tpl: Dict[str, Any], tgt: Dict[str, Any]):
        for k, v in tpl.items():
            if isinstance(v, dict):
                sub = tgt.setdefault(k, {}) if isinstance(tgt.get(k), dict) else {}
                if k not in tgt:
                    tgt[k] = sub
                _fill_from_template(v, sub)
            else:
                tgt.setdefault(k, None)
    _fill_from_template(MONSTER_STATS_TEMPLATE, stats)
    assets = entry.setdefault('assets', {})
    # active_set válido
    active = assets.get('active_set')
    if active not in ('no-sets', 'sets'):
        assets['active_set'] = 'no-sets'

    # no-sets
    no_sets = assets.setdefault('no-sets', {})
    for st in states:
        dir_map = no_sets.setdefault(st, {})
        if isinstance(dir_map, dict):
            for d in directions:
                dir_map.setdefault(d, None)
        else:
            # si está mal tipado, reemplazar por dict vacío con None
            no_sets[st] = {d: None for d in directions}
    meta_no = no_sets.setdefault('sprites_data_no-set', {})
    for st in states:
        default_scale = 0.55 if st == 'death' else 0.5
        meta_no.setdefault(f'scale_{st}', default_scale)
    meta_no.setdefault('tint', None)

    # sets
    sets = assets.setdefault('sets', {})
    sheets = sets.setdefault('sprites_set', {})
    for st in states:
        # cada estado debe ser lista (posiblemente vacía)
        lst = sheets.get(st)
        if not isinstance(lst, list):
            sheets[st] = []
    meta_set = sets.setdefault('sprites_data_set', {})
    for st in states:
        default_scale = 0.55 if st == 'death' else 0.5
        meta_set.setdefault(f'scale_{st}', default_scale)
    meta_set.setdefault('tint', None)

    return entry


def ensure_player_skeleton(entry: Dict[str, Any]) -> Dict[str, Any]:
    """
    Similar a ensure_monster_skeleton pero para jugadores.
    - Rellena stats con claves de PLAYER_STATS_TEMPLATE en None si faltan.
    - Garantiza estructura de assets con active_set por defecto 'sets'.
    """
    states = ['idle', 'walk', 'chase', 'cast', 'attack', 'damage', 'death']
    directions = ['s', 'se', 'e', 'ne', 'n', 'nw', 'w', 'sw']

    entry = dict(entry or {})
    # Stats
    stats = entry.setdefault('stats', {})
    def _fill_from_template(tpl: Dict[str, Any], tgt: Dict[str, Any]):
        for k, v in tpl.items():
            if isinstance(v, dict):
                sub = tgt.setdefault(k, {}) if isinstance(tgt.get(k), dict) else {}
                if k not in tgt:
                    tgt[k] = sub
                _fill_from_template(v, sub)
            else:
                tgt.setdefault(k, None)
    _fill_from_template(PLAYER_STATS_TEMPLATE, stats)

    # Assets
    assets = entry.setdefault('assets', {})
    # active_set por defecto 'sets' para jugadores
    active = assets.get('active_set')
    if active not in ('no-sets', 'sets'):
        assets['active_set'] = 'sets'

    # no-sets
    no_sets = assets.setdefault('no-sets', {})
    for st in states:
        dir_map = no_sets.setdefault(st, {})
        if isinstance(dir_map, dict):
            for d in directions:
                dir_map.setdefault(d, None)
        else:
            no_sets[st] = {d: None for d in directions}
    meta_no = no_sets.setdefault('sprites_data_no-set', {})
    for st in states:
        default_scale = 0.55 if st == 'death' else 0.5
        meta_no.setdefault(f'scale_{st}', default_scale)
    meta_no.setdefault('tint', None)

    # sets
    sets = assets.setdefault('sets', {})
    sheets = sets.setdefault('sprites_set', {})
    for st in states:
        lst = sheets.get(st)
        if not isinstance(lst, list):
            sheets[st] = []
    meta_set = sets.setdefault('sprites_data_set', {})
    for st in states:
        default_scale = 0.55 if st == 'death' else 0.5
        meta_set.setdefault(f'scale_{st}', default_scale)
    meta_set.setdefault('tint', None)

    return entry
