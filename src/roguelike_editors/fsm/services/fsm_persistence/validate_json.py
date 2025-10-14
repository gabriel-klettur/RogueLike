from __future__ import annotations
from typing import Any, Dict
from pathlib import Path


def validate(data: Dict[str, Any], schema_path: str | Path) -> None:
    """Validate data with JSON Schema if 'jsonschema' is available.
    Raise ValueError on validation errors. No-op if schema not found or dependency missing.
    """
    try:
        import json
        import jsonschema  # type: ignore
        with open(str(schema_path), "r", encoding="utf-8") as f:
            schema = json.load(f)
        jsonschema.validate(instance=data, schema=schema)
    except FileNotFoundError:
        return
    except ImportError:
        return
