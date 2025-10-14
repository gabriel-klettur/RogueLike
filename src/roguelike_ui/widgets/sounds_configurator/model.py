from __future__ import annotations

from typing import Any, Callable, Dict, List, Optional

from .persistence import AudioJsonRepository, ZonesRepository


class SoundSettingsModel:
    """Domain model holding the state and persistence for sound settings.

    It bridges the external `audio_config` (live volumes) and the JSON catalogs
    (tracks, defaults, and per-zone overrides).
    """

    def __init__(
        self,
        audio_config: Any,
        repo: Optional[AudioJsonRepository] = None,
        zones_repo: Optional[ZonesRepository] = None,
        on_change: Optional[Callable[[str, float], None]] = None,
    ) -> None:
        self.audio_config = audio_config
        self.on_change = on_change
        self.repo = repo or AudioJsonRepository()
        self.zones_repo = zones_repo or ZonesRepository()

        # Live volumes 0..1
        self.values: Dict[str, float] = {
            "music": float(self.audio_config.get("music")),
            "ambient": float(self.audio_config.get("ambient")),
            "sfx": float(self.audio_config.get("sfx")),
        }
        self.muted: Dict[str, bool] = {k: (v <= 0.0) for k, v in self.values.items()}
        self.last_non_zero: Dict[str, float] = {k: (v if v > 0 else 0.6) for k, v in self.values.items()}

        # JSON payload and defaults
        self._audio_json: Dict[str, Any] = self.repo.load()
        self.tracks: List[str] = list((self._audio_json.get("tracks") or {}).keys())

        d_music = (self._audio_json.get("defaults") or {}).get("music") or {}
        self.intro_track: Optional[str] = d_music.get("startup_track_id") or (self.tracks[0] if self.tracks else None)
        self.ingame_track: Optional[str] = d_music.get("ingame_track_id") or (self.tracks[0] if self.tracks else None)

        d_amb = (self._audio_json.get("defaults") or {}).get("ambient") or {}
        self.ambient_min: float = float(d_amb.get("min_interval", 6.0))
        self.ambient_max: float = float(d_amb.get("max_interval", 18.0))

        d_duck = (self._audio_json.get("defaults") or {}).get("ducking") or {}
        self.duck_db: float = float(d_duck.get("amount_db", -4.0))
        self.duck_hold: int = int(d_duck.get("hold_ms", 250))
        self.duck_release: int = int(d_duck.get("release_ms", 200))

        # Zones from audio.json overrides + zones catalog list
        self.zone_track: Dict[str, str] = {}
        self.zone_ambient_min: Dict[str, float] = {}
        self.zone_ambient_max: Dict[str, float] = {}
        for zname, zcfg in (self._audio_json.get("zones") or {}).items():
            if not isinstance(zcfg, dict):
                continue
            mt = zcfg.get("music_track_id")
            if isinstance(mt, str):
                self.zone_track[zname] = mt
            amb = zcfg.get("ambient") or {}
            if isinstance(amb, dict):
                if "min_interval" in amb:
                    self.zone_ambient_min[zname] = float(amb.get("min_interval"))
                if "max_interval" in amb:
                    self.zone_ambient_max[zname] = float(amb.get("max_interval"))

        self.zones: List[str] = self.zones_repo.load_zones()
        self.zone_index: int = 0

    # --- Volumes ---
    def set_volume(self, key: str, value: float) -> None:
        v = max(0.0, min(1.0, float(value)))
        self.values[key] = v
        self.audio_config.set(key, v)
        if callable(self.on_change):
            self.on_change(key, v)
        if v <= 0.0:
            self.muted[key] = True
        else:
            self.muted[key] = False
            self.last_non_zero[key] = v

    def nudge_volume(self, key: str, delta: float) -> None:
        self.set_volume(key, self.values[key] + delta)

    def toggle_mute(self, index: int) -> None:
        key = ("music", "ambient", "sfx")[max(0, min(2, index))]
        if not self.muted.get(key, False):
            if self.values[key] > 0:
                self.last_non_zero[key] = self.values[key]
            self.set_volume(key, 0.0)
        else:
            restored = self.last_non_zero.get(key, 0.6)
            restored = 0.6 if restored <= 0.0 else restored
            self.set_volume(key, restored)

    def reset_channel(self, index: int) -> None:
        key = ("music", "ambient", "sfx")[max(0, min(2, index))]
        defaults = {"music": 0.6, "ambient": 0.6, "sfx": 0.7}
        nv = float(defaults.get(key, 0.6))
        self.set_volume(key, nv)

    def reset_defaults(self) -> None:
        defaults = {"music": 0.6, "ambient": 0.6, "sfx": 0.7}
        for k, v in defaults.items():
            self.set_volume(k, v)
        self.intro_track = self.tracks[0] if self.tracks else self.intro_track
        self.ingame_track = self.ingame_track or (self.tracks[0] if self.tracks else None)
        self.ambient_min, self.ambient_max = 6.0, 18.0
        self.duck_db, self.duck_hold, self.duck_release = -4.0, 250, 200
        self.save_audio()

    # --- Tracks & advanced ---
    def step_intro_track(self, step: int) -> None:
        if not self.tracks:
            return
        try:
            idx = self.tracks.index(self.intro_track)
        except Exception:
            idx = 0
        self.intro_track = self.tracks[(idx + step) % len(self.tracks)]
        self.save_audio()

    def step_ingame_track(self, step: int) -> None:
        if not self.tracks:
            return
        try:
            idx = self.tracks.index(self.ingame_track)
        except Exception:
            idx = 0
        self.ingame_track = self.tracks[(idx + step) % len(self.tracks)]
        self.save_audio()

    def nudge_ambient_min(self, step: int) -> None:
        self.ambient_min = max(0.0, min(60.0, float(self.ambient_min) + 0.5 * step))
        if self.ambient_min > self.ambient_max:
            self.ambient_max = self.ambient_min
        self.save_audio()

    def nudge_ambient_max(self, step: int) -> None:
        self.ambient_max = max(0.0, min(120.0, float(self.ambient_max) + 0.5 * step))
        if self.ambient_max < self.ambient_min:
            self.ambient_min = self.ambient_max
        self.save_audio()

    def nudge_duck_db(self, step: int) -> None:
        self.duck_db = max(-24.0, min(0.0, float(self.duck_db) + 1.0 * step))
        self.save_audio()

    def nudge_duck_hold(self, step: int) -> None:
        self.duck_hold = int(max(0, min(2000, int(self.duck_hold) + 25 * step)))
        self.save_audio()

    def nudge_duck_release(self, step: int) -> None:
        self.duck_release = int(max(0, min(2000, int(self.duck_release) + 25 * step)))
        self.save_audio()

    # --- Zones ---
    def step_zone(self, step: int) -> None:
        if self.zones:
            self.zone_index = (self.zone_index + step) % len(self.zones)

    def step_zone_track(self, step: int) -> None:
        if not self.zones or not self.tracks:
            return
        zname = self.zones[self.zone_index]
        cur = self.zone_track.get(zname)
        try:
            idx = self.tracks.index(cur)
        except Exception:
            idx = 0
        self.zone_track[zname] = self.tracks[(idx + step) % len(self.tracks)]
        self.save_audio()

    def nudge_zone_ambient(self, which: str, step: int) -> None:
        if not self.zones:
            return
        zname = self.zones[self.zone_index]
        if which == "min":
            v = float(self.zone_ambient_min.get(zname, self.ambient_min)) + 0.5 * step
            v = max(0.0, min(60.0, v))
            self.zone_ambient_min[zname] = v
            if v > self.zone_ambient_max.get(zname, self.ambient_max):
                self.zone_ambient_max[zname] = v
        else:
            v = float(self.zone_ambient_max.get(zname, self.ambient_max)) + 0.5 * step
            v = max(0.0, min(120.0, v))
            self.zone_ambient_max[zname] = v
            if v < self.zone_ambient_min.get(zname, self.ambient_min):
                self.zone_ambient_min[zname] = v
        self.save_audio()

    # --- Persistence ---
    def save_audio(self) -> None:
        try:
            data = dict(self._audio_json or {})
            defaults = data.setdefault("defaults", {})
            md = defaults.setdefault("music", {})
            if self.intro_track:
                md["startup_track_id"] = self.intro_track
            if self.ingame_track:
                md["ingame_track_id"] = self.ingame_track

            ad = defaults.setdefault("ambient", {})
            ad["min_interval"] = float(self.ambient_min)
            ad["max_interval"] = float(self.ambient_max)

            dk = defaults.setdefault("ducking", {})
            dk["amount_db"] = float(self.duck_db)
            dk["hold_ms"] = int(self.duck_hold)
            dk["release_ms"] = int(self.duck_release)

            if self.zone_track or self.zone_ambient_min or self.zone_ambient_max:
                zdict = data.setdefault("zones", {})
                all_z = set(self.zone_track.keys()) | set(self.zone_ambient_min.keys()) | set(self.zone_ambient_max.keys())
                for zname in sorted(all_z):
                    rec = zdict.setdefault(zname, {})
                    mt = self.zone_track.get(zname)
                    if mt:
                        rec["music_track_id"] = mt
                    amb = rec.setdefault("ambient", {})
                    if zname in self.zone_ambient_min:
                        amb["min_interval"] = float(self.zone_ambient_min[zname])
                    if zname in self.zone_ambient_max:
                        amb["max_interval"] = float(self.zone_ambient_max[zname])

            self.repo.save(data)
            self._audio_json = data
        except Exception:
            # Non-fatal
            pass
