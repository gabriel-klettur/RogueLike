import pygame
import pytest


def _make_manager(monkeypatch, surface_factory, camera):
    """Create a minimal BuildingEditorManager with fake Game and monkeypatched loaders for view tests."""
    try:
        from roguelike_engine.utils import loader as loader_mod
    except Exception:  # pragma: no cover
        pytest.skip("loader module not importable")

    # Avoid disk IO: return scaled dummy surfaces
    monkeypatch.setattr(
        loader_mod,
        "load_image",
        lambda path, scale=None: pygame.transform.scale(
            surface_factory(32, 32, (120, 120, 120, 255)), scale or (32, 32)
        ),
        raising=True,
    )

    from types import SimpleNamespace
    dummy_building = SimpleNamespace(
        x=0,
        y=0,
        image=surface_factory(64, 64, (80, 80, 80, 255)),
        rect=pygame.Rect(0, 0, 64, 64),
        split_ratio=0.5,
        image_path="/virtual/dummy.png",
        collision_map=[["."]],
        z_bottom=0,
        z_top=0,
        collider_scope="CG",
    )
    fake_game = SimpleNamespace(
        state=SimpleNamespace(running=True, editor=None, z_state=SimpleNamespace()),
        buildings=SimpleNamespace(buildings=[dummy_building]),
        camera=camera,
    )

    from roguelike_game.managers.editors.buildings_editor_manager import BuildingEditorManager

    mgr = BuildingEditorManager(fake_game)
    mgr.editor_state.active = True
    return mgr


def test_viw_001_title_bar_exposes_last_rect(monkeypatch, surface_factory, camera):
    """[VIW-001] Title bar: BuildingEditorView exposes _last_title_rect after render when editor is active."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    rect = getattr(mgr.view, "_last_title_rect", None)
    assert rect is not None and rect.width > 0 and rect.height > 0


def test_viw_002_picker_anchor_fallback_and_manual(monkeypatch, surface_factory, camera):
    """[VIW-002] Picker anchoring: fallback under title when no add/remove rect; manual pos overrides anchors."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Ensure no manual pos and no add/remove rect → fallback under title
    editor = mgr.editor_state
    editor.picker_manual_pos = None
    # No toolbar activation → add_remove_panel_rect should remain None
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    title_rect = getattr(mgr.view, "_last_title_rect", None)
    assert title_rect is not None
    assert mgr.view.picker_view._top_anchor_y == title_rect.bottom + 8
    assert mgr.view.picker_view._left_anchor_x == title_rect.left

    # Manual position must override
    editor.picker_manual_pos = (200, 150)
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert mgr.view.picker_view._left_anchor_x == 200
    assert mgr.view.picker_view._top_anchor_y == 150


def test_viw_003_picker_visible_only_when_active(monkeypatch, surface_factory, camera):
    """[VIW-003] Picker is rendered only when editor.picker_active is True."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    calls = {"count": 0}

    def spy_picker_render(surface, cam):
        calls["count"] += 1

    # Replace picker render with spy
    mgr.view.picker_view.render = spy_picker_render

    # Inactive -> not called
    mgr.editor_state.picker_active = False
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert calls["count"] == 0

    # Active -> called once
    mgr.editor_state.picker_active = True
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert calls["count"] == 1


def test_viw_004_ui_blocker_suppresses_overlays(monkeypatch, surface_factory, camera):
    """[VIW-004] When UI is blocked, overlays/handles are not rendered (early return)."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Spy on tool renderers to ensure they are not called when blocked
    flags = {"reset": 0, "split": 0, "zbot": 0, "ztop": 0, "sc": 0}

    mgr.view.default_view.render_reset_handle = lambda *a, **k: flags.__setitem__("reset", flags["reset"] + 1)
    mgr.view.split_view.render = lambda *a, **k: flags.__setitem__("split", flags["split"] + 1)
    mgr.view.z_bottom_view.render = lambda *a, **k: flags.__setitem__("zbot", flags["zbot"] + 1)
    mgr.view.z_top_view.render = lambda *a, **k: flags.__setitem__("ztop", flags["ztop"] + 1)
    mgr.view.collider_scope_view.render = lambda *a, **k: flags.__setitem__("sc", flags["sc"] + 1)

    # Active building to trigger overlays path
    mgr.editor_state.active_building = mgr.game.buildings.buildings[0]

    # Monkeypatch UI blocker to always block
    import roguelike_editors.buildings.building_editor_view as view_mod
    monkeypatch.setattr(view_mod, "is_blocked", lambda mx, my: True, raising=True)

    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert flags == {"reset": 0, "split": 0, "zbot": 0, "ztop": 0, "sc": 0}


