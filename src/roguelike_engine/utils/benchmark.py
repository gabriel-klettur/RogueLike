import time
from functools import wraps

# Número máximo de muestras por clave para evitar crecimiento descontrolado de memoria
MAX_SAMPLES_PER_KEY = 300

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
                lst = perf_log.setdefault(key, [])
                lst.append(elapsed)
                # Limitar tamaño del buffer por clave
                if len(lst) > MAX_SAMPLES_PER_KEY:
                    # Mantener solo las últimas N muestras
                    del lst[:-MAX_SAMPLES_PER_KEY]
            return result

        return wrapper
    return decorator