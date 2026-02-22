"""
Centralized logging configuration for the RogueLike project.
Adds colorized console output and optional rotating file logging.
"""

from __future__ import annotations

import logging
import logging.config
from logging.handlers import RotatingFileHandler
from pathlib import Path
import os
from datetime import datetime
import re
import time

# ANSI color codes
RESET = "\033[0m"
COLOR_TIME = "\033[36m"      # Cyan
COLOR_LEVEL = "\033[33m"     # Yellow
COLOR_NAME = "\033[35m"      # Magenta
COLOR_MSG = "\033[37m"       # White
COLOR_WARNING = "\033[38;5;214m"  # Orange
COLOR_ERROR = "\033[31m"          # Red
COLOR_CRITICAL = "\033[31m"       # Red

class ColorFormatter(logging.Formatter):
    """
    Custom formatter to add colors to log output in console.
    """
    def format(self, record):
        # Formatear timestamp
        asctime = self.formatTime(record, datefmt="%H:%M:%S")
        # Override: full line orange for WARNING, red for ERROR/CRITICAL
        raw = f"[{asctime}][{record.levelname}][{record.name}]: {record.getMessage()}"
        if record.levelno == logging.WARNING:
            return f"{COLOR_WARNING}{raw}{RESET}"
        if record.levelno >= logging.ERROR:
            return f"{COLOR_ERROR}{raw}{RESET}"
        # Timestamp en cyan
        time_part  = f"{COLOR_TIME}[{asctime}]{RESET}"
        # Nivel siempre en amarillo
        level_part = f"{COLOR_LEVEL}[{record.levelname}]{RESET}"
        # Mensaje siempre en blanco
        msg_part   = f"{COLOR_MSG}{record.getMessage()}{RESET}"
        if record.levelno == logging.INFO:
            # Para INFO: nombre de logger y función tras nivel            
            return f"{time_part}{level_part}: {msg_part}"
        # Otros niveles: incluir nombre del logger
        name_part  = f"{COLOR_NAME}[{record.name}]{RESET}"
        return f"{time_part}{level_part}{name_part}: {msg_part}"


class RateLimitFilter(logging.Filter):
    """Rate-limit duplicate log messages per logger prefix and level.

    - Maintains a per-key (logger prefix, level, normalized message) state.
    - Suppresses logs within a time window, counting how many were suppressed.
    - On first allowed log after the window, optionally appends "; suppressed=N".

    Normalization removes any trailing "suppressed=\d+" to make keys stable even
    when upstream code already aggregates duplicates.
    """

    _SUPPRESSED_RE = re.compile(r";?\s*suppressed=\d+")

    def __init__(
        self,
        *,
        default_window_ms: int = 0,
        rules: list[tuple[str, int]] | None = None,
    ) -> None:
        """Create the filter.

        Args:
            default_window_ms: Window in ms for logs without a matching rule. 0 disables.
            rules: List of (logger_name_prefix, window_ms).
        """
        super().__init__()
        self.default_window_ms = int(default_window_ms) if default_window_ms else 0
        self.rules = [(p, int(w)) for p, w in (rules or []) if int(w) > 0]
        # key -> (last_ms, suppressed_count)
        self._state: dict[tuple[str, int, str], tuple[int, int]] = {}

    @staticmethod
    def _normalize_message(msg: str) -> str:
        """Strip variable suppressed counters to stabilize dedup keys."""
        return RateLimitFilter._SUPPRESSED_RE.sub("", msg).strip()

    def _window_for(self, record: logging.LogRecord) -> tuple[str, int]:
        """Return (matched_prefix, window_ms) for the record."""
        name = record.name or ""
        for prefix, win in self.rules:
            if name.startswith(prefix):
                return prefix, win
        return "", self.default_window_ms

    def filter(self, record: logging.LogRecord) -> bool:  # noqa: D401
        """Return True to allow the record, False to suppress within window."""
        prefix, window_ms = self._window_for(record)
        if window_ms <= 0:
            return True

        # Build a stable message for keying
        try:
            # getMessage() safely formats with args if present
            rendered = record.getMessage()
        except Exception:
            rendered = str(record.msg)
        normalized = self._normalize_message(rendered)

        key = (prefix or record.name or "", int(record.levelno), normalized)
        now_ms = int(time.monotonic() * 1000)
        last_ms, suppressed = self._state.get(key, (-10_000_000, 0))

        if now_ms - last_ms >= window_ms:
            # Allow and report how many were suppressed, but avoid duplicating
            # downstream "suppressed=" if it already exists in the message.
            if suppressed > 0 and "suppressed=" not in rendered:
                new_msg = f"{rendered}; suppressed={suppressed}"
                record.msg = new_msg
                record.args = None
            # Reset window
            self._state[key] = (now_ms, 0)
            return True
        else:
            # Suppress and accumulate count
            self._state[key] = (last_ms, suppressed + 1)
            return False

