# 🏙️ 20. City Upgrade System – Progresión del Pueblo y Economía

Este documento describe el sistema de mejoras del pueblo/base del jugador, que actúa como centro de progresión compartida, gestión económica y expansión del mundo.

---

## 🎯 Objetivo

- Crear un sistema de **mejoras persistentes** que el jugador pueda construir e invertir.
- Desbloquear **nuevas funciones, tiendas, habilidades, zonas y ventajas** para todos los personajes.
- Fomentar la **exploración y economía compartida**.

---

## 🏘️ 1. Tipos de Edificaciones Mejorables

| Edificio               | Funcionalidad Inicial                          | Posibles mejoras                            |
|------------------------|-----------------------------------------------|---------------------------------------------|
| Taberna                | Punto de spawn, lugar para reclutar NPCs.     | Acceso a rumores, misiones de gremio.       |
| Herrería               | Compra de armas y reparación.                 | Forjado de objetos únicos, encantamientos.  |
| Tienda de pociones     | Venta básica.                                 | Recetas avanzadas, alquimia.                |
| Templo de la Vida      | Restauración de salud.                        | Buffs temporales, resurrección.             |
| Torre Arcana           | N/A inicial.                                  | Desbloquea magias y habilidades arcanas.    |
| Biblioteca             | N/A inicial.                                  | Historia, talentos, lore, conocimiento.     |
| Cámara de Comercio     | N/A inicial.                                  | Afecta la economía y comercio global.       |

---

## 🧩 2. Lógica Técnica del Sistema

- Cada mejora se almacena en una estructura de ciudad persistente:

```json
{
  "smith_level": 2,
  "potion_shop": true,
  "temple_upgraded": false
}
```

- Esta estructura puede almacenarse en:
  - `SQLite` (modo local).
  - `MySQL` (modo online).

- El jugador invierte recursos para desbloquear cada nivel:
  - Oro.
  - Materiales especiales (minerales, esencia mágica, etc.).
  - Reputación.

---

## 👥 3. Persistencia Compartida

- Las mejoras del pueblo afectan **a todos los personajes de una misma cuenta**.
- El progreso es persistente y se comparte entre partidas.
- Las mejoras pueden afectar la dificultad, economía o balance general del mundo.

---

## 💰 4. Economía Global y Balance Dinámico

- En el futuro, el juego podría incluir una economía con **moneda limitada globalmente**.
  - Los NPCs tendrán **fondos limitados**, no infinitos.
  - Los precios podrían **ajustarse dinámicamente** según:
    - Oferta y demanda local.
    - Cantidad de oro en circulación.
    - Eventos globales o progresión de los jugadores.

- Se requiere un sistema de contabilidad central (modo online) para esto.

---

## 🧪 5. Interfaz Visual Propuesta

- El jugador accede a una **UI de mejoras** desde el menú o un edificio específico.
- Se muestra el estado actual, lo necesario para mejorar, y las consecuencias.

```text
[ Herrería Nivel 1 ]
  ➤ Mejora a Nivel 2: 100 oro, 2 lingotes de hierro
  ➤ Beneficio: Acceso a armas mágicas básicas
```

---

## 📂 6. Organización del Código

| Carpeta / Módulo        | Propósito                                               |
|--------------------------|----------------------------------------------------------|
| `systems/city/`          | Lógica del pueblo, mejoras, inversión, estados.          |
| `data/city.json`         | Archivo con niveles y condiciones por mejora.            |
| `ui/menus/city_upgrade.py` | Interfaz de mejoras.                                  |
| `entities/npc/builders.py`| NPCs encargados de mejoras.                            |

---

## 💪 7. Mejoras que afectan al Personaje

Muchas mejoras en el pueblo otorgarán **bonificaciones permanentes o desbloqueos** al personaje, como:

- Aumento de stats básicos.
- Reducción de cooldowns.
- Nuevas habilidades pasivas.
- Acceso a evoluciones de clase/raza.
- Mejoras en árboles de talentos.

### 🛠️ Ejemplos de Edificios con Bonus Directo

| Edificio / Mejora            | Efecto sobre el jugador                                  |
|------------------------------|-----------------------------------------------------------|
| **Templo de Vitalidad**       | +20 vida máxima, regeneración más rápida.                |
| **Altar de Energía**          | Mayor energía máxima, recuperación acelerada.            |
| **Torre Arcana**              | Desbloquea hechizos, reduce coste de maná.              |
| **Dojo del Guerrero**         | +5% daño físico, desbloquea habilidades físicas.         |
| **Biblioteca Oculta**         | Acceso a talentos únicos y conocimientos secretos.       |
| **Santuario de Clases/Razas** | Permite evolución avanzada de personaje.                 |

