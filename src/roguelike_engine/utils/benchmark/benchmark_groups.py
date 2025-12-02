from __future__ import annotations

from typing import Any, Optional, Union

from .benchmark import benchmark


class BenchmarkGroup:
    """Helper to build hierarchical benchmark keys like 1.*, 1.1.*, 5.01.*.

    It wraps the existing `benchmark` decorator so callers only specify a
    numeric/string prefix and a local name or label.
    """

    def __init__(
        self,
        perf_log_source: Any,
        prefix: str,
        auto_index: bool = False,
    ) -> None:
        self.perf_log_source = perf_log_source
        self.prefix = prefix
        self.auto_index = auto_index
        self._counter: int = 0

    def subgroup(self, idx: Union[int, str]) -> "BenchmarkGroup":
        """Return a subgroup with prefix `<self.prefix>.<idx>`.

        Example: BenchmarkGroup(perf_log, "1").subgroup(1) -> "1.1".
        """
        return BenchmarkGroup(self.perf_log_source, f"{self.prefix}.{idx}")

    def bench(self, name: Optional[str] = None):
        """Return a benchmark decorator for `<prefix>.<name>`.

        If `name` is None or empty, uses just `prefix` as the key.
        """
        if not name:
            key = self.prefix
        else:
            key = f"{self.prefix}.{name}"
        return benchmark(self.perf_log_source, key)

    def next(self, label: str):
        """Return a decorator with an auto-incremented index.

        Keys look like `<prefix>.<01>.<label>`, `<prefix>.<02>.<label>`, ...
        Counter is per-`BenchmarkGroup` instance, so if you want the same
        numbering each frame, create the group inside the per-frame function.
        """
        self._counter += 1
        key = f"{self.prefix}.{self._counter:02d}.{label}"
        return benchmark(self.perf_log_source, key)


__all__ = ["BenchmarkGroup"]
