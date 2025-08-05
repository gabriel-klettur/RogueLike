"""
Centralized logging configuration for the RogueLike project.
"""

import logging
import logging.config
from logging.handlers import RotatingFileHandler
from pathlib import Path


def init_logging(config_path: str = None, level: str = "INFO", logfile: str = None) -> None:
    """
    Initialize logging for the application.

    If `config_path` is provided and points to a valid file, load logging
    configuration from it. Otherwise, configure basic logging with a console
    handler and, optionally, a rotating file handler.

    Args:
        config_path: Optional path to a logging configuration file.
        level: Default log level (e.g., "DEBUG", "INFO").
        logfile: Path to the log file. If provided, a RotatingFileHandler is added.
    """
    if config_path and Path(config_path).is_file():
        logging.config.fileConfig(config_path, disable_existing_loggers=False)
    else:
        handlers = []

        # Custom format: only time (HH:MM:SS), level, module, message
        fmt = "[%(asctime)s][%(levelname)s][%(name)s]: %(message)s"
        datefmt = "%H:%M:%S"  # Only time, no date

        # Rotating file handler if logfile provided
        if logfile:
            log_path = Path(logfile)
            # Ensure directory exists
            log_path.parent.mkdir(parents=True, exist_ok=True)
            handlers.append(
                RotatingFileHandler(
                    filename=str(log_path),
                    maxBytes=10 * 1024 * 1024,  # 10 MB per file
                    backupCount=5,              # Keep 5 backups
                    encoding='utf-8'
                )
            )

        # Always log to console as well
        handlers.append(logging.StreamHandler())

        # Configure root logger
        logging.basicConfig(
            level=getattr(logging, level.upper(), logging.INFO),
            format=fmt,
            datefmt=datefmt,
            handlers=handlers,
        )
