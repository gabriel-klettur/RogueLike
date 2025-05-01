# src.roguelike_project/utils/benchmark.py

# Path: src/roguelike_engine/utils/benchmark.py
import time
from functools import wraps

def benchmark(perf_log_source, key):
    def decorator(func):
        @wraps(func)
        def wrapper(*args, **kwargs):
            # Soporta lambda o diccionario directo
            perf_log = perf_log_source(args[0]) if callable(perf_log_source) else perf_log_source
            start = time.perf_counter()
            result = func(*args, **kwargs)
            elapsed = time.perf_counter() - start
            if perf_log is not None:
                perf_log[key].append(elapsed)
            return result
        return wrapper
    return decorator