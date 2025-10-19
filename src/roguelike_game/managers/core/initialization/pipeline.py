from __future__ import annotations

import logging
import time
from typing import Callable, Iterable, Optional

from .types import InitContext, Stage

logger = logging.getLogger(__name__)


def _call_stage_fn(fn, ctx: InitContext) -> None:
    """Call a stage function supporting both (ctx) and no-arg callables.

    This preserves compatibility with externally provided extras that may not
    accept a context parameter.
    """
    try:
        return fn(ctx)  # type: ignore[arg-type]
    except TypeError:
        return fn()  # type: ignore[misc]


def run_stages(
    ctx: InitContext,
    stages: Iterable[Stage],
    on_stage_completed: Optional[Callable[[str, Callable[..., None], float], None]] = None,
) -> None:
    """Run the given stages with timing, progress, and logging.

    - Advances the loading screen progress after each stage.
    - Logs timing to the root logger so file handlers can capture it.
    - Optionally invokes a callback after each stage completes.
    """
    stages = list(stages)
    total = len(stages)
    for i, (msg, fn) in enumerate(stages):
        t0 = time.time()
        _call_stage_fn(fn, ctx)
        elapsed = time.time() - t0
        frac = (i + 1) / max(1, total)
        try:
            ctx.game.loader.draw(frac, msg)
        except Exception:
            pass
        base = getattr(fn, 'func', fn)
        name = getattr(base, '__qualname__', getattr(base, '__name__', str(base)))
        logger.info(f"[{name}]: {msg}: {elapsed:.4f}s")
        if on_stage_completed is not None:
            try:
                on_stage_completed(msg, fn, elapsed)
            except Exception:
                # Avoid breaking the pipeline on observer errors
                pass
