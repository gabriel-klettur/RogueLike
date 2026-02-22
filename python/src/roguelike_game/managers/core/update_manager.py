from roguelike_engine.utils.benchmark.benchmark_groups import BenchmarkGroup
import pygame
import types
import logging
try:
    from roguelike_ui.hud.orchestrator.hud_orchestrator import HudOrchestrator
except Exception:
    HudOrchestrator = None

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

    root_group = BenchmarkGroup(perf_log, "2")
    editors_group = root_group.subgroup("0")
    ecs_group = root_group.subgroup("2")
    camera_group = root_group.subgroup("1")
    entities_group = root_group.subgroup("3")
    hud_group = root_group.subgroup("45")
    minimap_group = root_group.subgroup("5")

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
        ("1.tiles_editor.update", _step_tiles_editor, tiles_editor.editor_state.active),
        ("2.buildings_editor.update", _step_buildings_editor, buildings_editor.editor_state.active),
        ("3.map_editor.update", _step_map_editor, map_editor.editor_state.active),
    ]

    for name, fn, cond in editor_steps:
        if cond:
            @editors_group.bench(name)
            def _run_editor(sfn=fn):
                sfn()
            _run_editor()
            # If the Tiles Editor is active, still run ECS update so physics reflects edits in runtime
            if fn is _step_tiles_editor:
                try:
                    @ecs_group.bench("ecs.update[while_tiles_editor]")
                    def _run_ecs_update():
                        ecs.ecs_world.update(camera)
                    _run_ecs_update()
                except Exception:
                    pass
            # If the Buildings Editor is active, rebuild SpatialIndex only when there are pending collider changes
            # (colliders_dirty) and the throttle interval elapsed. Then run ECS update on those rebuild frames.
            if fn is _step_buildings_editor:
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
                    if dirty and due:
                        # Rebuild at most once per interval; emit INFO only once per dirty cycle
                        try:
                            ecs.ecs_world._log_rebuild_info = bool(dirty)
                        except Exception:
                            pass
                        if not bool(getattr(be_state, '_colliders_dirty_logged', False)):
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
                        @ecs_group.bench("ecs.update[while_buildings_editor]")
                        def _run_ecs_update():
                            ecs.ecs_world.update(camera)
                        _run_ecs_update()
                    # If not dirty, skip rebuild/update entirely to avoid unnecessary cost
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
                        @ecs_group.bench("ecs.update[after_rebuild]")
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
            # Ensure world state reflects new spatial index immediately in idle frames
            @ecs_group.bench("ecs.update[after_rebuild]")
            def _run_ecs_update():
                ecs.ecs_world.update(camera)
            _run_ecs_update()
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

    def _step_hud_update():
        """Update HUD orchestrator state before minimap (compute pressed, layout)."""
        try:
            orch = getattr(ecs, 'hud_orchestrator', None)
            if orch is None:
                if HudOrchestrator is not None:
                    try:
                        orch = HudOrchestrator(minimap=minimap, systems=None)
                        setattr(ecs, 'hud_orchestrator', orch)
                    except Exception:
                        orch = None
            if orch is not None:
                try:
                    world = getattr(ecs, 'ecs_world', None)
                except Exception:
                    world = None
                orch.update(world=world, screen=screen, camera=camera)
        except Exception:
            # HUD is optional; never break update loop
            pass

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
        (camera_group, "camera.update", _step_camera),
        (entities_group, "entities.update", _step_entities),
        (hud_group, "hud.update", _step_hud_update),
        (minimap_group, "minimap.update", _step_minimap),
    ]

    for grp, name, fn in steps:
        @grp.bench(name)
        def _run(sfn=fn):
            sfn()
        _run()
