from __future__ import annotations
import threading
import time
import queue
from typing import Optional, List, Callable, Tuple
import os

from .events import (
    PlayMusic, StopMusic, Crossfade, SetMusicVolume,
    PlaySfx, StopSfx, SetSfxVolume, SetAmbientVolume,
    DuckMusic, PlaylistSet,
)
from .config import AudioCatalog, load_audio_catalog
from .backend_pygame import PygameAudioBackend


class AudioService:
    """
    Servicio de audio en hilo dedicado. Consume comandos y aplica acciones en el backend.
    Mantiene un pequeño scheduler para crossfades y tareas diferidas.
    """
    def __init__(self, catalog: Optional[AudioCatalog] = None) -> None:
        self.catalog: AudioCatalog = catalog or load_audio_catalog()
        self.backend = PygameAudioBackend()
        self._q: "queue.Queue[object]" = queue.Queue()
        self._running: bool = False
        self._thread: Optional[threading.Thread] = None
        self._delayed: List[Tuple[float, Callable[[], None]]] = []
        self._music_current_volume: float = 0.6

    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self.backend.init()
        self._thread = threading.Thread(target=self._run_loop, name="AudioService", daemon=True)
        self._thread.start()

    def stop(self, fade_music_ms: int = 300, timeout_s: float = 1.0) -> None:
        try:
            # Cortesía: parar música
            self.backend.stop_music(fade_ms=fade_music_ms)
        except Exception:
            pass
        self._running = False
        try:
            if self._thread and self._thread.is_alive():
                self._thread.join(timeout=timeout_s)
        except Exception:
            pass

    # --- API de encolado ---
    def post(self, event: object) -> None:
        try:
            self._q.put_nowait(event)
        except Exception:
            pass

    # --- Loop interno ---
    def _run_loop(self) -> None:
        tick = 0.01  # 10 ms
        while self._running:
            tnow = time.time()
            # 1) Ejecutar tareas diferidas vencidas
            if self._delayed:
                keep: List[Tuple[float, Callable[[], None]]] = []
                for when, fn in self._delayed:
                    if tnow >= when:
                        try:
                            fn()
                        except Exception:
                            pass
                    else:
                        keep.append((when, fn))
                self._delayed = keep
            # 2) Drenar unos cuantos comandos por tick
            for _ in range(32):
                try:
                    ev = self._q.get_nowait()
                except queue.Empty:
                    break
                try:
                    self._handle_event(ev)
                except Exception:
                    pass
            time.sleep(tick)

    # --- Handlers ---
    def _prefetch_file(self, path: Optional[str], bytes_hint: int = 65536) -> None:
        """Lee una pequeña porción del archivo para calentar la caché del SO.
        Ejecutado en el hilo de audio para evitar bloquear el hilo principal.
        """
        try:
            if not path:
                return
            # Evitar hacer trabajo si el archivo es muy pequeño: aún así se beneficia, pero limitamos el hint
            hint = max(4096, int(bytes_hint))
            with open(path, 'rb') as f:
                _ = f.read(hint)
        except Exception:
            # Silencioso: es una optimización best-effort
            pass
    def _handle_event(self, ev: object) -> None:
        if isinstance(ev, PlayMusic):
            path = ev.path
            if not path and ev.track_id:
                path = self.catalog.track_path(ev.track_id)
            if path:
                # Prefetch inmediato (rápido) para reducir latencias de I/O
                try:
                    cfg = self.catalog.get_default_music() if self.catalog else {}
                except Exception:
                    cfg = {}
                prefetch_bytes = int((cfg or {}).get('prefetch_bytes', 65536))
                self._prefetch_file(path, bytes_hint=prefetch_bytes)
                # Preparar buffer en mixer y luego iniciar sin coste de carga
                try:
                    self.backend.prepare_music(path)
                    self.backend.play_prepared_music(loop=ev.loop, volume=ev.volume, fade_in_ms=ev.fade_in_ms)
                except Exception:
                    # Fallback: reproducción directa
                    self.backend.play_music(path, loop=ev.loop, volume=ev.volume, fade_in_ms=ev.fade_in_ms)
                if ev.volume is not None:
                    try:
                        self._music_current_volume = float(ev.volume)
                    except Exception:
                        pass
        elif isinstance(ev, StopMusic):
            self.backend.stop_music(fade_ms=ev.fade_ms)
        elif isinstance(ev, Crossfade):
            path = ev.to_path
            if not path and ev.to_track_id:
                path = self.catalog.track_path(ev.to_track_id)
            if path:
                # Estrategia simple: fadeout de actual y, tras duración, fadein de la nueva
                dur = max(0, int(ev.duration_ms))
                self.backend.stop_music(fade_ms=dur)
                when = time.time() + (dur / 1000.0)
                # Programar un prefetch un poco antes de iniciar la nueva pista
                try:
                    cfg = self.catalog.get_default_music() if self.catalog else {}
                except Exception:
                    cfg = {}
                lead_ms = int((cfg or {}).get('prefetch_lead_ms', 250))
                prefetch_at = max(time.time(), when - (lead_ms / 1000.0))
                prefetch_bytes = int((cfg or {}).get('prefetch_bytes', 65536))
                def _prefetch(path=path):
                    self._prefetch_file(path, bytes_hint=prefetch_bytes)
                    try:
                        self.backend.prepare_music(path)
                    except Exception:
                        pass
                self._delayed.append((prefetch_at, _prefetch))
                def _start_new(path=path, vol=ev.target_volume):
                    try:
                        self.backend.play_prepared_music(loop=True, volume=vol, fade_in_ms=dur)
                    except Exception:
                        self.backend.play_music(path, loop=True, volume=vol, fade_in_ms=dur)
                    if vol is not None:
                        try:
                            self._music_current_volume = float(vol)
                        except Exception:
                            pass
                self._delayed.append((when, _start_new))
        elif isinstance(ev, SetMusicVolume):
            self.backend.set_music_volume(ev.value)
            try:
                self._music_current_volume = float(ev.value)
            except Exception:
                pass
        elif isinstance(ev, PlaySfx):
            path = ev.path
            if not path and ev.sfx_id:
                path = self.catalog.sfx_path(ev.sfx_id)
            if path:
                snd = self.backend.load_sfx(path)
                if snd is not None:
                    self.backend.play_sfx(snd, volume=ev.volume, pan=ev.pan, group=ev.group)
                    # Ducking automático si el sfx coincide con prefijos configurados
                    try:
                        sfx_id = ev.sfx_id or ""
                        dk = self.catalog.get_default_ducking() or {}
                        prefixes = dk.get('auto_on_sfx_prefixes') or []
                        if sfx_id and any(str(sfx_id).startswith(str(p)) for p in prefixes):
                            amt = float(dk.get('amount_db', -4.0))
                            hold = int(dk.get('hold_ms', 250))
                            rel = int(dk.get('release_ms', 200))
                            self._handle_event(DuckMusic(amount_db=amt, hold_ms=hold, release_ms=rel))
                    except Exception:
                        pass
        elif isinstance(ev, StopSfx):
            # Placeholder: pygame no facilita parar por id; se podría llevar registro de canales por grupo
            # Para este MVP, no implementamos stop granulado.
            pass
        elif isinstance(ev, SetSfxVolume):
            self.backend.set_sfx_volume(ev.value)
        elif isinstance(ev, SetAmbientVolume):
            self.backend.set_ambient_volume(ev.value)
        elif isinstance(ev, DuckMusic):
            # Bajar volumen temporalmente y restaurarlo luego
            try:
                prev = float(self._music_current_volume)
            except Exception:
                prev = 0.6
            # amount_db negativo reduce volumen. Convertir dB a factor lineal
            try:
                amount_db = float(ev.amount_db)
            except Exception:
                amount_db = -6.0
            factor = pow(10.0, amount_db / 20.0)
            new_v = max(0.0, min(1.0, prev * factor))
            self.backend.set_music_volume(new_v)
            # Restaurar después de hold+release (sin rampa por simplicidad)
            delay = max(0.0, (getattr(ev, 'hold_ms', 200) + getattr(ev, 'release_ms', 200)) / 1000.0)
            when = time.time() + delay
            def _restore(vol=prev):
                self.backend.set_music_volume(vol)
                try:
                    self._music_current_volume = float(vol)
                except Exception:
                    pass
            self._delayed.append((when, _restore))
        elif isinstance(ev, PlaylistSet):
            # MVP: no-op. La playlist puede gestionarse a nivel de juego por ahora.
            pass


