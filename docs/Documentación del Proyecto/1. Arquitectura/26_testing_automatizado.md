# 🧪 26. Testing Automatizado – Framework, Módulos Testeables y Buenas Prácticas

Este documento detalla cómo implementar y organizar pruebas automatizadas para validar funcionalidades del juego de forma estructurada, reutilizable y escalable.

---

## 🎯 Objetivo

- Detectar errores rápidamente al modificar código.
- Evitar regresiones en funcionalidades ya implementadas.
- Automatizar la validación de lógica, cálculos y estructuras internas.

---

## 🧰 Framework Utilizado

> 📦 `unittest` (incluido en la librería estándar de Python)

Características:
- Ligero y rápido.
- Compatible con CI/CD.
- Facilita agrupación y ejecución por módulos.

---

## 📁 Estructura de Carpeta Sugerida

```bash
tests/
├── __init__.py
├── test_player.py           # Test de stats, movimiento, daño
├── test_map_generation.py   # Test de mapas procedurales
├── test_utils.py            # Test de utilidades (loaders, helpers)
├── test_combat.py           # Daño, cooldowns, habilidades
└── test_network.py          # Validación de mensajes enviados y recibidos
```

---

## 🧪 Módulos actuales testeables

| Módulo                                | Tests sugeridos                                         |
|---------------------------------------|---------------------------------------------------------|
| `entities/player/stats.py`           | Aumentar/reducir stats, restaurar, límites              |
| `entities/player/movement.py`        | Movimiento con colisiones, dirección resultante         |
| `entities/projectiles/*` (futuro)    | Trayectoria, colisión, daño                             |
| `utils/loader.py`                    | Carga de imágenes, manejo de rutas inválidas            |
| `map/generator.py`                   | Número y posición de habitaciones, no solapamientos     |
| `network/client.py`                  | Serialización, detección de cambios, estructura JSON    |
| `core/game/state.py` (futuro)        | Cambios de estado del juego, pausa, reinicio            |

---

## 🧪 Ejemplo de Test con `unittest`

```python
import unittest
from src.roguelike_project.entities.player.stats import PlayerStats

class TestPlayerStats(unittest.TestCase):
    def setUp(self):
        self.stats = PlayerStats("first_hero")

    def test_restore_all(self):
        self.stats.health = 0
        self.stats.restore_all()
        self.assertEqual(self.stats.health, self.stats.max_health)

    def test_take_damage(self):
        initial = self.stats.health
        self.stats.take_damage()
        self.assertLess(self.stats.health, initial)
```

Para correrlo:

```bash
python -m unittest discover tests
```

---

## 🔁 Buenas Prácticas

| Recomendación                        | Descripción                                                               |
|-------------------------------------|---------------------------------------------------------------------------|
| ✅ Tests pequeños y específicos     | Validar solo una cosa por test.                                           |
| ✅ Usar `setUp()`                    | Inicializa objetos reutilizables.                                         |
| ✅ Nombres descriptivos              | `test_restore_all()` es mejor que `test_1()`.                            |
| ✅ Automatizar en CI (futuro)        | Integrar con GitHub Actions para validar pull requests automáticamente.   |
| ✅ Testear lógica pura               | No probar gráficos directamente, pero sí cálculo de stats, daño, etc.    |

---

## 🚫 Qué **no** testear (por ahora)

| Elemento             | Motivo                                                                 |
|----------------------|------------------------------------------------------------------------|
| Renderizado visual   | Depende de Pygame; se prueba manualmente por ahora.                    |
| Cámara               | Difícil de automatizar sin entorno gráfico.                            |
| Pygame Events        | Se testean indirectamente vía tests de input o integración.            |

---

## 🧪 Avances Futuro

| Mejora                           | Propósito                                                  |
|----------------------------------|-------------------------------------------------------------|
| ✅ Test coverage                 | Medir qué porcentaje del código tiene pruebas.             |
| ✅ Mocks para red                | Simular el servidor WebSocket sin conexión real.           |
| ✅ Tests con data externa        | Validar funcionamiento con múltiples mapas, stats, etc.    |
| ✅ Integración con CI            | Ejecutar automáticamente al hacer push o PR.               |

---

## 📌 Plantilla para nuevos archivos de test

```python
import unittest

class Test<NOMBRE_DEL_MODULO>(unittest.TestCase):

    def setUp(self):
        # Setup común para todos los tests
        pass

    def test_<funcion>_deberia_<resultado>():
        # Arrange
        # Act
        # Assert
        self.assertEqual(1, 1)
```

---
