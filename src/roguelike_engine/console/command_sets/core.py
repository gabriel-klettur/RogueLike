"""
Comandos core de la consola (help, echo, quit, placeholders).
"""
from __future__ import annotations
import pygame
import time
from typing import TYPE_CHECKING, Any, Optional
from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Item as ItemRow
from roguelike_game.factories.player.loader import (
    load_and_scale_sprites,
    extract_initial_frame,
    build_animator_map,
    build_masks_map,
)
from roguelike_game.factories.player.config import (
    ANIMATION_INTERVAL,
    INITIAL_ANIMATION_STATE,
    DEFAULT_CLASS,
)
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.rendering.animator import Animator
from roguelike_game.ecs.components.rendering.animation_timer import AnimationTimer
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_engine.config.config_z_layer import Z_LAYERS
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.managers.ecs.particles_loader import (
    refresh_particles_from_world as _refresh_particles_from_world,
)

if TYPE_CHECKING:  # solo para type hints sin dependencias en runtime
    from roguelike_engine.console.console_model import CommandRegistry


def register_core_commands(registry: 'CommandRegistry', game: Optional[Any] = None) -> None:
    """Registra comandos básicos y placeholders de sistema/entidades."""
    # --- CORE ---
    def help_cmd(*args: str) -> str:
        # help general
        if not args:
            # Agrupar por categoría
            cats = {}
            for name, meta in registry.metas.items():
                cat = meta.category or 'core'
                cats.setdefault(cat, []).append(name)
            lines = []
            for cat in sorted(cats.keys()):
                cmds = ', '.join(sorted(cats[cat]))
                lines.append(f"[{cat}] {cmds}")
            lines.append("\nUsa: help <comando> para detalles")
            return "\n".join(lines)
        # help de un comando en concreto (resuelve alias)
        q = args[0]
        primary = registry.alias_to_name.get(q, q)
        meta = registry.metas.get(primary)
        if not meta:
            return f"Comando desconocido: {q}"
        lines = [f"{primary}"]
        if meta.usage:
            lines.append(f"Uso: {meta.usage}")
        if meta.help:
            lines.append(meta.help)
        if meta.aliases:
            lines.append(f"Alias: {', '.join(meta.aliases)}")
        if meta.category:
            lines.append(f"Categoría: {meta.category}")
        return "\n".join(lines)

    registry.register(
        'help', help_cmd,
        usage='help [comando]',
        help='Muestra ayuda general o detallada para un comando.',
        category='core',
        aliases=['?', '/help']
    )

    registry.register(
        'echo', lambda *a: ' '.join(a),
        usage='echo <texto...>',
        help='Imprime el texto dado.',
        category='core'
    )
    registry.register(
        'quit', lambda: pygame.event.post(pygame.event.Event(pygame.QUIT)) or '',
        usage='quit',
        help='Cierra el juego (lanza evento QUIT).',
        category='core'
    )

    # --- SYSTEM ---
    def _set_godmode(val: bool) -> str:
        # Set flag tanto en game.state como en world.state (por seguridad)
        try:
            if game is not None:
                setattr(getattr(game, 'state', game), 'godmode', bool(val))
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'godmode', bool(val))
        except Exception:
            pass
        return 'godmode on' if val else 'godmode off'

    def godmode_cmd(*args: str) -> str:
        # toggle por defecto; acepta on/off/true/false/1/0/toggle
        current = False
        try:
            if game is not None:
                # Leer de world.state si existe, si no de game.state
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    current = bool(getattr(world.state, 'godmode', False))
                else:
                    current = bool(getattr(getattr(game, 'state', game), 'godmode', False))
        except Exception:
            current = False
        val = None
        if args:
            a0 = str(args[0]).lower()
            if a0 in ('on','1','true','yes','y'):
                val = True
            elif a0 in ('off','0','false','no','n'):
                val = False
            elif a0 in ('toggle','switch'):
                val = not current
        if val is None:
            val = not current
        return _set_godmode(val)

    registry.register(
        'godmode', godmode_cmd,
        usage='godmode [on|off|toggle]',
        help='Activa/desactiva modo dios: sin coste de maná, invulnerabilidad total y one-shot en ataques del jugador, además de dash infinito.',
        category='system',
        aliases=['/godmode']
    )

    # --- CHEATS / UTILIDADES ---
    def givememoney_cmd(*args: str) -> str:
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            player_eid = getattr(world, 'player_entity', None) if world else None
            inv_store = world.components.get('InventoryComponent', {}) if world else {}
            inv = inv_store.get(player_eid)
            if not inv:
                return 'Inventario no disponible'
            inv.add('gold', 100)
            return 'Añadidos 100x gold'
        except Exception:
            return 'Error al añadir gold'

    registry.register(
        'givememoney', givememoney_cmd,
        usage='givememoney',
        help='Añade 100 de gold al inventario del jugador.',
        category='cheats',
        aliases=['/givememoney']
    )

    def restockvendorfood_cmd(*args: str) -> str:
        """Añade N unidades de cada item con type='food' al vendor indicado o al target actual.

        Uso:
          - restockvendorfood <vendor_name|current> [cantidad]
          - Si <vendor_name> es 'current' intenta usar el target actual del chat.
        """
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world is None:
                return 'World no disponible'
            qty = 100
            vendor_eid = None
            if args:
                key = str(args[0]).strip().lower()
            else:
                key = 'current'
            if len(args) >= 2:
                try:
                    qty = max(1, int(args[1]))
                except Exception:
                    qty = 100
            if key in {'current', 'here'}:
                try:
                    vendor_eid = getattr(getattr(world, 'state', None), 'chat_target_eid', None)
                except Exception:
                    vendor_eid = None
            if vendor_eid is None:
                # Buscar por nombre en Identity
                idents = world.components.get('Identity', {})
                for eid, ident in idents.items():
                    try:
                        nm = str(getattr(ident, 'name', '')).lower()
                        if not nm:
                            continue
                        if key in nm:
                            vendor_eid = eid
                            break
                    except Exception:
                        continue
            if vendor_eid is None:
                return f"Vendor no encontrado: {key}"
            inv_store = world.components.get('InventoryComponent', {})
            inv = inv_store.get(vendor_eid)
            if not inv:
                return 'El vendor no tiene inventario'
            # Obtener todos los items type='food' desde SQLite
            food_ids: list[str] = []
            try:
                with session_scope() as s:
                    rows = s.query(ItemRow).filter(ItemRow.type == 'food').all()  # type: ignore[attr-defined]
                    for r in rows:
                        try:
                            iid = str(getattr(r, 'id'))
                            if iid:
                                food_ids.append(iid)
                        except Exception:
                            continue
                # Fallback adicional: si la columna type falla, usar prefijo 'food_'
                if not food_ids:
                    with session_scope() as s:
                        rows = s.query(ItemRow).all()
                        for r in rows:
                            try:
                                iid = str(getattr(r, 'id'))
                                if iid.startswith('food_'):
                                    food_ids.append(iid)
                            except Exception:
                                continue
            except Exception:
                return 'Error consultando items en SQLite'
            added = 0
            for iid in food_ids:
                try:
                    if inv.add(iid, qty):
                        added += 1
                except Exception:
                    continue
            return f"Restock OK: añadidos {qty} de cada uno de {added} items de tipo food."
        except Exception:
            return 'Error en restockvendorfood'

    registry.register(
        'restockvendorfood', restockvendorfood_cmd,
        usage='restockvendorfood <vendor_name|current> [cantidad]',
        help='Añade N unidades de todos los items con type=food al vendor indicado (o al target actual).',
        category='cheats',
        aliases=['/restockvendorfood']
    )

    # --- PLAYER UTILS ---
    def vida_cmd(*args: str) -> str:
        """Rellena la vida (HP) del jugador al máximo."""
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world is None:
                return 'World no disponible'
            player_eid = getattr(world, 'player_entity', None)
            if player_eid is None:
                return 'Jugador no disponible'
            comps = world.components
            # Determinar HP máximo desde Health o CombatStats
            hp_cmp = comps.get('Health', {}).get(player_eid)
            cs_cmp = comps.get('CombatStats', {}).get(player_eid)
            max_hp = None
            if hp_cmp is not None:
                max_hp = getattr(hp_cmp, 'max_hp', None)
            if max_hp is None and cs_cmp is not None:
                max_hp = getattr(cs_cmp, 'max_hp', None)
            if max_hp is None:
                return 'No se pudo determinar max HP'
            # Usar helper para soportar Health o CombatStats
            try:
                from roguelike_game.ecs.utils.health_utils import set_current_hp
                ok = bool(set_current_hp(world, player_eid, int(max_hp)))
            except Exception:
                # Fallback directo
                ok = False
                try:
                    if hp_cmp is not None:
                        hp_cmp.current_hp = int(max_hp)
                        ok = True
                    elif cs_cmp is not None:
                        cs_cmp.current_hp = int(max_hp)
                        ok = True
                except Exception:
                    ok = False
            return 'HP al máximo' if ok else 'No se pudo rellenar HP'
        except Exception:
            return 'Error al procesar vida'

    registry.register(
        'vida', vida_cmd,
        usage='vida',
        help='Rellena la vida del jugador al máximo.',
        category='cheats',
        aliases=['/vida']
    )

    def mana_cmd(*args: str) -> str:
        """Rellena el maná del jugador al máximo."""
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world is None:
                return 'World no disponible'
            player_eid = getattr(world, 'player_entity', None)
            if player_eid is None:
                return 'Jugador no disponible'
            mana_cmp = world.components.get('Mana', {}).get(player_eid)
            if not mana_cmp:
                return 'Componente Mana no disponible'
            max_mana = getattr(mana_cmp, 'max_mana', None)
            if max_mana is None:
                return 'No se pudo determinar max Mana'
            try:
                mana_cmp.current_mana = int(max_mana)
                return 'Maná al máximo'
            except Exception:
                return 'No se pudo rellenar maná'
        except Exception:
            return 'Error al procesar maná'

    registry.register(
        'mana', mana_cmd,
        usage='mana',
        help='Rellena el maná del jugador al máximo.',
        category='cheats',
        aliases=['/mana']
    )

    def resurrect_cmd(*args: str) -> str:
        """Resucita al jugador: limpia estados de muerte, restaura HP y reestablece animaciones."""
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world is None:
                return 'World no disponible'
            player_eid = getattr(world, 'player_entity', None)
            if player_eid is None:
                return 'Jugador no disponible'
            comps = world.components
            # Limpiar flags/estados de muerte
            comps.get('GrayscaleComponent', {}).pop(player_eid, None)
            comps.get('DeathTimer', {}).pop(player_eid, None)
            comps.get('DyingTag', {}).pop(player_eid, None)

            # Restaurar HP al máximo
            hp_cmp = comps.get('Health', {}).get(player_eid)
            cs_cmp = comps.get('CombatStats', {}).get(player_eid)
            max_hp = None
            if hp_cmp is not None:
                max_hp = getattr(hp_cmp, 'max_hp', None)
            if max_hp is None and cs_cmp is not None:
                max_hp = getattr(cs_cmp, 'max_hp', None)
            if max_hp is not None:
                try:
                    from roguelike_game.ecs.utils.health_utils import set_current_hp
                    set_current_hp(world, player_eid, int(max_hp))
                except Exception:
                    try:
                        if hp_cmp is not None:
                            hp_cmp.current_hp = int(max_hp)
                        elif cs_cmp is not None:
                            cs_cmp.current_hp = int(max_hp)
                    except Exception:
                        pass

            # Reconstruir Sprite/Animator/AnimationTimer (Death/Unconscious los habían eliminado)
            try:
                pt = comps.get('PlayerTagComponent', {}).get(player_eid)
                class_name = getattr(pt, 'class_name', None) or DEFAULT_CLASS
                sprites_dict = load_and_scale_sprites(class_name)
                frame = extract_initial_frame(sprites_dict)
                if frame is not None:
                    comps.setdefault('Sprite', {})
                    comps['Sprite'][player_eid] = Sprite(frame)
                comps.setdefault('Animator', {})
                comps['Animator'][player_eid] = Animator(
                    animations=build_animator_map(sprites_dict),
                    current_state=INITIAL_ANIMATION_STATE,
                    masks=build_masks_map(sprites_dict),
                )
                comps.setdefault('AnimationTimer', {})
                comps['AnimationTimer'][player_eid] = AnimationTimer(
                    last_time=0.0, interval=ANIMATION_INTERVAL
                )
            except Exception:
                # No bloquear resurrección por fallos de assets; el sistema de animación puede seguir con Sprite actual
                pass

            # Asegurar capa Z adecuada del jugador
            comps.setdefault('ZLayer', {})[player_eid] = ZLayer(Z_LAYERS.get('player', 4))

            # Cambiar FSM a IdleState si existe NPCState (después de restaurar animaciones)
            try:
                npc_state = comps.get('NPCState', {}).get(player_eid)
                if npc_state and getattr(npc_state, 'fsm', None):
                    from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
                    npc_state.fsm.change_state(IdleState(), type('E', (), {'world': world, 'id': player_eid}))
            except Exception:
                pass
            return 'Jugador resucitado'
        except Exception:
            return 'Error al resucitar'

    registry.register(
        'resurrect', resurrect_cmd,
        usage='resurrect',
        help='Resucita al jugador (limpia estados de muerte y restaura HP).',
        category='cheats',
        aliases=['/resurrect']
    )

    def teleport_cmd(*args: str) -> str:
        """Teletransporta al jugador a un tile (tile_x, tile_y) en el mundo indicado o actual."""
        # Requiere contexto de juego
        if game is None:
            return 'Teleport no disponible (contexto no inicializado)'
        # Obtener mundo ECS y map_manager
        ecs = getattr(game, 'ecs', None)
        world = getattr(ecs, 'ecs_world', None) if ecs is not None else None
        if world is None or not hasattr(world, 'map_manager'):
            return 'World/map_manager no disponible'
        cur_world = getattr(global_map_settings, 'current_world', 'base')
        if not args:
            return 'Uso: teleport <world> <tile_x> <tile_y> | teleport <tile_x> <tile_y>'
        dest_world = None
        tile_pos = None
        # Sintaxis corta: teleport <tile_x> <tile_y>  (mundo actual)
        if len(args) == 2:
            try:
                tx = int(args[0])
                ty = int(args[1])
            except Exception:
                return 'Coordenadas inválidas: espera enteros tile_x tile_y'
            dest_world = cur_world
            tile_pos = (tx, ty)
        # Sintaxis larga: teleport <world> <tile_x> <tile_y>
        elif len(args) >= 3:
            dest_world = args[0]
            try:
                tx = int(args[1])
                ty = int(args[2])
            except Exception:
                return 'Coordenadas inválidas: espera enteros tile_x tile_y'
            tile_pos = (tx, ty)
        else:
            # teleport <world>  -> ir al spawn por defecto del mundo
            dest_world = args[0]
            tile_pos = None
        dest_world = dest_world or cur_world
        try:
            # Cross-world
            if dest_world != cur_world:
                world.map_manager.swap_world_and_spawn(dest_world, tile_pos)
                # Refrescar partículas y marcar índice espacial para reconstrucción
                try:
                    _refresh_particles_from_world(world)
                except Exception:
                    pass
                try:
                    world.invalidate_spatial_index()
                except Exception:
                    pass
                return f"Teleport OK: {cur_world} -> {dest_world} tile={tile_pos}"
            # Intra-world
            if tile_pos is None:
                # Fallback: usar spawn por defecto del mundo actual
                world.map_manager.swap_world_and_spawn(cur_world, None)
            else:
                world.map_manager.spawn_player(tile_pos)
            try:
                world.invalidate_spatial_index()
            except Exception:
                pass
            return f"Teleport OK: {dest_world} tile={tile_pos}"
        except Exception:
            return 'Error al procesar teleport'

    registry.register(
        'teleport', teleport_cmd,
        usage='teleport <world> <tile_x> <tile_y> | teleport <tile_x> <tile_y>',
        help='Teletransporta al jugador a un tile (tile_x,tile_y) en el mundo indicado o en el mundo actual.',
        category='cheats',
        aliases=['/teleport']
    )

    # --- PLACEHOLDERS de otras áreas (pueden migrar a sus módulos) ---
    for cmd in ['spawn','kill','listentities']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='entities')
    for cmd in ['setvar','getvar','listvars']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='vars')
    for cmd in ['save','load']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='io')
    for cmd in ['pause','resume','noclip']:
        registry.register(cmd, lambda *a, name=cmd: f"[{name}] implementado próximamente.", category='system')
