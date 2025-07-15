import json
from typing import List, Dict, Union, Optional

class ItemDropManager:
    _instances = {}

    def __new__(cls, path: str):
        if path in cls._instances:
            return cls._instances[path]
        instance = super(ItemDropManager, cls).__new__(cls)
        cls._instances[path] = instance
        return instance
    """
    Gestor de drops de ítems en el mapa, persiste en un JSON.
    """
    def __init__(self, path: str):
        """
        Inicializa gestor con la ruta a inventory_map.json.
        """
        self.path = path
        try:
            with open(self.path, 'r', encoding='utf-8') as f:
                self._data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            self._data = {}
            self._persist()

    def _persist(self):
        with open(self.path, 'w', encoding='utf-8') as f:
            json.dump(self._data, f, indent=2)

    def create_drop(self, drop_id: str, item_id: str, quantity: int,
                    zone_id: str,
                    tile: Union[Dict[str, Union[int, float]], object] = None,
                    position: Union[Dict[str, Union[int, float]], object] = None) -> None:
        """
        Registra un drop en el mapa con su drop_id, zona y coordenadas de tile o posición relativa.
        """
        entry = {
            'item_id': item_id,
            'quantity': quantity,
            'zone_id': zone_id,
            'schema_version': '1.0.0'
        }
        if tile is not None:
            # Soporta tile dict o con atributos x,y
            if hasattr(tile, 'x') and hasattr(tile, 'y'):
                coords = {'x': tile.x, 'y': tile.y}
            else:
                coords = {'x': tile.get('x'), 'y': tile.get('y')}
            entry['tile'] = coords
        elif position is not None:
            # Soporta position dict o con atributos x,y
            if hasattr(position, 'x') and hasattr(position, 'y'):
                coords = {'x': position.x, 'y': position.y}
            else:
                coords = {'x': position.get('x'), 'y': position.get('y')}
            entry['position'] = coords
        else:
            raise ValueError("Debe especificar 'tile' o 'position'")
        self._data[drop_id] = entry
        self._persist()

    def pick_up(self, drop_id: str) -> bool:
        print(f"[ItemDropManager][DEBUG] pick_up called for drop {drop_id}")
        """
        Elimina el drop del mapa y devuelve True si existía.
        """
        if drop_id in self._data:
            del self._data[drop_id]
            self._persist()
            return True
        return False

    def load_all(self) -> List[Dict]:
        """
        Carga todos los drops persistidos desde inventory_map.json.
        """
        return list(self._data.values())

    def update_drop(self, drop_id: str, tile=None, position=None) -> None:
        """
        Actualiza la posición de un drop existente en el JSON.
        tile: dict u objeto con atributos x,y; position: dict u objeto con atributos x,y.
        """
        # Recargar datos del archivo para actualizar cambios externos
        try:
            with open(self.path, 'r', encoding='utf-8') as f:
                self._data = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            self._data = {}

        if drop_id not in self._data:
            raise KeyError(f"Drop '{drop_id}' no existe")
        entry = self._data[drop_id]
        # Eliminar coordenadas anteriores
        entry.pop('tile', None)
        entry.pop('position', None)
        if tile is not None:
            if hasattr(tile, 'x') and hasattr(tile, 'y'):
                coords = {'x': tile.x, 'y': tile.y}
            else:
                coords = {'x': tile.get('x'), 'y': tile.get('y')}
            entry['tile'] = coords
        elif position is not None:
            if hasattr(position, 'x') and hasattr(position, 'y'):
                coords = {'x': position.x, 'y': position.y}
            else:
                coords = {'x': position.get('x'), 'y': position.get('y')}
            entry['position'] = coords
        else:
            raise ValueError("Debe especificar 'tile' o 'position'")
        self._persist()
