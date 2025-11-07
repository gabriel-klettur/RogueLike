from __future__ import annotations

import sys
from pathlib import Path
import pytest

ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from tests.utils.fakes import FakeWorld, FakeCamera  # reuse


@pytest.fixture()
def world():
    return FakeWorld()


@pytest.fixture()
def camera():
    return FakeCamera()