- Estas bonificaciones se integran con los sistemas de `PlayerStats`, `SkillSystem`, y `CityState`.

---

## 🏗️ 8. Configuración Modular del Pueblo

Para fomentar la **personalización del entorno urbano** y aumentar la rejugabilidad, el sistema permitirá al jugador decidir cómo organizar sus edificios de mejora:

### 🧱 A. Pueblo Compacto (todo en uno)

- Todos los servicios y mejoras disponibles desde un único edificio central.
- Ventajas:
  - Mayor conveniencia y velocidad de acceso.
  - Ideal para jugadores que priorizan la eficiencia.
- Desventajas:
  - Algunas mejoras estarán limitadas a nivel básico/intermedio.
  - Menor variedad estética y de interacción con el entorno.

### 🏘️ B. Pueblo Expandido (varios edificios)

- Cada edificio tiene su función específica (ej: herrería, templo, torre arcana...).
- Ventajas:
  - Acceso a **mejoras avanzadas y especializadas**.
  - Pueblo visualmente más rico y diverso.
  - Incentiva el **recorrido, exploración y gestión del espacio**.
- Desventajas:
  - Requiere más desplazamiento.
  - Costes más elevados en recursos y tiempo.

### 🎨 C. Impacto Visual y Narrativo

- Las decisiones sobre la configuración del pueblo afectarán:
  - Su **apariencia física** en el mundo.
  - La **disponibilidad y potencia** de ciertas funciones.
  - Las **interacciones posibles con NPCs** (más edificios → más personajes y eventos).

---

## 👥 9. NPCs Esenciales en la Ciudad Personal

| Tipo de NPC               | Rol Principal                                              |
|---------------------------|------------------------------------------------------------|
| 🛠️ Constructor             | Permite construir o mejorar edificios de la ciudad.        |
| ⚖️ Comerciante              | Compra y vende objetos, gestionando recursos.              |
| 🔮 Hechicero / Sabio        | Desbloquea magias o habilidades especiales.                |
| 🧪 Alquimista               | Permite preparar pociones, brebajes o encantamientos.      |
| 🧙‍♂️ Mentor de clases       | Facilita cambios o progresión de clase/raza.               |
| 🧭 Dador de misiones        | Proporciona misiones personales (PvE, exploración, etc.).  |
| 🎭 NPCs sociales            | Añaden vida al entorno: aldeanos, niños, bardos, etc.      |
| 🐾 Mascota / Compañero      | Entidad única del jugador que puede evolucionar.           |
| 🎨 Decorador                | Permite personalizar estéticamente edificios y entornos.  |

Cada NPC puede tener una progresión propia, afinidad con el jugador, y misiones ramificadas.


---

## 🌐 10. Integración con el Modo Multijugador: Casas Propias y Ciudades Locales

Para ofrecer una experiencia inmersiva en línea y reforzar el sentido de propiedad:

### 🏠 A. Propiedades de Jugador (Modo Online)

- Cada jugador puede adquirir una **casa personal** dentro de la ciudad compartida.
- Estas casas serán decorables, funcionales y visibles por otros jugadores.
- Funcionan como puntos de encuentro, almacenamiento, y base privada.

### 🌀 B. Portal a Ciudad Personal (Modo Local / Host)

- En cada casa existe un **portal especial**.
- El portal puede:
  - Llevar al jugador a su **ciudad local** (modo singleplayer).
  - Activar al jugador como **host** y permitir que otros visiten su ciudad.

### 🔄 C. Progreso Sincronizado

- Las mejoras aplicadas en ciudades locales pueden sincronizarse con el perfil global del jugador.
- Permite progreso paralelo, sin depender de conexión constante.

### 🌟 D. Recompensa y Variedad

- Incentiva la inversión en la ciudad local.
- Ofrece rutas de desarrollo únicas y personalizadas.
- Permite la existencia de pueblos variados entre jugadores.

---

## 🔮 11. Extensiones Futuras

| Mejora                      | Posible efecto                                             |
|-----------------------------|-------------------------------------------------------------|
| Mejoras visuales            | Cambios en apariencia del pueblo, decoración.              |
| Invasiones o eventos        | Ciudades pueden ser atacadas o defendidas por el jugador.  |
| Gremios personalizados      | Crear facciones propias dentro del pueblo.                 |
| Expansión física del mapa   | Construir nuevas zonas adyacentes.                         |
| Proyectos comunitarios      | En modo multijugador, varios jugadores cooperan para mejorar. |

---

