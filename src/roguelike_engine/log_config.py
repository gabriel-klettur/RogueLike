"""
Centralized logging configuration for the RogueLike project.
Adds colorized console output and optional rotating file logging.
"""

import logging
import logging.config
from logging.handlers import RotatingFileHandler
from pathlib import Path

# ANSI color codes
RESET = "\033[0m"
COLOR_TIME = "\033[36m"      # Cyan
COLOR_LEVEL = "\033[33m"     # Yellow
COLOR_NAME = "\033[35m"      # Magenta
COLOR_MSG = "\033[37m"       # White

class ColorFormatter(logging.Formatter):
    """
    Custom formatter to add colors to log output in console.
    """
    def format(self, record):
        # Formatear timestamp
        asctime = self.formatTime(record, datefmt="%H:%M:%S")
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
