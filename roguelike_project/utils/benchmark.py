# roguelike_project/utils/benchmark.py

import time
from functools import wraps

def benchmark(perf_log, key):
    def decorator(func):
        @wraps(func)
        def wrapper(*args, **kwargs):
            start = time.perf_counter()
            result = func(*args, **kwargs)
            elapsed = time.perf_counter() - start
            if perf_log is not None:
                perf_log[key].append(elapsed)
            return result
        return wrapper
    return decorator
