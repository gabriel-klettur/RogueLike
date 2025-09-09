from __future__ import annotations

import re
from typing import Final

# Reglas muy básicas de sanitización (placeholder)
MAX_LEN: Final[int] = 2000
CTRL_RE = re.compile(r"[\x00-\x08\x0B\x0C\x0E-\x1F]")


def sanitize_text(text: str) -> str:
    if not text:
        return ""
    # Eliminar controles
    t = CTRL_RE.sub("", text)
    # Trim y recortar longitud
    t = t.strip()
    if len(t) > MAX_LEN:
        t = t[:MAX_LEN]
    return t
