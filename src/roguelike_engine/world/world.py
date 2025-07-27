from typing import Dict, Optional
from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.world.persistence import save_world_state, load_world_state
from roguelike_engine.world.world_config import WORLD_CONFIG
from roguelike_game.managers.map import MapManager


class WorldManager:
    """
    Orquesta múltiples MapManagers (niveles), mantiene el estado global persistente
    (NPCs, inventario) y gestiona carga/descarga de niveles.
    """
    def __init__(self, global_config=WORLD_CONFIG, load_state_on_init: bool = True):
        # Estado persistente de NPCs globales        
        self.npc_memory: Dict[str, dict] = {}
        # Configuración global (paths, límites de carga, etc.)
        self.config = global_config

        # Mapas cargados en memoria: nivel -> instancia MapManager
        self.maps: Dict[str, MapManager] = {}
        # Pending levels para carga lazy: nombre -> estado serializado
        self._pending_levels: Dict[str, dict] = {}
        self.current_level: Optional[str] = None
        
        # Si hay autosave, cargar estado previo
        if load_state_on_init and self.config.autosave_enabled:
            try:
                data = load_world_state(self.config.save_path)
                self._apply_loaded_state(data)
            except FileNotFoundError:
                # No hay estado previo, se crea nuevo
                pass

    def _apply_loaded_state(self, data: dict):
        """
        Aplica estado cargado parcialmente: NPCs y nivel actual.
        Guarda otros niveles para carga lazy.
        """
        # NPCs
        self.npc_memory = data.get("npcs", {})
        # Niveles serializados
        levels_data = data.get("levels", {})
        # Determinar nivel actual guardado
        player_info = data.get("player", {})
        current = player_info.get("level")
        # Carga lazy: guarda todos
        self._pending_levels = dict(levels_data)
        # Determina nivel actual para futura carga sin deserializar aún
        if current and current in self._pending_levels:
            self.current_level = current
        else:
            self.current_level = None

    def load_level(self, level_name: str):
        """
        Carga o construye el mapa indicado, descarga si es ndecesario
        y restaura estado de jugador/NPCs.
        """
        # Descargar exceso de niveles según max_loaded_levels
        self._enforce_level_limit()

        # Obtener o crear MapManager (lazy load si hay estado)
        if level_name not in self.maps:
            if getattr(self, '_pending_levels', None) and level_name in self._pending_levels:
                state = self._pending_levels.pop(level_name)
                mgr = MapManager(level_name)
                mgr.deserialize_state(state)
                self.maps[level_name] = mgr
            else:
                self.maps[level_name] = MapManager(level_name)
        self.current_level = level_name

        # Restaurar posición del jugador y NPCs globales
        mgr = self.maps[level_name]
        # Cargar posición previa de jugador desde estado local del mapa
        last_pos = mgr._local_state.get("player_pos")
        if last_pos is not None:
            mgr.spawn_player(last_pos)
        mgr.restore_npc_states(self.npc_memory)

    def _enforce_level_limit(self):
        """
        Aplica límite de niveles cargados, descargando el más antiguo si se excede.
        """
        max_lvls = self.config.max_loaded_levels
        if len(self.maps) < max_lvls:
            return
        # Descartar un nivel distinto al actual (por orden de inserción)
        for name in list(self.maps):
            if name != self.current_level:
                del self.maps[name]
                break

    def save_world(self, path: Optional[str] = None):
        """
        Serializa estado global (NPCs, estado de niveles) a disco.
        """
        save_path = path or str(self.config.save_path)
        # Construir estado con zona y posición del jugador
        state = {}
        # Zona y posición del jugador
        if self.current_level and self.current_level in self.maps:
            pos = self.maps[self.current_level]._local_state.get("player_pos")
            state["player"] = {"level": self.current_level, "pos": list(pos) if pos is not None else None}
        # Memoria de NPCs y niveles
        state["npcs"] = self.npc_memory
        state["levels"] = {name: mgr.serialize_state() for name, mgr in self.maps.items()}
        save_world_state(save_path, state)

        # Actualizar autosave
        if self.config.autosave_enabled and save_path != str(self.config.save_path):
            save_world_state(str(self.config.save_path), state)

    def load_world(self, path: Optional[str] = None):
        """
        Carga desde disco el estado global y reconstruye niveles guardados.
        """
        load_path = path or str(self.config.save_path)
        data = load_world_state(load_path)
        self._apply_loaded_state(data)

    def _load_pending_level(self, level_name: str):
        """
        Carga un nivel previamente diferido sin cambiar current_level.
        """
        state = self._pending_levels.pop(level_name, None)
        if state is not None:
            mgr = MapManager(level_name)
            mgr.deserialize_state(state)
            self.maps[level_name] = mgr

# Nota: MapManager debe exponer serialize_state(), deserialize_state(), spawn_player() y restore_npc_states().