from __future__ import annotations


def make_minimal_sets_doc() -> dict:
    """Return a minimal valid sets.json structure with one set and one state.
    We keep transitions empty; callers can extend as needed.
    """
    return {
        "version": 1,
        "sets": [
            {
                "id": "TestSet",
                "label": "Test Set",
                "initial": "Idle",
                "states": [
                    {"id": "Idle", "label": "Idle", "class": "IdleState", "props": {}}
                ],
                "transitions": [],
            }
        ],
    }
