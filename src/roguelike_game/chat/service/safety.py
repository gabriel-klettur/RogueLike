from __future__ import annotations

import re
import unicodedata as _ud
from typing import Final

# Reglas muy básicas de sanitización (placeholder)
MAX_LEN: Final[int] = 2000
CTRL_RE = re.compile(r"[\x00-\x08\x0B\x0C\x0E-\x1F]")
# Caracteres de ancho cero y BOM que suelen colarse en textos LLM
ZERO_WIDTH_RE = re.compile(r"[\u200B-\u200D\uFEFF]")
# Sustituto Unicode (�)
REPLACEMENT_CHAR_RE = re.compile(r"\uFFFD")
# Todo lo que esté fuera del BMP (incluye la mayoría de emojis y pictogramas)
NON_BMP_RE = re.compile(r"[\U00010000-\U0010FFFF]")


def sanitize_text(text: str) -> str:
    if not text:
        return ""
    # Eliminar controles
    t = CTRL_RE.sub("", text)
    # Eliminar zero-width y BOM
    t = ZERO_WIDTH_RE.sub("", t)
    # Eliminar el sustituto Unicode si aparece
    t = REPLACEMENT_CHAR_RE.sub("", t)
    # Eliminar caracteres fuera del BMP (emojis y pictogramas)
    try:
        t = NON_BMP_RE.sub("", t)
    except re.error:
        # Algunos motores podrían no soportar rangos altos; en ese caso, dejar tal cual
        pass
    # Filtrado por categoría Unicode (eliminar símbolos 'So' y categorías de control/formato)
    cleaned_chars = []
    for ch in t:
        try:
            cat = _ud.category(ch)
        except Exception:
            # Si no podemos categorizar, descartar
            continue
        # Categorías a excluir completamente
        if cat in {"Cf", "Cs", "Co", "Cc"}:
            continue
        # Otros símbolos (incluye emojis, pictogramas, flechas, formas)
        if cat == "So":
            continue
        cleaned_chars.append(ch)
    t = ("".join(cleaned_chars)).strip()
    if len(t) > MAX_LEN:
        t = t[:MAX_LEN]
    return t
