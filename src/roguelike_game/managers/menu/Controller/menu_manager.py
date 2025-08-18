import logging
from datetime import datetime
from pathlib import Path
import pygame

from .menu_handler import MenuHandler
from roguelike_ui.widgets.menu_renderer import MenuRenderer
from roguelike_ui.widgets.menu_configurator import MenuConfigurator
from roguelike_engine.world.persistence import load_world_state
from roguelike_game.ecs.components.experience_component import ExperienceComponent

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class MenuManager:
    """
    Orquesta la lógica, entrada y renderizado del menú.
    """
    def __init__(self, game, state, screen, input_config, font_size=36):
        # Referencias básicas
        self.game = game
        self.state = state
        self.screen = screen
        self.input_config = input_config

        # Componentes del menú
        self.renderer = MenuRenderer(font_size)
        self.configurator = MenuConfigurator(input_config, screen, self.renderer.font)
        self.handler = MenuHandler(state, input_config, self.configurator)

        # Flag para mostrar/ocultar menú y modo (start|pause|load_list)
        self.show_menu = False
        self.mode = "pause"
        self.prev_mode = "start"

        # Estado de lista de partidas
        self.save_entries: list[dict] = []  # cada entrada: {"path": str, "label": str, "meta": dict}
        self.load_selected = 0

    def handle_input(self, event):
        """
        Procesa la entrada del menú y devuelve la opción seleccionada o None.
        """
        # Modo especial: lista de partidas
        if self.mode == "load_list":
            if event.type == pygame.KEYDOWN:
                if event.key in (pygame.K_UP, pygame.K_w, pygame.K_a):
                    if self.save_entries:
                        self.load_selected = (self.load_selected - 1) % len(self.save_entries)
                elif event.key in (pygame.K_DOWN, pygame.K_s, pygame.K_d):
                    if self.save_entries:
                        self.load_selected = (self.load_selected + 1) % len(self.save_entries)
                elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                    self._load_selected_save()
                elif event.key in (pygame.K_ESCAPE,):
                    # Volver al menú anterior (start/pause)
                    self.set_mode(self.prev_mode)
            return None

        self.handler.mode = self.mode
        return self.handler.handle_input(event)

    def draw(self, screen):
        """
        Dibuja el menú y devuelve el rect para dirty rects.
        """
        # Vista especial: lista de partidas
        if self.mode == "load_list":
            items = [e["label"] for e in self.save_entries]
            meta = self.save_entries[self.load_selected]["meta"] if self.save_entries else {}
            detail_lines = self._format_save_details(meta)
            return self.renderer.draw_saves(screen, self.load_selected, items, detail_lines)

        self.handler.mode = self.mode
        options = self.handler.get_options()
        selected = self.handler.selected
        return self.renderer.draw(screen, selected, options)

    def execute_menu_option(self, selected, state):
        """
        Ejecuta la acción seleccionada en el menú.
        """
        # Opción 'Continuar': cerrar menú y reanudar juego
        if selected == "Continuar":
            self.show_menu = False
            return
        # Resto de acciones
        if selected == "Guardar partida":
            self._action_save()
        elif selected == "Nuevo juego":
            self._action_new_game()
        elif selected == "Cargar juego":
            # Cambiar a submenú de selección de partidas
            self._enter_load_list()
        elif selected == "Opciones":
            # Abrir configurador de botones (opciones)
            self.configurator.configure()
        else:
            # Delegar en handler para opciones existentes (modo, salir, configurar botones)
            self.handler.execute_option(selected)

    # ---- API de control ----
    def set_mode(self, mode: str):
        """Establece el modo del menú: 'start' o 'pause' o 'load_list'."""
        if mode not in ("start", "pause", "load_list"):
            logger.warning("Modo de menú desconocido: %s", mode)
            return
        self.mode = mode
        # Reiniciar selección al cambiar de menú
        self.handler.selected = 0

    # ---- Acciones ----
    def _action_save(self):
        """Guardar juego sin salir."""
        try:
            self.game.shutdown_manager.shutdown()
            logger.info("Partida guardada correctamente.")
        except Exception as e:
            logger.warning("Error al guardar partida: %s", e)

    def _action_new_game(self):
        """Inicia una partida nueva en memoria (sin borrar archivos)."""
        g = self.game
        try:
            # 1) Reset de estado del mundo en memoria: limpiar posición de jugador actual
            if hasattr(g, 'map') and hasattr(g.map, '_local_state'):
                g.map._local_state["player_pos"] = None
            # 2) Teletransportar jugador al centro del lobby
            off_x, off_y = g.map.lobby_offset
            from roguelike_engine.config.map_config import global_map_settings
            tx = off_x + global_map_settings.zone_width // 2
            ty = off_y + global_map_settings.zone_height // 2
            # Aplicar al mapa y a ECS
            g.map.spawn_player((tx, ty))
            # Convertir a píxeles y mover componente Position
            px, py = g.map.get_spawn_pixel((tx, ty))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            # 3) Resetear inventario del jugador a 10 monedas de oro
            try:
                from roguelike_game.ecs.components.inventory_component import InventoryComponent
                eid = g.ecs.ecs_world.player_entity
                inv = InventoryComponent(capacity=20, player_id="player")
                inv.add("gold", 10)
                g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
                # Reflejar en WorldManager para persistencia inmediata en próximos guardados
                if hasattr(g, 'world'):
                    g.world.player_inventory = inv.serialize()
            except Exception as e:
                logger.warning("No se pudo inicializar inventario de nuevo juego: %s", e)
            # 3c) Resetear experiencia/nivel del jugador a 0
            try:
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                xp_comp.xp = 0
                xp_comp.level = 0
                # xp_to_next_level se mantiene por defecto
            except Exception as e:
                logger.warning("No se pudo reiniciar experiencia de nuevo juego: %s", e)
            # 3b) Establecer un nuevo slot de guardado con nombre 'partida_YYYY-MM-DD_HH-MM-SS.json'
            try:
                ts = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
                save_dir: Path = g.world.config.save_dir
                save_dir.mkdir(parents=True, exist_ok=True)
                slot_path = save_dir / f"partida_{ts}.json"
                g.world.current_save_path = str(slot_path)
                # Preparar metadatos iniciales
                g.world.save_metadata = {
                    "name": f"partida_{ts}",
                    "created_at": datetime.now().isoformat(timespec='seconds'),
                    "last_played": datetime.now().isoformat(timespec='seconds'),
                }
            except Exception as e:
                logger.warning("No se pudo preparar slot de guardado: %s", e)
            # 4) Salir al juego
            self.show_menu = False
            # Asegurar modo pausa en siguientes aperturas
            self.mode = "pause"
            # Guardado inicial para crear archivo del slot
            try:
                g.shutdown_manager.shutdown()
            except Exception:
                pass
            logger.info("Nuevo juego iniciado (en memoria)")
        except Exception as e:
            logger.error("Error al iniciar nuevo juego: %s", e)

    def _action_load_game(self):
        """Carga partida desde el slot actual (modo legacy). Preferir _enter_load_list."""
        g = self.game
        try:
            # 1) Cargar estado mundial desde disco
            g.world.load_world()
            # Descubrir nivel actual guardado
            level = getattr(g.world, 'current_level', None)
            if not level:
                # Si no está resuelto aún, intentar desde player en pending
                pdata = getattr(g.world, '_pending_levels', {})
                # Fallback al nombre actual del mapa si no hay nada
                level = g.map.name
            # 2) Cargar nivel (MapManager) y asignarlo a game
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            # 3) Restaurar posición del jugador
            tile = g.map._local_state.get("player_pos")
            if tile is None:
                # Fallback: centro del lobby
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings
                tile = (
                    off_x + global_map_settings.zone_width // 2,
                    off_y + global_map_settings.zone_height // 2,
                )
                g.map.spawn_player(tile)
            px, py = g.map.get_spawn_pixel(tuple(tile))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            # 4) Restaurar inventario si fue guardado en world
            try:
                pdata = getattr(g.world, 'player_inventory', None)
                if pdata:
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent
                    inv = InventoryComponent(capacity=pdata.get('capacity', 20), player_id=pdata.get('player_id'))
                    for slot in pdata.get('slots', []):
                        if slot:
                            inv.add(slot['item'], slot.get('quantity', 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)
            # 4b) Restaurar XP/Nivel desde metadatos del guardado
            try:
                meta = getattr(g.world, 'save_metadata', {}) or {}
                p = meta.get('player', {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get('xp') is not None:
                    xp_comp.xp = int(p['xp'])
                if p.get('level') is not None:
                    xp_comp.level = int(p['level'])
                # Reflejar de vuelta en metadatos por coherencia
                meta.setdefault('player', {})
                meta['player']['xp'] = int(xp_comp.xp)
                meta['player']['level'] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
            # 5) Cerrar menú y pasar a modo pausa para próximas aperturas
            self.show_menu = False
            self.mode = "pause"
            logger.info("Partida cargada: nivel=%s", level)
        except Exception as e:
            logger.error("Error al cargar partida: %s", e)

    # ---- Load list helpers ----
    def _enter_load_list(self):
        """Prepara y entra al modo de lista de partidas guardadas."""
        self._refresh_save_list()
        self.load_selected = 0
        self.prev_mode = self.mode
        self.set_mode("load_list")

    def _refresh_save_list(self):
        """Escanea el directorio de guardados y construye entradas ordenadas por fecha reciente."""
        g = self.game
        save_dir: Path = g.world.config.save_dir
        save_dir.mkdir(parents=True, exist_ok=True)
        entries: list[dict] = []
        # Buscar archivos que empiecen con 'partida_' y terminen en .json
        for path in sorted(save_dir.glob('partida_*.json'), reverse=True):
            try:
                data = load_world_state(str(path))
            except Exception:
                data = {}
            meta = data.get("meta") or {}
            label = meta.get("name") or path.stem
            entries.append({"path": str(path), "label": label, "meta": meta})
        self.save_entries = entries

    def _format_save_details(self, meta: dict) -> list[str]:
        """Construye líneas de detalle para el panel de info de guardado."""
        if not meta:
            return ["Sin metadatos", "Pulsa Enter para cargar"]
        lines = []
        lines.append(f"Nombre: {meta.get('name', '-')}")
        lines.append(f"Creada: {meta.get('created_at', '-')}")
        lines.append(f"Última vez: {meta.get('last_played', '-')}")
        p = meta.get('player', {}) or {}
        lines.append(f"Nivel: {p.get('level', '-')}")
        lines.append(f"XP: {p.get('xp', '-')}")
        it = meta.get('items_summary', {}) or {}
        lines.append(f"Pilas: {it.get('stacks', 0)}")
        top = it.get('top_items') or []
        if top:
            lines.append("Items: " + ", ".join([str(x) for x in top]))
        return lines

    def _load_selected_save(self):
        """Carga el save seleccionado de la lista y entra al juego."""
        if not self.save_entries:
            return
        entry = self.save_entries[self.load_selected]
        path = entry["path"]
        g = self.game
        try:
            # Cargar mundo desde path específico y recordar slot activo
            g.world.load_world(path)
            # Determinar nivel actual y cargarlo
            level = getattr(g.world, 'current_level', None) or g.map.name
            g.world.load_level(level)
            g.map = g.world.maps[level]
            g.world.current_level = level
            # Restaurar posición del jugador
            tile = g.map._local_state.get("player_pos")
            if tile is None:
                off_x, off_y = g.map.lobby_offset
                from roguelike_engine.config.map_config import global_map_settings
                tile = (
                    off_x + global_map_settings.zone_width // 2,
                    off_y + global_map_settings.zone_height // 2,
                )
                g.map.spawn_player(tile)
            px, py = g.map.get_spawn_pixel(tuple(tile))
            try:
                eid = g.ecs.ecs_world.player_entity
                pos = g.ecs.ecs_world.components["Position"][eid]
                pos.x, pos.y = px, py
            except Exception:
                pass
            # Restaurar inventario
            try:
                pdata = getattr(g.world, 'player_inventory', None)
                if pdata:
                    from roguelike_game.ecs.components.inventory_component import InventoryComponent
                    inv = InventoryComponent(capacity=pdata.get('capacity', 20), player_id=pdata.get('player_id'))
                    for slot in pdata.get('slots', []):
                        if slot:
                            inv.add(slot['item'], slot.get('quantity', 0))
                    eid = g.ecs.ecs_world.player_entity
                    g.ecs.ecs_world.components.setdefault("InventoryComponent", {})[eid] = inv
            except Exception as e:
                logger.warning("No se pudo restaurar inventario: %s", e)
            # Restaurar XP/Nivel desde metadatos del guardado
            try:
                meta = getattr(g.world, 'save_metadata', {}) or {}
                p = meta.get('player', {}) or {}
                eid = g.ecs.ecs_world.player_entity
                xp_comp = g.ecs.ecs_world.components.setdefault("ExperienceComponent", {}).get(eid)
                if xp_comp is None:
                    xp_comp = ExperienceComponent()
                    g.ecs.ecs_world.components.setdefault("ExperienceComponent", {})[eid] = xp_comp
                if p.get('xp') is not None:
                    xp_comp.xp = int(p['xp'])
                if p.get('level') is not None:
                    xp_comp.level = int(p['level'])
                # Reflejar de vuelta en metadatos por coherencia
                meta.setdefault('player', {})
                meta['player']['xp'] = int(xp_comp.xp)
                meta['player']['level'] = int(xp_comp.level)
                g.world.save_metadata = meta
                logger.info("XP restaurada: level=%s, xp=%s", xp_comp.level, xp_comp.xp)
            except Exception as e:
                logger.warning("No se pudo restaurar experiencia: %s", e)
            # Cerrar menú y dejarlo en modo pausa para próximas aperturas
            self.show_menu = False
            self.mode = "pause"
            logger.info("Partida cargada desde %s", path)
        except Exception as e:
            logger.error("Error al cargar partida desde lista: %s", e)