def test_viw_005_overlays_only_on_active_building(monkeypatch, surface_factory, camera):
    """[VIW-005] Overlays/handles are drawn only for editor.active_building."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)

    # Add a second building to iterate over
    from types import SimpleNamespace
    second = SimpleNamespace(
        x=10,
        y=10,
        image=surface_factory(16, 16, (90, 90, 90, 255)),
        rect=pygame.Rect(10, 10, 16, 16),
        split_ratio=0.5,
        image_path="/virtual/second.png",
        collision_map=[["."]],
        z_bottom=0,
        z_top=0,
        collider_scope="CG",
    )
    mgr.game.buildings.buildings.append(second)

    calls = {"on_active": 0}
    def spy_reset(screen_, b, cam):
        # Count only when called for the active building
        if b is mgr.editor_state.active_building:
            calls["on_active"] += 1

    mgr.view.default_view.render_reset_handle = spy_reset

    # Active is first building -> expect call once
    mgr.editor_state.active_building = mgr.game.buildings.buildings[0]
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    first_count = calls["on_active"]
    assert first_count >= 1

    # Switch active to second -> expect additional calls only for second
    mgr.editor_state.active_building = second
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert calls["on_active"] >= first_count + 1


def test_viw_006_colliders_mode_hides_handles_but_shows_collider_scope(monkeypatch, surface_factory, camera):
    """[VIW-006] When colliders_mode=True, tool handles are hidden but collider scope toggle is rendered."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)
    mgr = _make_manager(monkeypatch, surface_factory, camera)
    mgr.editor_state.active_building = mgr.game.buildings.buildings[0]

    flags = {"reset": 0, "split": 0, "zbot": 0, "ztop": 0, "sc": 0}
    mgr.view.default_view.render_reset_handle = lambda *a, **k: flags.__setitem__("reset", flags["reset"] + 1)
    mgr.view.split_view.render = lambda *a, **k: flags.__setitem__("split", flags["split"] + 1)
    mgr.view.z_bottom_view.render = lambda *a, **k: flags.__setitem__("zbot", flags["zbot"] + 1)
    mgr.view.z_top_view.render = lambda *a, **k: flags.__setitem__("ztop", flags["ztop"] + 1)
    mgr.view.collider_scope_view.render = lambda *a, **k: flags.__setitem__("sc", flags["sc"] + 1)

    mgr.editor_state.colliders_mode = True
    mgr.render(screen, camera, mgr.game.buildings.buildings)
    assert flags["reset"] == 0 and flags["split"] == 0 and flags["zbot"] == 0 and flags["ztop"] == 0
    assert flags["sc"] >= 1


def test_viw_007_camera_geometry_applied(monkeypatch, surface_factory):
    """[VIW-007] Overlay rect uses camera.apply/scale to position/size properly."""
    screen = pygame.Surface((800, 600), pygame.SRCALPHA)

    # Custom camera to spy calls
    class SpyCamera:
        def __init__(self):
            self.apply_calls = 0
            self.scale_calls = 0
            self.zoom = 1.0
        def apply(self, pos):
            self.apply_calls += 1
            return pos
        def scale(self, size):
            self.scale_calls += 1
            return size

    spy_cam = SpyCamera()
    # Monkeypatch loader to avoid IO during manager/render
    from roguelike_engine.utils import loader as loader_mod
    monkeypatch.setattr(
        loader_mod,
        "load_image",
        lambda path, scale=None: pygame.transform.scale(
            surface_factory(32, 32, (120, 120, 120, 255)), scale or (32, 32)
        ),
        raising=True,
    )
    from roguelike_game.managers.editors.buildings_editor_manager import BuildingEditorManager
    from types import SimpleNamespace
    dummy_building = SimpleNamespace(
        x=5,
        y=7,
        image=surface_factory(10, 8, (80, 80, 80, 255)),
        rect=pygame.Rect(5, 7, 10, 8),
        split_ratio=0.5,
        image_path="/virtual/dummy.png",
        collision_map=[["."]],
        z_bottom=0,
        z_top=0,
        collider_scope="CG",
    )
    fake_game = SimpleNamespace(
        state=SimpleNamespace(running=True, editor=None, z_state=SimpleNamespace()),
        buildings=SimpleNamespace(buildings=[dummy_building]),
        camera=spy_cam,
    )
    mgr = BuildingEditorManager(fake_game)
    mgr.editor_state.active = True
    mgr.editor_state.active_building = dummy_building

    mgr.render(screen, spy_cam, fake_game.buildings.buildings)
    assert spy_cam.apply_calls >= 1 and spy_cam.scale_calls >= 1
