# 🛠️ 28. Build Pipeline – Creación de Ejecutables y Distribución

Este documento detalla cómo convertir el proyecto **RogueLike** en ejecutables standalone para Windows (y opcionalmente otros sistemas), utilizando `PyInstaller`.

---

## 🎯 Objetivo

Permitir a cualquier usuario ejecutar el juego sin instalar Python ni dependencias externas, empaquetando todo en un único archivo ejecutable (`.exe`).

---

## 🧰 Herramientas Utilizadas

| Herramienta     | Propósito                                        |
|-----------------|--------------------------------------------------|
| `PyInstaller`   | Empaquetado del proyecto como `.exe`             |
| `build.spec`    | Configuración personalizada del ejecutable       |
| `zip / Inno Setup` | Distribución comprimida o como instalador      |

---

## ⚙️ Requisitos Previos

1. Python instalado (preferentemente versión 3.10 o superior)
2. PIP actualizado (`python -m pip install --upgrade pip`)
3. Instalar PyInstaller:
   ```bash
   pip install pyinstaller
   ```

---

## 📦 Estructura Recomendada del Proyecto

```
src.roguelike_project/
├── assets/                # Sprites, sonidos, mapas
├── core/                 # Lógica del juego
├── entities/             # Jugador, NPCs, obstáculos
├── map/                  # Mapas y sistemas de generación
├── network/              # Cliente WebSocket
├── ui/                   # Menús, HUD
├── main.py               # Punto de entrada
├── config.py             # Configuraciones globales
├── requirements.txt      # Dependencias
└── build.spec            # (opcional) Archivo de build personalizado
```

---

## 🔨 Comando Básico de Build

Desde la raíz del proyecto:

```bash
pyinstaller --onefile --noconsole main.py
```

Esto generará:

```
dist/
└── main.exe     # Ejecutable final
```

### Opciones útiles:

| Opción            | Descripción                                  |
|-------------------|----------------------------------------------|
| `--noconsole`     | Oculta consola negra (solo interfaz gráfica) |
| `--onefile`       | Crea un solo `.exe`                          |
| `--add-data`      | Incluye carpetas (ver más abajo)             |
| `--icon=icon.ico` | Ícono personalizado                          |

---

## 🧳 Incluir Archivos de Recursos (assets)

Al empaquetar, debés incluir los recursos con:

```bash
pyinstaller main.py --onefile --noconsole \
  --add-data "assets;assets"
```

Formato para `--add-data`:

- En **Windows**: `"origen;destino"`
- En **Linux/macOS**: `"origen:destino"`

Ejemplo completo:

```bash
pyinstaller main.py --noconsole --onefile \
  --add-data "assets;assets" \
  --add-data "map;map"
```

---

## 🛠️ Personalizar con `build.spec` (opcional)

Generá automáticamente el archivo:

```bash
pyi-makespec main.py --onefile --noconsole --add-data "assets;assets"
```

Podés editarlo y luego compilar con:

```bash
pyinstaller build.spec
```

Esto permite:

- Cambiar nombre del ejecutable
- Incluir múltiples carpetas
- Establecer rutas relativas

---

## 💡 Consejos Profesionales

- Verificá siempre que `load_image` y otras rutas usen rutas absolutas basadas en `__file__`.
- Probá el `.exe` en una carpeta diferente a la del código (para evitar referencias implícitas).
- Usá un script para copiar `.exe` + assets en una carpeta de distribución si no usás `--onefile`.

---

## 📤 Distribución Final

Opciones:

1. **ZIP Manual**  
   Empaquetá:
   ```
   dist/
   ├── rogue.exe
   ├── assets/
   └── README.txt
   ```

2. **Inno Setup (Windows Installer)**  
   Permite crear un instalador `.exe` con ícono, carpeta de instalación, etc.

---

## 📦 Release Profesional en GitHub

1. Usá `gh release` para crear una versión.
2. Subí el `.exe` o el `.zip`.
3. Agregá changelog en formato Markdown.

---

## 🔄 Automatización (futuro)

Podés automatizar builds con un script `build_game.py` o un workflow de GitHub Actions.

---