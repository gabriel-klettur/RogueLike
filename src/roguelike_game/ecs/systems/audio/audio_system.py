import time
import random
import logging
import os
from roguelike_engine.utils.benchmark import benchmark

try:
    from roguelike_engine.audio.api import get_bus
    from roguelike_engine.audio.config import load_audio_catalog
except Exception:
    def get_bus():
        return None
    def load_audio_catalog():
        return None

logger = logging.getLogger(__name__)


class AudioSystem:
    """
    Sistema ECS que consume eventos desde world.components['AudioEventQueue'] y
    los traduce en comandos al AudioBus. También gestiona un pequeño scheduler
    para disparar SFX de ambiente de forma aleatoria mientras esté habilitado.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._catalog = None
        self._last_level = None
        self._last_zone = None
        self._last_biome = None
        self._last_music_track = None
        # Playlist state
        self._playlist_enabled = False
        self._playlist = None
        self._playlist_index = 0
        self._playlist_interval_s = 120.0
        self._next_playlist_at = None
        self._playlist_mode = 'loop'  # 'loop' | 'once' | 'shuffle'

    def _enqueue_song_toast(self, comps, track_id: str, *, duration_s: float = 3.5) -> None:
        try:
            title = None
            try:
                title = (self._catalog.tracks.get(track_id) or {}).get('title')
            except Exception:
                title = None
            name = None
            p = self._catalog.track_path(track_id) if self._catalog else None
            if p:
                name = os.path.basename(str(p))
            # Preferir título si existe; si no, archivo con extensión
            disp = str(title) if title else (str(name) if name else str(track_id))
            tq = comps.setdefault('ToastQueue', [])
            tq.append({'text': f"Song: '{disp}'", 'until': time.time() + float(duration_s)})
        except Exception:
            pass

    def _track_duration_s(self, track_id: str) -> float:
        # Intentar leer duración desde config; evitar E/S en hilo principal
        try:
            rec = (self._catalog.tracks or {}).get(track_id) if self._catalog else None
        except Exception:
            rec = None
        # 1) Campo explícito en config
        try:
            if rec and rec.get('duration_s') is not None:
                d = float(rec.get('duration_s'))
                if d > 0:
                    return d
        except Exception:
            pass
        # 2) Fallback: usar intervalo fijo configurable (no hacer I/O aquí)
        try:
            return float(self._playlist_interval_s)
        except Exception:
            return 120.0
    
    def update(self, world, camera=None):
        bus = get_bus()
        if bus is None:
            return
        if self._catalog is None:
            try:
                self._catalog = load_audio_catalog()
            except Exception:
                self._catalog = None
        comps = world.components
        # 0) Autoresolución por ámbito (nivel/bioma/zona) si hay catálogo
        if self._catalog is not None:
            try:
                level = getattr(getattr(world, 'world', world), 'current_level', None) or getattr(world, 'current_level', None) or getattr(getattr(world, 'map', None), 'name', None)
            except Exception:
                level = None
            # Intentar obtener zona desde map.current_zone; si no existe, calcular a partir de la posición del jugador
            try:
                zone = getattr(getattr(world, 'map', None), 'current_zone', None)
            except Exception:
                zone = None
            if not zone:
                try:
                    from roguelike_engine.config.config_tiles import TILE_SIZE as _TILE
                    from roguelike_engine.config.map_config import global_map_settings
                    # Posición del jugador -> tile
                    pid = getattr(world, 'player_entity', None)
                    pos = world.components.get('Position', {}).get(pid)
                    if pos is not None:
                        tx, ty = int(pos.x) // _TILE, int(pos.y) // _TILE
                        for zname, (ox, oy) in (getattr(global_map_settings, 'zone_offsets', {}) or {}).items():
                            zw, zh = global_map_settings.zone_width, global_map_settings.zone_height
                            if ox <= tx < ox + zw and oy <= ty < oy + zh:
                                zone = zname
                                break
                except Exception:
                    pass
            try:
                biome = getattr(getattr(world, 'map', None), 'biome', None) or getattr(getattr(world, 'map', None), 'biome_key', None)
            except Exception:
                biome = None
            changed = (level != self._last_level) or (zone != self._last_zone) or (biome != self._last_biome)
            if changed:
                self._last_level, self._last_zone, self._last_biome = level, zone, biome
                # Resolver música deseada
                track_id = self._catalog.resolve_music_for(level=level, zone=zone, biome=biome)
                if track_id and track_id != self._last_music_track:
                    bus.crossfade(to_track_id=track_id, duration_ms=int((self._catalog.get_default_music() or {}).get('crossfade_ms', 600)))
                    self._last_music_track = track_id
                    # Toast canción
                    self._enqueue_song_toast(comps, track_id)
                # Resolver ambient deseado y programar scheduler
                amb = self._catalog.resolve_ambient_for(level=level, zone=zone, biome=biome)
                aq = comps.setdefault('AudioEventQueue', [])
                aq.append({
                    'type': 'enable_ambient',
                    'choices': amb.get('choices') or [],
                    'min_interval': amb.get('min_interval', 8.0),
                    'max_interval': amb.get('max_interval', 20.0),
                    'group': amb.get('group', 'ambient'),
                    'volume': amb.get('volume'),
                })
                # (Re)configurar playlist según ámbito
                try:
                    defaults_music = (self._catalog.get_default_music() or {})
                    self._playlist_interval_s = float(defaults_music.get('playlist_interval_s', 120))
                    playlist = list(defaults_music.get('ingame_playlist') or [])
                    default_id = defaults_music.get('ingame_track_id')
                    self._playlist_mode = str(defaults_music.get('playlist_mode') or 'loop').lower()
                    # Habilitar playlist solo si el ámbito NO fija un tema específico distinto del default
                    # i.e., si resolve_music_for devolvió el default y tenemos una playlist válida
                    self._playlist_enabled = bool(track_id and default_id and track_id == default_id and len(playlist) >= 2)
                    if self._playlist_enabled:
                        self._playlist = playlist
                        try:
                            self._playlist_index = max(0, playlist.index(track_id))
                        except Exception:
                            self._playlist_index = 0
                        # Programar cambio al final de la canción actual (o fallback al intervalo)
                        dur = self._track_duration_s(track_id)
                        self._next_playlist_at = time.time() + max(5.0, float(dur))
                    else:
                        self._playlist = None
                        self._next_playlist_at = None
                except Exception:
                    # No romper si la config no trae playlist
                    self._playlist_enabled = False
                    self._playlist = None
                    self._next_playlist_at = None
        # 1) Procesar cola de eventos
        queue = comps.setdefault('AudioEventQueue', [])
        while queue:
            ev = queue.pop(0)
            try:
                et = ev.get('type')
            except Exception:
                continue
            try:
                if et == 'play_sfx':
                    sfx_id = ev.get('sfx_id')
                    choices = ev.get('choices')
                    volume = ev.get('volume')
                    pan = ev.get('pan')
                    group = ev.get('group', 'sfx')
                    if not sfx_id and choices:
                        sfx_id = random.choice(list(choices) if isinstance(choices, (list, tuple, set)) else choices)
                    if sfx_id:
                        bus.play_sfx(sfx_id=sfx_id, volume=volume, pan=pan, group=group)
                elif et == 'play_music':
                    bus.play_music(track_id=ev.get('track_id'), path=ev.get('path'), loop=bool(ev.get('loop', True)), volume=ev.get('volume'), fade_in_ms=int(ev.get('fade_in_ms') or 0))
                elif et == 'stop_music':
                    bus.stop_music(fade_ms=int(ev.get('fade_ms') or 300))
                elif et == 'crossfade_to':
                    bus.crossfade(to_track_id=ev.get('to_track_id'), to_path=ev.get('to_path'), duration_ms=int(ev.get('duration_ms') or 600), target_volume=ev.get('target_volume'))
                elif et == 'set_music_vol':
                    bus.set_music_volume(float(ev.get('value', 1.0)))
                elif et == 'set_sfx_vol':
                    bus.set_sfx_volume(float(ev.get('value', 1.0)))
                elif et == 'set_ambient_vol':
                    bus.set_ambient_volume(float(ev.get('value', 1.0)))
                elif et == 'enable_ambient':
                    state = comps.setdefault('AudioAmbientState', {})
                    state['enabled'] = True
                    state['choices'] = list(ev.get('choices') or [])
                    state['min_interval'] = float(ev.get('min_interval', 8.0))
                    state['max_interval'] = float(ev.get('max_interval', 20.0))
                    now = time.time()
                    state['next_at'] = now + random.uniform(state['min_interval'], state['max_interval'])
                    state['volume'] = ev.get('volume')
                    state['group'] = ev.get('group', 'ambient')
                    logger.info("[AudioAmbient] habilitado con %d choices", len(state['choices']))
                elif et == 'disable_ambient':
                    state = comps.setdefault('AudioAmbientState', {})
                    state['enabled'] = False
                elif et == 'reload_audio_catalog':
                    # Recargar catálogo y re-aplicar música/ambient según el ámbito actual
                    try:
                        self._catalog = load_audio_catalog()
                    except Exception:
                        self._catalog = None
                    if self._catalog is not None:
                        try:
                            level = getattr(getattr(world, 'world', world), 'current_level', None) or getattr(world, 'current_level', None) or getattr(getattr(world, 'map', None), 'name', None)
                        except Exception:
                            level = None
                        try:
                            zone = getattr(getattr(world, 'map', None), 'current_zone', None)
                        except Exception:
                            zone = None
                        try:
                            biome = getattr(getattr(world, 'map', None), 'biome', None) or getattr(getattr(world, 'map', None), 'biome_key', None)
                        except Exception:
                            biome = None
                        # Música
                        track_id = self._catalog.resolve_music_for(level=level, zone=zone, biome=biome) or (self._catalog.get_default_music() or {}).get('ingame_track_id')
                        if track_id and track_id != self._last_music_track:
                            bus.crossfade(to_track_id=track_id, duration_ms=int((self._catalog.get_default_music() or {}).get('crossfade_ms', 600)))
                            self._last_music_track = track_id
                            # Toast canción
                            self._enqueue_song_toast(comps, track_id)
                        # Ambient
                        amb = self._catalog.resolve_ambient_for(level=level, zone=zone, biome=biome)
                        aq2 = comps.setdefault('AudioEventQueue', [])
                        aq2.append({
                            'type': 'enable_ambient',
                            'choices': amb.get('choices') or [],
                            'min_interval': amb.get('min_interval', 8.0),
                            'max_interval': amb.get('max_interval', 20.0),
                            'group': amb.get('group', 'ambient'),
                            'volume': amb.get('volume'),
                        })
                elif et == 'enter_game_default':
                    # Entrar al juego: usar catálogo (defaults o ámbito actual si disponible)
                    amb = None
                    track_id = None
                    if self._catalog is not None:
                        try:
                            level = getattr(getattr(world, 'world', world), 'current_level', None) or getattr(world, 'current_level', None) or getattr(getattr(world, 'map', None), 'name', None)
                        except Exception:
                            level = None
                        try:
                            zone = getattr(getattr(world, 'map', None), 'current_zone', None)
                        except Exception:
                            zone = None
                        try:
                            biome = getattr(getattr(world, 'map', None), 'biome', None) or getattr(getattr(world, 'map', None), 'biome_key', None)
                        except Exception:
                            biome = None
                        track_id = self._catalog.resolve_music_for(level=level, zone=zone, biome=biome) or (self._catalog.get_default_music() or {}).get('ingame_track_id')
                        amb = self._catalog.resolve_ambient_for(level=level, zone=zone, biome=biome)
                    dur = int(ev.get('duration_ms') or ((self._catalog.get_default_music() or {}).get('crossfade_ms', 600) if self._catalog else 600))
                    if track_id:
                        bus.crossfade(to_track_id=track_id, duration_ms=dur)
                        self._last_music_track = track_id
                        # Toast canción
                        self._enqueue_song_toast(comps, track_id)
                    if amb is not None:
                        aq = comps.setdefault('AudioEventQueue', [])
                        aq.append({
                            'type': 'enable_ambient',
                            'choices': amb.get('choices') or [],
                            'min_interval': amb.get('min_interval', 8.0),
                            'max_interval': amb.get('max_interval', 20.0),
                            'group': amb.get('group', 'ambient'),
                            'volume': amb.get('volume'),
                        })
            except Exception:
                continue

        # 2) Ambient scheduler
        st = comps.setdefault('AudioAmbientState', {})
        if st.get('enabled') and st.get('choices'):
            now = time.time()
            next_at = st.get('next_at') or 0.0
            if now >= next_at:
                try:
                    choice = random.choice(list(st['choices']))
                    bus.play_sfx(sfx_id=choice, volume=st.get('volume'), group=st.get('group', 'ambient'))
                except Exception:
                    pass
                # programar siguiente
                try:
                    mi = float(st.get('min_interval', 8.0))
                    ma = float(st.get('max_interval', 20.0))
                except Exception:
                    mi, ma = 8.0, 20.0
                st['next_at'] = now + random.uniform(mi, ma)

        # 3) Playlist rotation (si habilitada)
        try:
            if self._playlist_enabled and self._playlist and len(self._playlist) >= 2:
                now = time.time()
                if self._next_playlist_at and now >= self._next_playlist_at:
                    try:
                        current = self._last_music_track
                        # Asegurar índice consistente con track actual
                        if current in self._playlist:
                            self._playlist_index = self._playlist.index(current)
                        if self._playlist_mode == 'shuffle':
                            # Elegir al azar evitando repetir el actual si hay más de 1 opción
                            choices = [t for t in self._playlist if t != current] or list(self._playlist)
                            next_id = random.choice(choices)
                            nxt_idx = self._playlist.index(next_id)
                        else:
                            nxt_idx = (self._playlist_index + 1) % len(self._playlist)
                            next_id = self._playlist[nxt_idx]
                        if next_id != current:
                            bus.crossfade(to_track_id=next_id, duration_ms=int((self._catalog.get_default_music() or {}).get('crossfade_ms', 600)))
                            self._last_music_track = next_id
                            self._playlist_index = nxt_idx
                            # Toast canción
                            self._enqueue_song_toast(comps, next_id)
                        # Reprogramar siguiente cambio al final de la canción siguiente
                        dur_next = self._track_duration_s(next_id)
                        self._next_playlist_at = now + max(5.0, float(dur_next))
                        # Si el modo es 'once' y hemos vuelto al inicio, deshabilitar playlist
                        if self._playlist_mode == 'once' and nxt_idx == 0:
                            self._playlist_enabled = False
                            self._next_playlist_at = None
                    except Exception:
                        # Si algo falla, deshabilitar playlist para no spamear
                        self._playlist_enabled = False
                        self._next_playlist_at = None
        except Exception:
            pass
