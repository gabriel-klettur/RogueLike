from __future__ import annotations
from typing import Optional, Callable

from .service import AudioBus

_BUS: Optional[AudioBus] = None
_APPLY_HOOK: Optional[Callable[[], None]] = None

def set_bus(bus: AudioBus) -> None:
    global _BUS
    _BUS = bus

def get_bus() -> Optional[AudioBus]:
    return _BUS

def set_apply_hook(fn: Callable[[], None]) -> None:
    """Registra un hook para 'Aplicar ahora' (inyectado por el juego)."""
    global _APPLY_HOOK
    _APPLY_HOOK = fn

def apply_audio_config_now() -> None:
    """Invoca el hook de aplicación inmediata, si existe."""
    if _APPLY_HOOK is not None:
        try:
            _APPLY_HOOK()
        except Exception:
            pass

# Convenience wrappers ---------------------------------------------------------

def play_music(track_id: Optional[str] = None, *, path: Optional[str] = None,
               loop: bool = True, volume: Optional[float] = None, fade_in_ms: int = 0) -> None:
    if _BUS is not None:
        _BUS.play_music(track_id=track_id, path=path, loop=loop, volume=volume, fade_in_ms=fade_in_ms)

def stop_music(fade_ms: int = 300) -> None:
    if _BUS is not None:
        _BUS.stop_music(fade_ms=fade_ms)

def crossfade(to_track_id: Optional[str] = None, *, to_path: Optional[str] = None,
              duration_ms: int = 600, target_volume: Optional[float] = None) -> None:
    if _BUS is not None:
        _BUS.crossfade(to_track_id=to_track_id, to_path=to_path, duration_ms=duration_ms, target_volume=target_volume)

def set_music_volume(v: float) -> None:
    if _BUS is not None:
        _BUS.set_music_volume(v)

def play_sfx(sfx_id: Optional[str] = None, *, path: Optional[str] = None,
             volume: Optional[float] = None, pan: Optional[float] = None, group: str = 'sfx') -> None:
    if _BUS is not None:
        _BUS.play_sfx(sfx_id=sfx_id, path=path, volume=volume, pan=pan, group=group)

def set_sfx_volume(v: float) -> None:
    if _BUS is not None:
        _BUS.set_sfx_volume(v)

def set_ambient_volume(v: float) -> None:
    if _BUS is not None:
        _BUS.set_ambient_volume(v)
