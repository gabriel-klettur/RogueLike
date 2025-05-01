

# 💾 9. Save System – Sistema de Guardado Local y Multijugador

Este documento describe cómo debe diseñarse e implementarse el sistema de guardado del juego, tanto en modo **local** como en modo **multijugador**.

---

## 🎯 Objetivo

- Guardar y cargar información persistente del jugador, progresión, configuración y mundo.
- Soportar múltiples partidas guardadas (slots).
- Permitir una migración futura al modo multijugador con sincronización remota.
- Asegurar la integridad de los datos y mantener compatibilidad futura.

---

## 🗂️ Estructura de Carpetas Propuesta

```bash
src.roguelike_project/
└── save_system/
    ├── local/
    │   ├── local_saver.py         # Guardar/cargar datos en .json
    │   ├── config_handler.py      # Preferencias del usuario (audio, resolución, etc.)
    │   ├── schema.json            # Estructura esperada de los archivos .json
    │   └── slot_manager.py        # Selector y manejador de múltiples partidas
    │
    ├── multiplayer/
    │   ├── multiplayer_sync.py    # Comunicación con el servidor (POST, GET, PATCH)
    │   ├── serializers.py         # Conversión de objetos del juego a estructuras serializables
    │   └── validation.py          # Validación de paquetes recibidos
    │
    └── utils/
        ├── json_utils.py          # Lectura/escritura segura con control de errores
        └── versioning.py          # Manejo de versiones de archivos guardados
```

---

### 🗃️ Directorio externo para archivos `.json` (modo local)

```bash
saves/
├── slot_1.json
├── slot_2.json
├── slot_3.json
└── config.json
```

Estos archivos contendrán el estado serializado del juego y las preferencias del usuario.

---

## 🧠 Qué se debe guardar

### 📦 A. Información mínima por partida (`slot_1.json`)

- Posición y dirección del jugador.
- Personaje seleccionado.
- Stats actuales y máximos (vida, maná, energía).
- Inventario, oro, hechizos, nivel.
- Nivel del mapa o semilla de generación procedural.
- Tiempo jugado, fecha del último guardado.

### ⚙️ B. Configuración del usuario (`config.json`)

- Audio: volumen general, efectos, música.
- Resolución de pantalla.
- Preferencias de idioma.
- Último personaje usado.
- Último slot de guardado.

---

## 🖥️ Formatos de almacenamiento

- **Formato principal:** `.json`
  - Simple de leer y editar.
  - Ideal para prototipado y depuración.
- **Futuro alternativo (modo multijugador):**
  - **`sqlite`** local para mejoras de rendimiento.
  - **`MySQL` / `PostgreSQL` remoto** en caso de servidor persistente.

---

## 📌 Consideraciones clave para implementación

| Consideración                      | Descripción                                                                 |
|-----------------------------------|-----------------------------------------------------------------------------|
| 🎯 Múltiples slots                | Implementar selector, creación/eliminación de slots.                        |
| 🧪 Validación de estructura       | Usar `schema.json` para validar contenido antes de cargar.                  |
| 🛡️ Manejo de errores             | Guardado corrupto → fallback + mensaje amigable al jugador.                |
| ♻️ Versionado de saves           | Soporte para migrar entre versiones del juego.                             |
| 🔗 Integración con UI            | Menú de "Cargar partida", "Nueva partida", "Configuración".                |

---

## 🔐 Save System en Multijugador (futuro)

- El sistema debe poder sincronizar:
  - Progreso del jugador.
  - Estado del mundo (si hay persistencia).
  - Mejoras del pueblo (modo cooperativo).
- Se utilizará una estructura similar a los saves locales, pero convertida a JSON y enviada por red.

---

## 🧰 Utilidades requeridas

| Utilidad                 | Uso principal                                       |
|--------------------------|-----------------------------------------------------|
| `json_utils.py`          | Cargar y guardar archivos con try/except            |
| `versioning.py`          | Añadir campo `"version": "1.0"` y migrar si cambia  |
| `schema.json`            | Validar estructura esperada del archivo             |

---

## 📝 Próximos pasos sugeridos

1. Implementar `local_saver.py` para guardar/cargar una estructura simple.
2. Crear estructura base de `slot_manager.py` para soportar múltiples saves.
3. Integrar opción en el menú principal para:
   - Nueva partida (slot vacío).
   - Cargar partida existente.
   - Borrar slot.
4. Diseñar `schema.json` con campos obligatorios y opcionales.
5. Evaluar guardado automático cada X minutos o al salir del juego.

---

Este sistema será una parte crítica del MVP del juego, especialmente para soportar progresión del jugador, exploración prolongada y sistemas complejos como inventario, niveles o progresión de pueblo.

¿Listo para comenzar con `local_saver.py` o el diseño del schema.json?