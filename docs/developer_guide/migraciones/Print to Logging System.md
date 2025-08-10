# Migración de `print()` al Sistema de Logging en Python

Este documento describe en detalle los pasos necesarios para profesionalizar el sistema de logs de tu proyecto, sustituyendo todas las llamadas a `print()` por el módulo estándar `logging` de Python, configurando niveles, handlers, CLI y una estrategia de migración masiva.

---

## 1. Reemplazar todas las llamadas a `print()` por `logger`

1.1. En cada módulo Python:
```python
import logging
logger = logging.getLogger(__name__)
```
1.2. Sustituye:
```python
print("Iniciando juego con modo DEBUG")
```  
Por:
```python
logger.debug("Iniciando juego en modo DEBUG")
```
1.3. Ventajas:
- Filtrado por nivel y módulo.
- Salida configurable (consola, fichero).
- Formato uniforme con timestamp, nivel y origen.

---

## 2. Definir y usar niveles de log claros

| Nivel     | Uso recomendado                                                 |
|-----------|-----------------------------------------------------------------|
| DEBUG     | Detalle extremo para desarrollo (variables, estados internos).  |
| INFO      | Eventos normales: inicio/parada, acciones del usuario.          |
| WARNING   | Situaciones inesperadas pero recuperables.                     |
| ERROR     | Errores de ejecución que afectan flujo, requieren revisión.     |
| CRITICAL  | Fallos graves que implican caída o riesgo de corrupción de datos |

Ejemplo:
```python
if not config.exists():
    logger.error("Archivo de configuración no encontrado: %s", path)
    sys.exit(1)
```  

---

## 3. Configuración centralizada de logging

### 3.1. Módulo `log_config.py`
```python
import logging
import logging.config

def init_logging(config_path=None, level="INFO", logfile=None):
    if config_path:
        logging.config.fileConfig(config_path, disable_existing_loggers=False)
    else:
        handlers = []
        fmt = "%(asctime)s [%(levelname)s] %(name)s: %(message)s"
        if logfile:
            handlers.append(logging.handlers.RotatingFileHandler(
                logfile, maxBytes=10*1024*1024, backupCount=5))
        handlers.append(logging.StreamHandler())
        logging.basicConfig(level=level, format=fmt, handlers=handlers)
```

### 3.2. Llamada desde tu launcher:
```python
from log_config import init_logging

init_logging(
    config_path=args.log_config,
    level=args.log_level,
    logfile=args.log_file
)
```  

---

## 4. Handlers recomendados

- **StreamHandler**: salida consola, útil en desarrollo.
- **RotatingFileHandler** o **TimedRotatingFileHandler**: controla tamaño y retención histórica.
- **SMTPHandler** o integraciones (Sentry, ELK): notificaciones automáticas en producción.

```python
from logging.handlers import TimedRotatingFileHandler
handler = TimedRotatingFileHandler('logs/app.log', when='midnight', backupCount=7)
handler.setFormatter(logging.Formatter(fmt))
logger.addHandler(handler)
```

---

## 5. Control vía CLI y/o variables de entorno

### 5.1. `launcher.py` con `argparse`:
```python
import argparse

parser = argparse.ArgumentParser(description="Iniciar RogueLike")
parser.add_argument("--log-level", default="INFO",
    choices=["DEBUG","INFO","WARNING","ERROR","CRITICAL"]
)
parser.add_argument("--log-file", help="Ruta de archivo de log")
parser.add_argument("--log-config", help="Archivo de configuración de logging (INI/YAML)")
args = parser.parse_args()
```  

### 5.2. Variables de entorno (fallback):
```python
import os
level = os.getenv('LOG_LEVEL', args.log_level)
file_ = os.getenv('LOG_FILE', args.log_file)
```  

---

## 6. Estructura de loggers por módulo

- Crea un logger por módulo usando `__name__`.
- Configura niveles distintos por paquete:
```ini
[logger_roguelike]
level = DEBUG
handlers = consoleHandler
qualname = roguelike
```  
- En código:
```python
logger = logging.getLogger('roguelike.submodulo')
logger.info("Acción ejecutada en submódulo")
```

---

## 7. Estrategia de migración masiva

1. **Detección automática**: usa `grep -R "print(" src/ > prints.txt` o un script en Python que busque AST.
2. **Script de reemplazo**: con regex o AST, cambia `print(` → `logger.info(` (o nivel apropiado).
3. **Revisión manual**: inspecciona casos especiales (errores, warnings) y ajusta a `logger.error` o `logger.warning`.
4. **Pruebas**: ejecuta la suite de tests y el juego en modo DEBUG para asegurar que no queden `print()`.
5. **Despliegue progresivo**: primero en entorno de staging, luego producción.
6. **Monitoreo**: valida tamaños de log, busca patrones de ERROR/CRITICAL.

---

> Con estos pasos tendrás un sistema de logging robusto, configurable y listo para producción.
