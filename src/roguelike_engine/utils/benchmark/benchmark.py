import time
import inspect
from functools import wraps

# Número máximo de muestras por clave para evitar crecimiento descontrolado de memoria
MAX_SAMPLES_PER_KEY = 300

def benchmark(perf_log_source, key):
    def decorator(func):
        # Cache function signature for arg binding (supports positional/keyword extraction)
        try:
            _sig = inspect.signature(func)
        except Exception:
            _sig = None
        @wraps(func)
        def wrapper(*args, **kwargs):
            # Soporta lambda o diccionario directo
            # 1) Try to resolve perf log from passed arguments by name (works for positional/keyword)
            perf_log = None
            if _sig is not None:
                try:
                    bound = _sig.bind_partial(*args, **kwargs)
                    if 'perf_log' in bound.arguments:
                        perf_log = bound.arguments.get('perf_log')
                    elif 'performance_log' in bound.arguments:
                        perf_log = bound.arguments.get('performance_log')
                except Exception:
                    pass
            # 2) If not found, prefer kwargs direct access (backup)
            if perf_log is None:
                if 'perf_log' in kwargs:
                    perf_log = kwargs.get('perf_log')
                elif 'performance_log' in kwargs:
                    perf_log = kwargs.get('performance_log')
            # 3) If still not found, resolve via provided source (callable or dict)
            if perf_log is None and callable(perf_log_source):
                # Permite usar lambda: performance_log (0 args) o lambda self: self.perf_log (1 arg)
                try:
                    if args:
                        try:
                            perf_log = perf_log_source(args[0])
                        except TypeError:
                            # La fuente no acepta argumentos -> llamar sin ellos
                            perf_log = perf_log_source()
                    else:
                        # No hay args (función libre) -> llamar sin argumentos
                        perf_log = perf_log_source()
                except Exception:
                    # Ante cualquier fallo, desactiva el log para no romper la ejecución
                    perf_log = None
            elif perf_log is None:
                perf_log = perf_log_source
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