import pytest
import pygame
from unittest.mock import MagicMock
from roguelike_game.main import (
    init_pygame,
    create_screen,
    configure_window,
    init_performance_tools,
    create_game,
    run_game_loop,
)
from roguelike_engine.config.config import SCREEN_WIDTH, SCREEN_HEIGHT


def test_init_pygame(monkeypatch):
    """
    Verifies that init_pygame() initializes Pygame and shows the mouse cursor.
    """
    called = {"init": False, "visible": None}

    # Simulate pygame.init() and pygame.mouse.set_visible()
    def fake_init():
        called["init"] = True
    def fake_set_visible(vis):
        called["visible"] = vis

    monkeypatch.setattr(pygame, "init", fake_init)
    monkeypatch.setattr(pygame.mouse, "set_visible", fake_set_visible)

    # Execute and assert behavior
    init_pygame()
    assert called["init"] is True, "Pygame init() should be called"
    assert called["visible"] is True, "Mouse visibility should be enabled"


def test_create_screen(monkeypatch):
    """
    Ensures create_screen() returns the surface and uses correct size and flags.
    """
    dummy_surface = object()
    args = {}

    # Stub pygame.display.set_mode to capture arguments
    def fake_set_mode(size, flags):
        args["size"] = size
        args["flags"] = flags
        return dummy_surface
    monkeypatch.setattr(pygame.display, "set_mode", fake_set_mode)

    surface = create_screen()

    # Validate return value and parameters
    assert surface is dummy_surface, "Should return the surface from set_mode()"
    assert args["size"] == (SCREEN_WIDTH, SCREEN_HEIGHT), "Screen dimensions must match config"
    expected_flags = pygame.HWSURFACE | pygame.DOUBLEBUF | pygame.RESIZABLE
    assert args["flags"] == expected_flags, "Display flags should enable hardware surface, double buffering, and resizing"


def test_configure_window(monkeypatch):
    """
    Checks that configure_window() loads the icon and sets window icon and caption.
    """
    dummy_icon = object()
    calls = {}

    # Stub load_image to return dummy icon
    monkeypatch.setattr(
        "roguelike_game.main.load_image", lambda path: dummy_icon)
    # Capture display calls
    monkeypatch.setattr(
        pygame.display, "set_icon", lambda icon: calls.setdefault("icon", icon))
    monkeypatch.setattr(
        pygame.display, "set_caption", lambda title: calls.setdefault("title", title))

    configure_window("path/to/icon.png", "MyTitle")

    assert calls["icon"] is dummy_icon, "Icon passed to set_icon() should match loaded icon"
    assert calls["title"] == "MyTitle", "Window title should be set correctly"


def test_init_performance_tools(monkeypatch):
    """
    Validates that init_performance_tools() returns the performance log and benchmark logger.
    """
    fake_log = {}
    fake_logger = object()

    monkeypatch.setattr(
        "roguelike_game.main.init_debug_log", lambda: fake_log)
    monkeypatch.setattr(
        "roguelike_game.main.setup_benchmark_logger", lambda: fake_logger)

    perf_log, bench_logger = init_performance_tools()

    assert perf_log is fake_log, "Performance log should be the object returned by init_debug_log()"
    assert bench_logger is fake_logger, "Benchmark logger should come from setup_benchmark_logger()"


def test_create_game_success(monkeypatch):
    """
    Ensures create_game() returns a valid Game instance when state is present.
    """
    dummy_screen = object()
    dummy_log = {}
    dummy_game = MagicMock()
    dummy_game.state = "ok"

    monkeypatch.setattr(
        "roguelike_game.main.Game", lambda *args, **kwargs: dummy_game)

    game = create_game(dummy_screen, dummy_log, map_name="m", loading_bg="bg.png")
    assert game is dummy_game, "Should return the MagicMock instance when valid state exists"


def test_create_game_failure(monkeypatch):
    """
    Verifies that create_game() raises RuntimeError if the Game instance lacks a state attribute.
    """
    dummy_screen = object()
    dummy_log = {}
    dummy_game = MagicMock()
    if hasattr(dummy_game, "state"):
        del dummy_game.state

    monkeypatch.setattr(
        "roguelike_game.main.Game", lambda *args, **kwargs: dummy_game)

    with pytest.raises(RuntimeError):
        create_game(dummy_screen, dummy_log)


def test_run_game_loop_success(monkeypatch):
    """
    Checks run_game_loop() executes game.run(), shutdown, save_benchmarks, and pygame.quit without errors.
    """
    dummy_game = MagicMock()
    dummy_logger = MagicMock()
    dummy_log = {}

    dummy_game.run = MagicMock()
    dummy_game.shutdown = MagicMock()
    called = []

    # Stub save_benchmarks and pygame.quit to record calls
    monkeypatch.setattr(
        "roguelike_game.main.save_benchmarks", lambda log: called.append(("save", log)))
    monkeypatch.setattr(
        pygame, "quit", lambda: called.append("quit"))

    run_game_loop(dummy_game, dummy_logger, dummy_log)

    dummy_game.run.assert_called_once(), "game.run() debe llamarse una vez"
    dummy_game.shutdown.assert_called_once(), "game.shutdown() debe llamarse en finally"
    assert ("save", dummy_log) in called, "Debe guardar benchmarks con el log provisto"
    assert "quit" in called, "Debe llamar a pygame.quit() al finalizar"
    dummy_logger.exception.assert_not_called(), "No debe registrar excepciones cuando no ocurre ningún error"


def test_run_game_loop_exception(monkeypatch):
    """
    Asegura que run_game_loop() captura y registra excepciones antes de propagarlas.
    """
    class DummyError(Exception):
        pass

    def raise_error():
        raise DummyError("fail")

    dummy_game = MagicMock()
    dummy_game.run = MagicMock(side_effect=raise_error)
    dummy_game.shutdown = MagicMock()
    dummy_logger = MagicMock()
    dummy_log = {}

    monkeypatch.setattr(
        "roguelike_game.main.save_benchmarks", lambda log: None)
    monkeypatch.setattr(
        pygame, "quit", lambda: None)

    with pytest.raises(DummyError):
        run_game_loop(dummy_game, dummy_logger, dummy_log)

    dummy_logger.exception.assert_called_once_with("Uncaught exception in main loop"), \
        "Debe registrar la excepción con el mensaje adecuado"
    dummy_game.shutdown.assert_called_once(), "shutdown() debe llamarse incluso tras la excepción"
