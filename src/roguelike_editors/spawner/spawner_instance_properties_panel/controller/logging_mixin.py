from __future__ import annotations

import time
import pygame


class LoggingMixin:
    def _now_ms(self) -> int:
        try:
            import pygame as _pg
            return int(_pg.time.get_ticks() or 0)
        except (ImportError, AttributeError, TypeError, ValueError):
            try:
                return int(time.time() * 1000)
            except Exception:
                return 0

    def _should_log(self, key: str, msg: str, interval_ms: int = 800) -> bool:
        try:
            now = self._now_ms()
            last = self._log_last.get(key)
            if last is not None:
                last_ts, last_msg = last
                if (now - last_ts) < int(interval_ms) and str(last_msg) == str(msg):
                    return False
            self._log_last[key] = (now, str(msg))
            return True
        except Exception:
            return True

    def _log_info_rl(self, key: str, msg: str, interval_ms: int = 800) -> None:
        if self._should_log(key, msg, interval_ms):
            try:
                self._log.info(msg)
            except Exception:
                pass

    def _log_debug_rl(self, key: str, msg: str, interval_ms: int = 800) -> None:
        if self._should_log(key, msg, interval_ms):
            try:
                self._log.debug(msg)
            except Exception:
                pass

    def _log_warning_rl(self, key: str, msg: str, interval_ms: int = 800) -> None:
        if self._should_log(key, msg, interval_ms):
            try:
                self._log.warning(msg)
            except Exception:
                pass
