from roguelike_engine.utils.benchmark import benchmark
import pygame
import types
import logging

logger = logging.getLogger(__name__)



def update_game(
    state,
    camera,
    clock,
    screen,
    map,
    buildings,
    tiles_editor,
    buildings_editor,
    map_editor,
    minimap,
    ecs,
    perf_log,
    item_editor=None,
):
    """
    Actualiza el juego en cada frame, incluyendo:
      1) Lógica de editores (tiles/buildings)
      2) Mecánicas core: cámara, sistemas, enemigos, jugador...
    """
    if not state.running:
        return

    # Prioritized editor steps: execute first active and return
    def _step_tiles_editor():
        tiles_editor.update(camera, map)

    def _step_buildings_editor():
        buildings_editor.update(camera)

    def _step_map_editor():
        map_editor.update(camera, map)
        # Free camera panning with arrow keys
        keys = pygame.key.get_pressed()
        dx = int(keys[pygame.K_RIGHT]) - int(keys[pygame.K_LEFT])
        dy = int(keys[pygame.K_DOWN]) - int(keys[pygame.K_UP])
        # Base speed and speed boost with Shift
        base_speed = 10
        shift = keys[pygame.K_LSHIFT] or keys[pygame.K_RSHIFT]
        pan_speed = base_speed * (3 if shift else 1)
        camera.offset_x += dx * pan_speed
        camera.offset_y += dy * pan_speed

    editor_steps = [
        ("2.0.1.tiles_editor.update", _step_tiles_editor, tiles_editor.editor_state.active),
        ("2.0.2.buildings_editor.update", _step_buildings_editor, buildings_editor.editor_state.active),
        ("2.0.3.map_editor.update", _step_map_editor, map_editor.editor_state.active),
    ]

    for key, fn, cond in editor_steps:
        if cond:
            @benchmark(perf_log, key)
            def _run_editor(sfn=fn):
                sfn()
            _run_editor()
            # If the Tiles Editor is active, still run ECS update so physics reflects edits in runtime
            if key == "2.0.1.tiles_editor.update":
                try:
                    @benchmark(perf_log, "2.2.ecs.update[while_tiles_editor]")
                    def _run_ecs_update():
                        ecs.ecs_world.update(camera)
                    _run_ecs_update()
                except Exception:
                    pass
            # If the Buildings Editor is active, rebuild SpatialIndex only when needed (dirty or throttled)
            # and run ECS update on those rebuild frames to avoid FPS drops.
            if key == "2.0.2.buildings_editor.update":
                try:
                    # Throttle parameters
                    ticks = pygame.time.get_ticks()
                    be_state = getattr(buildings_editor, 'editor_state', None)
                    last_ms = int(getattr(be_state, 'last_colliders_rebuild_ms', 0)) if be_state else 0
                    interval = int(getattr(be_state, 'colliders_rebuild_interval_ms', 120)) if be_state else 120
                    dirty = bool(getattr(be_state, 'colliders_dirty', False)) if be_state else False
                    # Avoid per-frame logs: only log on actual rebuilds below

                    try:
                        # Ensure ECSWorld references the current buildings list from the manager
                        ecs.ecs_world.buildings = getattr(buildings, 'buildings', ecs.ecs_world.buildings)
                    except Exception:
                        pass
                    due = (ticks - last_ms) >= interval
                    if due:
                        # Rebuild at most once per interval; emit INFO only once per dirty cycle
                        try:
                            ecs.ecs_world._log_rebuild_info = bool(dirty)
                        except Exception:
                            pass
                        if dirty and not bool(getattr(be_state, '_colliders_dirty_logged', False)):
                            logger.info("[COLLIDERS][BE] Rebuild SpatialIndex (reason=dirty)")
                            try:
                                be_state._colliders_dirty_logged = True
                            except Exception:
                                pass
                        ecs.ecs_world.rebuild_spatial_index()
                        # Clear dirty flag and stamp last rebuild time
                        try:
                            if be_state is not None:
                                be_state.colliders_dirty = False
                                be_state.last_colliders_rebuild_ms = ticks
                                # Reset log gate for next dirty cycle
                                be_state._colliders_dirty_logged = False
                        except Exception:
                            pass
                        @benchmark(perf_log, "2.2.ecs.update[while_buildings_editor]")
                        def _run_ecs_update():
                            ecs.ecs_world.update(camera)
                        _run_ecs_update()
                except Exception:
                    pass
            # Global hot-reload: if Buildings Editor marked colliders dirty, ensure rebuild+ECS update
            # even if another editor (e.g., Tiles or Map) is the active one this frame.
            try:
                be_state = getattr(buildings_editor, 'editor_state', None)
                if be_state is not None and bool(getattr(be_state, 'colliders_dirty', False)):
                    ticks = pygame.time.get_ticks()
                    last_ms = int(getattr(be_state, 'last_colliders_rebuild_ms', 0))
                    interval = int(getattr(be_state, 'colliders_rebuild_interval_ms', 120))
                    # No per-frame logs here; only log on rebuild below
                    if ticks - last_ms >= interval:
                        try:
                            ecs.ecs_world.buildings = getattr(buildings, 'buildings', ecs.ecs_world.buildings)
                        except Exception:
                            pass
                        logger.info("[COLLIDERS][GLOBAL] Rebuild SpatialIndex (post-other-editor, interval_ms=%d)", interval)
                        ecs.ecs_world.rebuild_spatial_index()
                        try:
                            be_state.colliders_dirty = False
                            be_state.last_colliders_rebuild_ms = ticks
                        except Exception:
                            pass
                        @benchmark(perf_log, "2.2.ecs.update[after_rebuild]")
                        def _run_ecs_update():
                            ecs.ecs_world.update(camera)
                        _run_ecs_update()
            except Exception:
                pass
            # Early return when an editor is active (after optional ECS update for tiles editor)
            return

    # If no editor consumed the frame, still react to collider edits made via Buildings Editor UI.
    # Rebuild immediately so gameplay colliders are up-to-date without requiring restart.
    try:
        be_state = getattr(buildings_editor, 'editor_state', None)
        if be_state is not None and bool(getattr(be_state, 'colliders_dirty', False)):
            try:
                ecs.ecs_world.buildings = getattr(buildings, 'buildings', ecs.ecs_world.buildings)
            except Exception:
                pass
            logger.info("[COLLIDERS][IDLE] Rebuild SpatialIndex (no editor consumed frame)")
            ecs.ecs_world.rebuild_spatial_index()
            try:
                buildings_editor.editor_state.colliders_dirty = False
                buildings_editor.editor_state.last_colliders_rebuild_ms = pygame.time.get_ticks()
            except Exception:
                pass
    except Exception:
        pass

    # 3.1) Cámara sigue al jugador si está vivo (tiene Position),
    #      salvo cuando el Items Editor está reteniendo enfoque manual (hold-focus)
    def _step_camera():
        try:
            # Defer camera follow for N frames when requested (e.g., after MMB pan)
            if getattr(state, 'defer_follow_frames', 0) > 0:
                state.defer_follow_frames -= 1
                return
        except Exception:
            pass
        # Mientras el Particles Editor esté visible, no seguir al jugador (conservar la posición donde se soltó MMB)
        try:
            if bool(getattr(state, 'particles_editor_visible', False)):
                return
        except Exception:
            pass
        try:
            # Respetar defer de follow tras salir del Map Editor
            if getattr(getattr(map_editor, 'editor_state', None), 'defer_follow_frames', 0) > 0:
                map_editor.editor_state.defer_follow_frames -= 1
                return
        except Exception:
            pass
        try:
            if item_editor is not None and getattr(getattr(item_editor, 'model', None), 'holding_pos_focus', False):
                # Respetar enfoque manual del editor: no seguir jugador
                return
        except Exception:
            pass
        # No recentrar mientras hay editores overlay visibles (Item, Spawner, FSM)
        try:
            if item_editor is not None and getattr(getattr(item_editor, 'model', None), 'visible', False):
                return
        except Exception:
            pass
        try:
            import roguelike_engine.config.config as cfg
            if bool(getattr(cfg, 'DEBUG_SPAWNER', False)):
                return
            if bool(getattr(cfg, 'DEBUG_ENTITIES', False)):
                return
        except Exception:
            pass
        # Mientras el usuario arrastra con MMB, no recentrar la cámara al jugador
        try:
            if getattr(state, 'mmb_panning', False):
                return
        except Exception:
            pass
        # Respetar enfoque manual del Spawner Editor (hold-focus)
        try:
            if getattr(getattr(ecs, 'ecs_world', None), 'state', None) is not None:
                st = ecs.ecs_world.state
                if getattr(st, 'spawner_hold_focus', False):
                    return
        except Exception:
            pass
        eid = ecs.ecs_world.player_entity
        pos_map = ecs.ecs_world.components.get('Position', {})
        if eid in pos_map:
            pos = pos_map[eid]
            camera.update(types.SimpleNamespace(x=pos.x, y=pos.y))

    def _step_entities():
        buildings.update(state, map, perf_log)

    def _step_minimap():
        # Actualizar minimapa solo si el jugador todavía existe (tiene Position)
        eid = ecs.ecs_world.player_entity
        pos_map = ecs.ecs_world.components.get('Position', {})
        if eid in pos_map:
            pos = pos_map[eid]
            minimap.update(
                player_pos=(pos.x, pos.y),
                tiles=map.tiles_in_region,
                buildings=getattr(buildings, 'buildings', None),
                world=ecs.ecs_world,
            )

    steps = [
        ("2.1.camera.update", _step_camera),
        ("2.3.entities.update", _step_entities),
        ("2.5.minimap.update", _step_minimap),
    ]

    for key, fn in steps:
        @benchmark(perf_log, key)
        def _run(sfn=fn):
            sfn()
        _run()