class AudioBus:
    """Fachada segura para postear comandos al servicio."""
    def __init__(self, service: AudioService) -> None:
        self._svc = service

    # Música
    def play_music(self, track_id: Optional[str] = None, *, path: Optional[str] = None,
                   loop: bool = True, volume: Optional[float] = None, fade_in_ms: int = 0) -> None:
        self._svc.post(PlayMusic(track_id=track_id, path=path, loop=loop, volume=volume, fade_in_ms=fade_in_ms))

    def stop_music(self, fade_ms: int = 300) -> None:
        self._svc.post(StopMusic(fade_ms=fade_ms))

    def crossfade(self, to_track_id: Optional[str] = None, *, to_path: Optional[str] = None,
                  duration_ms: int = 600, target_volume: Optional[float] = None) -> None:
        self._svc.post(Crossfade(to_track_id=to_track_id, to_path=to_path, duration_ms=duration_ms, target_volume=target_volume))

    def set_music_volume(self, v: float) -> None:
        self._svc.post(SetMusicVolume(value=v))

    # SFX
    def play_sfx(self, sfx_id: Optional[str] = None, *, path: Optional[str] = None,
                 volume: Optional[float] = None, pan: Optional[float] = None, group: str = 'sfx') -> None:
        self._svc.post(PlaySfx(sfx_id=sfx_id, path=path, volume=volume, pan=pan, group=group))

    def set_sfx_volume(self, v: float) -> None:
        self._svc.post(SetSfxVolume(value=v))

    def set_ambient_volume(self, v: float) -> None:
        self._svc.post(SetAmbientVolume(value=v))

    def duck_music(self, amount_db: float = -6.0, hold_ms: int = 300, release_ms: int = 200) -> None:
        self._svc.post(DuckMusic(amount_db=amount_db, hold_ms=hold_ms, release_ms=release_ms))
