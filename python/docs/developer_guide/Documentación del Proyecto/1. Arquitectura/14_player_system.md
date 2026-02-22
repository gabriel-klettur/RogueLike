# 🧍‍♂️ 14. Player System – Stats, Cooldowns e Interfaz Visual

Este documento detalla el sistema actual del jugador, su composición modular, estadísticas principales, interfaz visual, y plan para su expansión futura.

---

## 🎯 Objetivo

- Centralizar toda la lógica del jugador en módulos claros.
- Separar renderizado, movimiento y lógica de stats.
- Hacer que el sistema sea **modular, extensible y fácil de testear**.

---

## 🧱 1. Estructura Modular del Jugador

El jugador está compuesto por 3 módulos principales:

| Módulo             | Archivo                                        | Función Principal                     |
|--------------------|-----------------------------------------------|----------------------------------------|
| `PlayerStats`      | `entities/player/stats.py`                    | Salud, maná, energía, lógica de daño   |
| `PlayerMovement`   | `entities/player/movement.py`                | Movimiento basado en input            |
| `PlayerRenderer`   | `entities/player/renderer.py`                | Dibujo, HUD, barra flotante           |

Estos módulos son gestionados desde:

```python
entities/player/base.py
```

---

## ❤️ 2. Stats del Jugador (actuales)

- Salud (`health`) / Salud Máxima (`max_health`)
- Maná (`mana`) / Maná Máximo (`max_mana`)
- Energía (`energy`) / Energía Máxima (`max_energy`)

Estas estadísticas:
- Se regeneran/restauran con `Q`
- Se usan visualmente en HUD e interacciones
- Se pueden modificar fácilmente desde `PlayerStats`

---

## ⌛ 3. Sistema de Cooldowns

- Tecla `Q`: restauración total, con cooldown.
- Cooldowns futuros se manejarán desde `PlayerStats` o módulo `CooldownManager`.
- Cada habilidad tendrá su cooldown individual (en milisegundos o ticks).

---

## 🖼️ 4. Interfaz Visual (HUD)

### `Floating HUD` (sobre el personaje)
- Barras pequeñas: vida, maná, energía
- Visible tanto en jugador local como jugadores remotos

### `Main HUD` (parte inferior de la pantalla)
- Barras grandes y visibles
- Espacio para iconos de habilidades, hechizos, objetos

---

## 🔮 5. Extensiones Futuras del Sistema del Jugador

### 🎮 Sistema de Habilidades (Skills)
- Skills activas y pasivas
- Sistema de desbloqueo por nivel o puntos
- Barra de acceso rápido para usar skills (`1`, `2`, `E`, `R`, etc.)
- Categorización: ofensivas, defensivas, soporte

### 🧬 Sistema de Clases
- Clases base como:
  - Guerrero (melee, tanque)
  - Mago (rango, AoE)
  - Pícaro (veloz, sigilo)
- Cada clase define:
  - Stats iniciales
  - Skills disponibles
  - Afinidades

### 🌍 Sistema de Razas
- Razas posibles: Humanos, Elfos, Demonios, Orcos
- Bonus únicos por raza (ej. +maná, resistencia, velocidad)
- Afecta narrativa y relaciones con NPCs (futuro)

### 📈 Sistema de Niveles / Experiencia
- Ganancia de experiencia al derrotar enemigos o completar quests
- Sistema de niveles con curva de XP
- Aumento automático o con puntos asignables en:
  - Stats base (vida, fuerza, agilidad)
  - Habilidades

### 🛠️ Sistema de Profesiones
- Recolección: minería, pesca, tala
- Producción: herrería, alquimia, cocina
- Profesiones suben de nivel por uso
- Desbloquean recetas exclusivas, armas especiales, o buffs

### 💍 Sistema de Equipamiento
- Ranuras: arma, armadura, casco, botas, anillo, amuleto
- Estadísticas que escalan con rareza o nivel
- Posibilidad de encantamientos o mejoras

### 🪙 Sistema de Economía
- Monedas (oro, plata, cobre)
- Tiendas, inventario de vendedores
- Objetos con precios dinámicos
- Sistema de trueque (opcional)

### 🧱 Sistema de Inventario
- Inventario limitado (espacio, peso)
- Ordenamiento por categorías
- Interfaz para uso rápido, equipamiento, descarte

### 🧾 Sistema de Logros / Títulos
- Logros por combate, exploración, historia
- Títulos con efectos pasivos o estéticos

### 📚 Sistema de Lore / Historia
- Diálogos ramificados con NPCs
- Misiones de historia principales y secundarias
- Documentos interactivos en el mundo (libros, pergaminos)

### 📜 Sistema de Reputación y Facciones
- Reputación con pueblos, gremios, facciones
- Afecta comercio, misiones, enemigos aliados

---
