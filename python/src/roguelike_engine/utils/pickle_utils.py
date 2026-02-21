"""Utilities for stable pickling across test reloads.

These helpers live in a canonical module to avoid duplicate function identity
issues that can occur when a module is imported under multiple names.
"""
from __future__ import annotations


def rebuild_map_view():
    """Factory used by pickle to rebuild a MapView instance.

    Imported from a stable utils module to ensure consistent identity.
    """
    from roguelike_engine.map.view.map_view import MapView
    return MapView()
