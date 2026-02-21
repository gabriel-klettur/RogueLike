"""Initialization package for game boot pipeline.

Provides stage types, a simple pipeline runner, and concrete stage functions
used by `GameInitializer` to build a readable, testable initialization flow.
"""

from .types import Stage, InitContext  # re-export for convenience
from .pipeline import run_stages
from . import stages as stage_funcs

__all__ = [
    "Stage",
    "InitContext",
    "run_stages",
    "stage_funcs",
]
