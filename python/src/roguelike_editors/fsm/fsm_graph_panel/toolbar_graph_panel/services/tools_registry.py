from __future__ import annotations
from typing import Any, Optional

# Registry that lazily imports per-tool Events (and optionally Controller/View/Model) bundles.
# Tools are optional: if a module is missing or incomplete, the registry degrades gracefully.

class _ToolBundle:
    def __init__(self, *, events: Optional[object] = None, controller: Optional[object] = None,
                 model: Optional[object] = None, view: Optional[object] = None) -> None:
        self.events = events
        self.controller = controller
        self.model = model
        self.view = view


def _try_import(path: str, name: str) -> Optional[object]:
    try:
        mod = __import__(path, fromlist=[name])
        return getattr(mod, name, None)
    except Exception:
        return None


# Canonicalize toolbar keys to package names
_KEY_TO_PKG = {
    'add_node': 'add_node',
    'clone_node': 'clone',
    'connect': 'connect',
    'disconnect': 'disconnect',
    'delete': 'delete_node',
    'mark_ini': 'mark_ini',
    'mark_end': 'mark_end',
}


def get_tool_bundle(key: str) -> _ToolBundle:
    pkg = _KEY_TO_PKG.get(key)
    if not pkg:
        return _ToolBundle()
    base = f"{__name__.rsplit('.services', 1)[0]}.{pkg}"
    # Preferred module names: <pkg>_*.py; Alt: <first>_*.py and <last>_*.py (e.g., delete_node -> delete_*.py)
    parts = pkg.split('_')
    preferred = pkg
    first = parts[0]
    last = parts[-1]
    module_candidates = []
    for name in (preferred, first, last):
        if name not in module_candidates:
            module_candidates.append(name)

    # Class name candidates
    cls_core = pkg.title().replace('_', '')  # e.g., DeleteNode
    cls_fallback = ''.join([p.capitalize() for p in pkg.split('_')])  # e.g., DeleteNode
    if not cls_core:
        cls_core = cls_fallback

    # Events
    events = None
    for mod in module_candidates:
        events = (
            _try_import(f"{base}.{mod}_events", f"{cls_core}EventHandler")
            or _try_import(f"{base}.{mod}_events", f"{cls_fallback}EventHandler")
        )
        if events:
            break
    # Controller
    controller = None
    for mod in module_candidates:
        controller = (
            _try_import(f"{base}.{mod}_controller", f"{cls_core}Controller")
            or _try_import(f"{base}.{mod}_controller", f"{cls_fallback}Controller")
        )
        if controller:
            break
    # Model
    model = None
    for mod in module_candidates:
        model = (
            _try_import(f"{base}.{mod}_model", f"{cls_core}Model")
            or _try_import(f"{base}.{mod}_model", f"{cls_fallback}Model")
        )
        if model:
            break
    # View
    view = None
    for mod in module_candidates:
        view = (
            _try_import(f"{base}.{mod}_view", f"{cls_core}View")
            or _try_import(f"{base}.{mod}_view", f"{cls_fallback}View")
        )
        if view:
            break
    return _ToolBundle(events=events, controller=controller, model=model, view=view)


__all__ = [
    'get_tool_bundle',
]
