import pytest


@pytest.mark.slow
@pytest.mark.skip(reason="[PERF-*] Performance tests are skipped by default; enable when profiling.")
def test_perf_matrix_placeholder():
    """
    PERF-001..PERF-003 per README (many instances hover perf, large picker responsiveness,
    massive persistence).
    """
    assert True