def build_log_filepath(base_name: str, directory: str = "logs", extension: str = "log", now_dt: datetime | None = None) -> Path:
    """
    Build a standardized timestamped log filepath with the pattern:
    "nombre_nombre_nombre_dia_mes_year--hora_minuto_segundo.extension"

    Example: build_log_filepath("roguelike") -> logs/roguelike_15_09_2025--11_22_30.log
    """
    now = now_dt or datetime.now()
    filename = f"{base_name}_{now.day:02d}_{now.month:02d}_{now.year:04d}--{now.hour:02d}_{now.minute:02d}_{now.second:02d}.{extension}"
    return Path(directory) / filename

def init_logging(config_path: str = None, level: str = "INFO", logfile: str = None) -> None:
    """
    Initialize logging for the application.

    If `config_path` is provided and points to a valid file, load logging
    configuration from it. Otherwise, configure basic logging with:
    - Colorized console output.
    - Optional rotating file handler.

    Args:
        config_path: Optional path to a logging configuration file.
        level: Default log level (e.g., "DEBUG", "INFO").
        logfile: Path to the log file. If provided, a RotatingFileHandler is added.
    """
    if config_path and Path(config_path).is_file():
        logging.config.fileConfig(config_path, disable_existing_loggers=False)
    else:
        handlers = []

        # Console handler with colors
        console_handler = logging.StreamHandler()
        console_handler.setFormatter(ColorFormatter())
        handlers.append(console_handler)

        # Rotating file handler (without colors)
        if logfile:
            log_path = Path(logfile)
            log_path.parent.mkdir(parents=True, exist_ok=True)
            file_handler = RotatingFileHandler(
                filename=str(log_path),
                maxBytes=10 * 1024 * 1024,  # 10 MB per file
                backupCount=5,              # Keep 5 backups
                encoding='utf-8'
            )
            file_fmt = "[%(asctime)s][%(levelname)s][%(name)s]: %(message)s"
            file_handler.setFormatter(logging.Formatter(file_fmt, datefmt="%H:%M:%S"))
            handlers.append(file_handler)

        # Configure root logger
        logging.basicConfig(
            level=getattr(logging, level.upper(), logging.INFO),
            handlers=handlers,
        )
        # Install rate-limit filter(s)
        try:
            # Read environment overrides
            spawner_ms_env = os.getenv("RL_RATELIMIT_SPAWNER_MS")
            spawner_ms = int(spawner_ms_env) if spawner_ms_env else 5000
            default_ms_env = os.getenv("RL_RATELIMIT_DEFAULT_MS")
            default_ms = int(default_ms_env) if default_ms_env else 0

            rate_filter = RateLimitFilter(
                default_window_ms=default_ms,
                rules=[
                    ("roguelike_editors.spawner", spawner_ms),
                ],
            )

            console_handler.addFilter(rate_filter)
            # If file handler exists, also add the filter to it (last in list)
            for h in handlers:
                if isinstance(h, RotatingFileHandler):
                    h.addFilter(rate_filter)
        except Exception:
            # Filters are optional; logging must not crash
            pass
        # Optional per-module level overrides via env vars
        # RL_LOG_LEVEL_SPELLS or RL_SPELLS_LOG_LEVEL -> applies to 'roguelike_editors.spells' and children
        try:
            spells_level = os.getenv('RL_LOG_LEVEL_SPELLS') or os.getenv('RL_SPELLS_LOG_LEVEL')
            if spells_level:
                lvl = getattr(logging, spells_level.upper(), None)
                if isinstance(lvl, int):
                    logging.getLogger('roguelike_editors.spells').setLevel(lvl)
        except Exception:
            pass
