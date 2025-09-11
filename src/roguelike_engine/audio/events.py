from dataclasses import dataclass
from typing import Optional, List

# --- Music commands ---
@dataclass
class PlayMusic:
    track_id: Optional[str] = None
    path: Optional[str] = None
    loop: bool = True
    volume: Optional[float] = None  # None -> keep current
    fade_in_ms: int = 0

@dataclass
class StopMusic:
    fade_ms: int = 300

@dataclass
class Crossfade:
    to_track_id: Optional[str] = None
    to_path: Optional[str] = None
    duration_ms: int = 600
    target_volume: Optional[float] = None

@dataclass
class SetMusicVolume:
    value: float = 1.0

# --- SFX commands ---
@dataclass
class PlaySfx:
    sfx_id: Optional[str] = None
    path: Optional[str] = None
    volume: Optional[float] = None
    pan: Optional[float] = None  # -1.0 left, 0 center, 1.0 right
    group: str = "sfx"

@dataclass
class StopSfx:
    sfx_id: Optional[str] = None
    group: Optional[str] = None

@dataclass
class SetSfxVolume:
    value: float = 1.0

@dataclass
class SetAmbientVolume:
    value: float = 1.0

# --- Ducking ---
@dataclass
class DuckMusic:
    amount_db: float = -6.0
    hold_ms: int = 300
    release_ms: int = 200

# --- Playlist ---
@dataclass
class PlaylistSet:
    entries: List[str] = None  # list of track_ids
    mode: str = "loop"  # 'loop' | 'once' | 'shuffle'
