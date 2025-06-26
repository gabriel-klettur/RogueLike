# 📋 Especificación de Requisitos – Proyecto RogueLike

## 🎮 1. Contexto del Proyecto

Estamos desarrollando un videojuego **Roguelike 2D con vista Top-Down**, usando **Python + Pygame**, con enfoque modular, profesional y multijugador. El proyecto se estructura con enfoque ágil y sin fechas límite.

Incluye actualmente:
- Movimiento de jugador y colisiones
- Generación de mapas y fusión con zonas hechas a mano
- Cámara dinámica y zoom
- Menú e interfaz básica
- Cliente WebSocket (modo multijugador)
- Sistema básico de minimapa y personajes seleccionables

---

## ✅ 2. Requisitos Generales

- Código limpio, comentado y modular
- Diseño orientado a funcionalidades jugables
- Desarrollo colaborativo en GitHub
- Uso de tablero Kanban libre
- Documentación técnica y de arquitectura

---

## 🧩 3. Requisitos Funcionales (MVP)

### 3.1 Movimiento y colisiones
- Movimiento fluido 360 grados y con sprites en 4 direcciones
- Colisión con tiles sólidos y obstáculos
- Animaciones por dirección

### 3.2 Interfaz y cámara
- HUD con barras de vida/energía
- Zoom dinámico (rueda de mouse)
- Minimapa activo

### 3.3 Menú interactivo
- Accesible con `ESC`
- Permite cambiar de personaje, modo y salir del juego

### 3.4 Multijugador básico (cliente)
- Sincronización de jugadores remotos
- Identificación de entidades remotas

### 3.5 Mapas
- Mapa procedural + zonas hechas a mano
- Sistema de tiles (`#` sólidos, `.` transitables)

### 3.6 Personajes
- Stats básicos (vida, maná, energía)
- Sprites separados por dirección
- Cooldown de restauración (`Q`)

---

## ⛔ 4. Requisitos No Funcionales

- 60 FPS en equipos gama media
- Uso de sprites normalizados (128x128 px) para tiles
- Uso de sprites normalizados (96x128 px) para personajes
- Fácil mantenimiento de assets

---

## 🔮 5. Futuras Extensiones para Alcanzar el MVP

Estas funcionalidades están **planeadas pero aún no implementadas**. Son necesarias para cerrar el MVP completo:

- [ ] Sistema de colisiones con NPCs
- [ ] Sistema básico de inventario
- [ ] Pantalla de inicio (Start Menu)
- [ ] Pantalla de Game Over / Respawn
- [ ] Mejoras de sincronización multijugador (modo host-local)
- [ ] Guardado de progreso local (modo SQLite)
- [ ] Selección de mapa o generación aleatoria desde menú
- [ ] IA básica: detección y persecución enemiga

---

## 🚀 6. Futuras Extensiones (NO MVP)

Estas funciones no son obligatorias para el MVP, pero están previstas a largo plazo:

- [ ] Árbol de habilidades por clase
- [ ] Sistema PvP con reglas opcionales
- [ ] Editor de mapas desde UI
- [ ] Eventos globales: bosses, clima, invasiones
- [ ] Progresión del pueblo con edificios mejorables
- [ ] Sistema completo de quests e historia
- [ ] Sistema de crafting / alquimia
- [ ] Persistencia multijugador en servidor remoto
- [ ] Rankings, retos y tablas globales

