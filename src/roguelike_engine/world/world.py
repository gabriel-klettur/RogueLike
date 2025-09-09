from typing import Dict, Optional
from pathlib import Path
from datetime import datetime
import time
import logging
from roguelike_engine.world.world_config import WORLD_CONFIG
from roguelike_engine.world.models import WorldSnapshot, CURRENT_WORLD_SNAPSHOT_VERSION
from roguelike_engine.world.repository import JSONWorldRepository, IWorldRepository
from roguelike_engine.world.events import EventBus
from roguelike_engine.world.level_gateway import LevelGatewayFactory, DefaultLevelGatewayFactory, ILevelGateway

logger = logging.getLogger(__name__)


class WorldManager:
    """
    Orquesta múltiples MapManagers (niveles), mantiene el estado global persistente
    (NPCs, inventario) y gestiona carga/descarga de niveles.
    """
    def __init__(
        self,
        global_config=WORLD_CONFIG,
        load_state_on_init: bool = True,
        repository: Optional[IWorldRepository] = None,
        event_bus: Optional[EventBus] = None,
        level_factory: Optional[LevelGatewayFactory] = None,
    ):
        # Estado persistente de NPCs globales        
        self.npc_memory: Dict[str, dict] = {}
        # Estado persistente de inventario del jugador (serializado)
        self.player_inventory: Optional[dict] = None
        # Snapshots de inventarios de NPCs por instance_id
        self.npc_inventories: Optional[dict] = None
        # Ruta actual del archivo de guardado (slot). Si es None, usa config.save_path
        self.current_save_path: Optional[str] = None
        # Metadatos del guardado actual (nombre, timestamps, resumen)
        self.save_metadata: Optional[dict] = None
        # Configuración global (paths, límites de carga, etc.)
        self.config = global_config

        # Infraestructura inyectable
        self.repository: IWorldRepository = repository or JSONWorldRepository()
        self.events: EventBus = event_bus or EventBus()
        self.level_factory: LevelGatewayFactory = level_factory or DefaultLevelGatewayFactory()

        # Mapas cargados en memoria: nivel -> gateway de nivel
        self.maps: Dict[str, ILevelGateway] = {}
        # Pending levels para carga lazy: nombre -> estado serializado
        self._pending_levels: Dict[str, dict] = {}
        self.current_level: Optional[str] = None
        
        # Autosave scheduling
        self._next_autosave_time: Optional[float] = (
            time.time() + self.config.autosave_interval if self.config.autosave_enabled else None
        )

        # Si hay autosave/config, cargar el slot actual o el más reciente si existe
        if load_state_on_init and self.config.autosave_enabled:
            latest = self.repository.get_current_path(self.config.save_dir) or (
                str(self._find_latest_slot()) if self._find_latest_slot() is not None else None
            )
            if latest:
                try:
                    self.current_save_path = latest
                    data = self.repository.load_from_path(self.current_save_path)
                    self._apply_loaded_state(data)
                except FileNotFoundError:
                    pass

    def _apply_loaded_state(self, data: dict):
        """
        Aplica estado cargado parcialmente: NPCs y nivel actual.
        Guarda otros niveles para carga lazy.
        """
        # NPCs
        self.npc_memory = data.get("npcs", {})
        # Inventario del jugador (serializado)
        self.player_inventory = data.get("player_inventory")
        # Inventarios de NPCs (snapshots por instance_id)
        self.npc_inventories = data.get("npc_inventories")
        # Metadatos del guardado
        self.save_metadata = data.get("meta")
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
        # Log de inicio de carga de nivel
        try:
            logger.info(
                f"[World] Cargando nivel: {level_name} (maps_cargados={len(self.maps)}, pendientes={len(self._pending_levels)})"
            )
        except Exception:
            pass
        # Descargar exceso de niveles según max_loaded_levels
        self._enforce_level_limit()

        # Obtener o crear MapManager (lazy load si hay estado)
        if level_name not in self.maps:
            if getattr(self, '_pending_levels', None) and level_name in self._pending_levels:
                state = self._pending_levels.pop(level_name)
                mgr = self.level_factory.create(level_name)
                mgr.deserialize_state(state)
                self.maps[level_name] = mgr
            else:
                self.maps[level_name] = self.level_factory.create(level_name)
        self.current_level = level_name

        # Restaurar posición del jugador y NPCs globales
        mgr = self.maps[level_name]
        # Cargar posición previa de jugador desde estado local del mapa (si existe)
        last_pos = None
        try:
            local_state = getattr(mgr, "_local_state", None)
            if isinstance(local_state, dict):
                last_pos = local_state.get("player_pos")
        except Exception:
            last_pos = None
        if last_pos is not None:
            mgr.spawn_player(last_pos)
        mgr.restore_npc_states(self.npc_memory)
        # Log de fin de carga de nivel
        try:
            npc_for_level = 0
            try:
                npc_for_level = sum(1 for st in (self.npc_memory or {}).values() if st and st.get('level') == level_name)
            except Exception:
                npc_for_level = 0
            logger.info(
                f"[World] Nivel cargado: {level_name}, player_pos={last_pos}, npcs_en_estado={npc_for_level}"
            )
        except Exception:
            pass
        # Evento de nivel cargado
        try:
            self.events.publish("on_level_loaded", level_name)
        except Exception:
            pass

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
                try:
                    del self.maps[name]
                finally:
                    try:
                        self.events.publish("on_level_unloaded", name)
                    except Exception:
                        pass
                break

    def save_world(self, path: Optional[str] = None):
        """
        Serializa estado global (NPCs, estado de niveles) a disco.
        """
        save_path = path or self.current_save_path
        # Si no hay slot activo, crear uno nuevo con timestamp
        if not save_path:
            ts = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
            save_dir: Path = self.config.save_dir
            filename = f"partida_{ts}.json"
            snapshot = self._build_snapshot()
            save_path = self.repository.create_new_slot(save_dir, filename, snapshot)
            self.current_save_path = save_path
            try:
                self.events.publish("on_slot_changed", save_path)
            except Exception:
                pass
        else:
            # Construir snapshot y guardar en ruta existente
            snapshot = self._build_snapshot()
            # Notificar antes de guardar
            try:
                self.events.publish("on_before_save", snapshot.to_dict())
            except Exception:
                pass
            t0 = time.perf_counter()
            self.repository.save_to_path(save_path, snapshot)
            dt_ms = int((time.perf_counter() - t0) * 1000)
            try:
                self.events.publish("on_after_save", save_path, dt_ms)
            except Exception:
                pass
        # Log informativo del guardado
        try:
            levels_cnt = len(self.maps)
            npc_cnt = len(self.npc_memory or {})
            inv_cnt = len(self.npc_inventories or {})
            player_pos = None
            try:
                player_info = (self._build_snapshot().to_dict().get("player") or {})
                player_pos = player_info.get("pos")
            except Exception:
                player_pos = None
            logger.info(
                f"[World] Guardando mundo: path={save_path}, niveles={levels_cnt}, nivel_actual={self.current_level}, "
                f"npcs={npc_cnt}, npc_inventarios={inv_cnt}, player_pos={player_pos}"
            )
        except Exception:
            pass

        # Log de tamaño de archivo resultante
        try:
            size = Path(save_path).stat().st_size
            logger.info(f"[World] Guardado completado: bytes={size}")
        except Exception:
            pass

        # Actualizar autosave
        # En modo multi-slot no replicamos al archivo global por defecto

    def load_world(self, path: Optional[str] = None):
        """
        Carga desde disco el estado global y reconstruye niveles guardados.
        """
        load_path = path or self.current_save_path
        if not load_path:
            # Intento de fallback: usar el slot actual del índice o el más reciente si existe
            current = self.repository.get_current_path(self.config.save_dir)
            latest = current or (str(self._find_latest_slot()) if self._find_latest_slot() is not None else None)
            if latest is not None:
                load_path = latest
                try:
                    logger.info(f"[World] Cargando mundo usando slot: {load_path}")
                except Exception:
                    pass
            else:
                raise FileNotFoundError("No hay slot de guardado activo para cargar.")
        else:
            # Log explícito si el path es provisto o ya existe en current_save_path
            try:
                logger.info(f"[World] Cargando mundo desde: {load_path}")
            except Exception:
                pass
        # Recordar slot activo y actualizar índice
        self.current_save_path = load_path
        try:
            self.repository.set_current_path(self.config.save_dir, load_path)
        except Exception:
            pass
        try:
            self.events.publish("on_slot_changed", load_path)
        except Exception:
            pass
        data = self.repository.load_from_path(load_path)
        # Importante: al cargar un mundo desde disco, descartar mapas en memoria
        # para que el estado de disco (deserialize_state) se aplique al llamar a load_level().
        try:
            self.maps.clear()
        except Exception:
            self.maps = {}
        # Reiniciar tracking de niveles pendientes y nivel actual
        self._pending_levels = {}
        self.current_level = None
        # Aplicar nuevo estado
        self._apply_loaded_state(data)
        # Resumen de carga
        try:
            levels_cnt = len(data.get("levels", {}))
            npc_cnt = len(data.get("npcs", {}))
            inv_cnt = len(data.get("npc_inventories", {}) or {})
            logger.info(
                f"[World] Carga completada: niveles={levels_cnt}, nivel_actual={self.current_level}, npcs={npc_cnt}, npc_inventarios={inv_cnt}"
            )
        except Exception:
            pass

    def _load_pending_level(self, level_name: str):
        """
        Carga un nivel previamente diferido sin cambiar current_level.
        """
        state = self._pending_levels.pop(level_name, None)
        if state is not None:
            mgr = self.level_factory.create(level_name)
            mgr.deserialize_state(state)
            self.maps[level_name] = mgr

    def _find_latest_slot(self) -> Optional[Path]:
        """Retorna la ruta del archivo de guardado más reciente (partida_*.json) o None."""
        try:
            save_dir: Path = self.config.save_dir
            if not save_dir.exists():
                return None
            # Preferir índice si existe
            try:
                slots = self.repository.list_slots(save_dir)
                if slots:
                    # ordenar por mtime real del archivo si existe
                    slots.sort(key=lambda s: (s.path.stat().st_mtime if s.path.exists() else 0), reverse=True)
                    return slots[0].path
            except Exception:
                pass
            # Legacy: buscar por patrón si índice vacío
            candidates = list(save_dir.glob('partida_*.json'))
            if not candidates:
                return None
            candidates.sort(key=lambda p: p.stat().st_mtime, reverse=True)
            return candidates[0]
        except Exception:
            return None

    def _build_snapshot(self) -> WorldSnapshot:
        """Construye un WorldSnapshot con versionado para persistir."""
        state: Dict[str, object] = {}
        # Zona y posición del jugador
        if self.current_level and self.current_level in self.maps:
            pos = self.maps[self.current_level]._local_state.get("player_pos")
            state_player = {"level": self.current_level, "pos": list(pos) if pos is not None else None}
        else:
            state_player = None
        # Memoria de NPCs y niveles
        levels = {name: mgr.serialize_state() for name, mgr in self.maps.items()}
        snapshot = WorldSnapshot(
            version=CURRENT_WORLD_SNAPSHOT_VERSION,
            player=state_player,
            npcs=self.npc_memory,
            levels=levels,
            player_inventory=getattr(self, 'player_inventory', None),
            npc_inventories=getattr(self, 'npc_inventories', None),
            meta=getattr(self, 'save_metadata', None),
        )
        return snapshot

    def tick_autosave(self) -> None:
        """Invocar periódicamente desde el game loop para auto-guardar según configuración."""
        if not self.config.autosave_enabled:
            return
        if self._next_autosave_time is None:
            self._next_autosave_time = time.time() + self.config.autosave_interval
            return
        now = time.time()
        if now >= self._next_autosave_time:
            try:
                self.save_world()
            finally:
                self._next_autosave_time = now + self.config.autosave_interval

# Nota: El gestor de niveles debe cumplir el protocolo ILevelGateway (serialize_state(),
# deserialize_state(), spawn_player(), restore_npc_states()). Se inyecta a través de
# LevelGatewayFactory para desacoplar el paquete world del paquete del juego.