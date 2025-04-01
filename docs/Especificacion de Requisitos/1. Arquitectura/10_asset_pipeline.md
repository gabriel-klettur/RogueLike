
# 🧩 10. Asset Pipeline – Convenciones y Organización de Recursos

Este documento define cómo organizar, nombrar, cargar y extender los **recursos visuales y de sonido** del proyecto.

Incluye sprites de personajes, tiles, objetos, efectos, íconos de HUD, sonidos y música.

---

## 🎯 Objetivo

- Mantener consistencia visual y de estructura.
- Asegurar compatibilidad entre assets existentes y futuros.
- Evitar duplicación, errores de carga o rutas incorrectas.
- Facilitar integración de nuevos artistas o contribuidores.

---

## 📁 Organización de Carpetas

```bash
assets/
├── characters/
├── tiles/
├── effects/
├── ui/
├── sounds/
└── mods/            # (Opcional, para contenido personalizado o experimental)
```

Cada carpeta contiene recursos de su tipo organizados en subcarpetas por tema o entidad.

---

## 🧠 Convenciones de Nombres

| Recurso         | Formato de nombre                       | Ejemplo                        |
|----------------|------------------------------------------|--------------------------------|
| Sprites de PJ   | `nombre_direccion.png`                  | `first_hero_up.png`            |
| Tiles           | `tipo_variación.png`                    | `stone_wall_a1.png`            |
| Animaciones     | `animacion/frame_##.png`                | `explosion/frame_01.png`       |
| Efectos         | `nombre_efecto.png`                     | `heal_glow.png`                |
| UI / HUD        | `tipo_elemento.png`                     | `cooldown_icon.png`            |
| Sonidos SFX     | `evento_tipo.wav`                       | `player_hit.wav`               |
| Música          | `ambiente_zona.ogg`                     | `overworld_theme.ogg`          |

✅ Siempre usar **snake_case**, sin espacios ni mayúsculas.  
✅ Usar nombres únicos por carpeta.  
❌ Evitar nombres ambiguos, duplicados o temporales (`_copy`, `_v2`, etc.).

---

## 🖼️ Tamaño y Formato de Sprites

- ✅ Tamaño base recomendado: **128x128 px**
- ✅ Todos los sprites deben tener **dimensiones consistentes** dentro de su categoría.
- ✅ Formato obligatorio: `.png` con **transparencia (canal alpha)**.
- 📦 Animaciones deben ir en subcarpetas, 1 imagen por frame (`frame_01.png`, etc).

---

## 🔊 Formato de Sonido

- SFX: `.wav` o `.ogg`, mono, 44.1 kHz.
- Música: `.ogg` preferentemente.
- Todos los audios deben estar normalizados en volumen.

---

## ⚙️ Carga de Assets en Código

Utilizamos un cargador personalizado ubicado en `utils/loader.py`:

```python
import pygame
import os

def load_image(path, scale=None):
    # ✅ Ruta base absoluta desde el archivo actual
    base_path = os.path.dirname(os.path.abspath(__file__))
    full_path = os.path.join(base_path, "..", path)

    image = pygame.image.load(full_path).convert_alpha()
    if scale:
        image = pygame.transform.scale(image, scale)
    return image
```

### ✔️ Ejemplo de uso:

```python
sprite = load_image("assets/characters/first_hero/first_hero_down.png", scale=(128, 128))
```

- ✅ Las rutas son **relativas al proyecto**, no absolutas.
- ✅ Si se cambia el tamaño, se realiza en el mismo `load_image()`.

---

## 📌 Consideraciones para expansión

- Posibilidad de usar un `assets_manifest.json` para cargar grupos de sprites o animaciones por lote.
- Carpeta `assets/mods/` para assets personalizados o descargables.
- Validación automática de sprites rotos puede integrarse desde menú de depuración (`DEBUG = True`).

---

## 🛠️ Añadir nuevos sprites o sonidos

1. Colocar el archivo en la carpeta correspondiente dentro de `assets/`.
2. Nombrar con convención adecuada.
3. Verificar tamaño y transparencia (si es sprite).
4. Cargar usando `load_image()` o equivalente.
5. Confirmar en el juego que se renderiza correctamente.

