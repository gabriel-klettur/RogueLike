# 🔊 11. Audio System – Organización y Planificación de Sonido

Este documento describe cómo se organizarán los recursos de **audio**, cómo se implementarán, y qué herramientas y estructuras se usarán para integrarlos en el juego.

---

## 🎯 Objetivo

- Definir una estructura clara para efectos de sonido (SFX) y música.
- Sentar las bases para la futura implementación del sistema de audio.
- Proveer fuentes seguras y gratuitas para adquirir sonidos.

---

## 📁 Estructura de Carpetas Sugerida

```bash
assets/
└── sounds/
    ├── sfx/          # Efectos de sonido: ataques, pasos, UI...
    ├── music/        # Música ambiental o de niveles
    └── ui/           # Sonidos de interfaz gráfica (botones, clicks)
```

---

## 🎧 Convenciones de Nombre

| Tipo de Sonido  | Formato sugerido       | Ejemplo              |
|-----------------|------------------------|-----------------------|
| Golpes/jugador  | `player_event.wav`     | `player_hit.wav`      |
| Enemigos        | `enemy_event.wav`      | `enemy_die.wav`       |
| UI              | `ui_event.wav`         | `ui_click.wav`        |
| Ambiente        | `ambiente_zona.ogg`    | `forest_day.ogg`      |
| Música          | `zona_theme.ogg`       | `dungeon_theme.ogg`   |

✅ Usar snake_case, sin espacios ni mayúsculas.
✅ Formato preferido: `.wav` para efectos, `.ogg` para música.

---

## ⚙️ Futuras Funciones del Sistema de Audio

- `load_sound(path)` → Cargar efecto de sonido.
- `play_sound(name)` → Reproducir por nombre clave.
- `play_music(path, loop=True)` → Cargar música de fondo.
- `fade_out_music()` → Transición suave al cambiar música.

---

## 🛠️ Herramientas sugeridas para edición

- [Audacity](https://www.audacityteam.org/) – Edición básica y normalización.
- [Ocenaudio](https://www.ocenaudio.com/) – Alternativa liviana.
- [Wavosaur](https://www.wavosaur.com/) – Gratis y sin instalación.

---

## 📥 Fuentes de Sonidos Gratuitos y Legales

| Sitio / Fuente                        | Licencia                          | ¿Qué tiene?                          |
|--------------------------------------|-----------------------------------|--------------------------------------|
| [Kenney.nl](https://www.kenney.nl/assets)              | CC0 (dominio público)             | Paquetes completos de SFX y música   |
| [OpenGameArt.org](https://opengameart.org/)           | Varias (incluye CC0 y CC-BY)      | Sonidos, música, ambientaciones      |
| [Freesound.org](https://freesound.org/)               | CC0, CC-BY, CC-BY-NC (¡ojo!)       | Enorme base de sonidos reales y editados |
| [Itch.io SFX packs](https://itch.io/game-assets/tag-sound-effects) | Depende del autor (usualmente gratis para uso libre) | Efectos variados por género o estilo |
| [Zapsplat](https://www.zapsplat.com/)                 | Gratuita (requiere atribución)    | Efectos realistas, interfaz, naturaleza |
| [Mixkit](https://mixkit.co/free-sound-effects/)       | Gratis, sin registro               | Sonidos UI, naturaleza, combate, etc. |

### 🎮 Paquetes recomendados:
- **Kenney – Game Audio Pack**: efectos base para combate, movimiento y UI.  
- **OpenGameArt – 8-bit SFX Pack**: estilo retro ideal para prototipos.  
- **Itch.io – Casual UI SFX Pack**: perfecto para menú e interfaz.

---

## ⚠️ Consejos importantes

- ✅ Preferir archivos con licencia **CC0** (no requieren atribución).
- ⚠️ Evitar licencias **CC-BY-NC** si se planea monetizar.
- 🔈 Probar el volumen: normalizar los sonidos para que no sobresalgan.

---

## 🧩 Consideraciones Futuras

- Implementación progresiva desde menús y HUD.
- Eventos que activen sonidos: daño recibido, botón pulsado, objeto recogido.
- Módulo de audio desacoplado del render (para modularidad).
- Sistema de `event_triggers` para audio contextual en futuras fases.

---

¿Querés que prepare una carpeta de ejemplo con sonidos iniciales y funciones `load_sound()` / `play_sound()` listas para conectar?

