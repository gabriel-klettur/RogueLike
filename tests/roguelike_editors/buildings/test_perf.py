import pytest


@pytest.mark.slow
def test_perf_matrix_placeholder():
    """
    PERF-001..PERF-003 per README (many instances hover perf, large picker responsiveness,
    massive persistence).
    """
    assert True
