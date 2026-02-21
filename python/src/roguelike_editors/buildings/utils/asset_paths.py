import os
from typing import Optional


def normalize_asset_path(p: Optional[str]) -> Optional[str]:
    """Return a normalized asset path using forward slashes and lowercase extension.

    Keeps input unchanged if not a non-empty string. Collapses duplicate slashes.
    """
    try:
        if not p or not isinstance(p, str):
            return p
        q = p.replace("\\", "/")
        while "//" in q:
            q = q.replace("//", "/")
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return p
