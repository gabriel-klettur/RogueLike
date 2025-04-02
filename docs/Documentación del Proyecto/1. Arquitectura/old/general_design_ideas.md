# Diseño General - Proyecto RogueLike

Este documento describe los principales sistemas, mecánicas y componentes estructurales que integrarán el videojuego **RogueLike Top-down**.

---

## 1. Modo Local

Descripción del comportamiento del juego en modo sin conexión.

- Todo el procesamiento ocurre en el cliente.
- Las entidades y eventos se gestionan de forma autónoma.
- Ideal para pruebas y para una experiencia single-player completa.

## 2. Modo Multijugador

- Soporte para partidas multijugador en red local o internet.
- Un jugador actúa como **host** (servidor temporal).
- Comunicación mediante WebSockets u otro protocolo (a definir).
- Sincronización de entidades, inputs y estados.

## 3. Jugador

- Controlado por el usuario.
- Posee estadísticas (vida, maná, energía, etc.).
- Puede moverse, atacar, interactuar.
- Puede cambiar de personaje (clases).
- Progresión individual (habilidades, nivel, equipo).

## 4. NPCs

- Distintos tipos:
  - Monstruos (enemigos)
  - Vendedores
  - Dadores de misiones (quest givers)
  - Aliados (por ejemplo, en el pueblo)
- Poseen colisiones, lógica de IA, estadísticas.
- Pueden interactuar y reaccionar al jugador.

## 5. Mapa

- Generación procedural + secciones hechas a mano.
- Compuesto por tiles sólidos y no sólidos.
- Carga de mapas a partir de texto.
- Compatibilidad con minimapa y cámara dinámica.

## 6. Sistema del Juego (núcleo)

- Toda la información persistente del juego será almacenada de la siguiente manera:

  - En **modo local**, se usará **SQLite** en el cliente.
  - En **modo multijugador**, se usará **MySQL** en el servidor para gestionar y sincronizar los datos persistentes de los jugadores y el mundo.

- El juego funcionará únicamente en **modo diurno**: no habrá transición a noche ni efectos de iluminación nocturna. En un futuro implementaremos modo 'nocturno'.

- Incluye consola del cliente con info técnica y de personaje.

- Menú interactivo (pausa, opciones, multijugador).

- Reloj del juego, estado global (GameState).

- Control de eventos y ciclo de vida del juego.

## 7. Sistema PvP

- Este sistema será discutido en el futuro, ya que depende del desarrollo del modo multijugador online.
- No será prioridad para el producto mínimo viable (MVP).
- La posibilidad de daño entre jugadores, sincronización y configuración del modo PvP será analizada más adelante.

## 8. Sistema PvE

- Enemigos con IA básica o avanzada.
- Patrullaje, detección, ataque.
- Sistema de loot tras vencer enemigos.

## 9. Progresión del Personaje

- Ganancia de experiencia.
- Mejora de estadísticas.
- Desbloqueo de habilidades.
- Sistema de niveles.
- Sistema de Skills mixto:
  - Algunas skills subirán por uso (ej: combate, minería, sigilo).
  - Otras por distribución de puntos al subir de nivel.
  - Algunas se incrementarán al matar enemigos, otras al vender objetos o completar tareas específicas.
  - Algunas skills tendrán progreso por niveles (1-10, etc.), otras por porcentaje (0%-100%).
  - Se podrían desbloquear estilos de juego según las skills más desarrolladas, aunque esto será evaluado más adelante.

## 10. Progresión de la Ciudad

- Pueblo o base del jugador.
- Puede mejorar con el tiempo o tras eventos.
- Nuevas tiendas, NPCs, estructuras.
- Afecta jugabilidad y narrativa.
- Se implementará un sistema de mejoras inspirado en **Heroes of Hammerwatch 2**, donde:
  - El jugador puede invertir recursos para mejorar edificios del pueblo.
  - Las mejoras desbloquean nuevas funcionalidades (más objetos en tiendas, acceso a nuevas clases, herrerías mejoradas, etc.).
  - Las mejoras pueden ser compartidas por todos los personajes de una misma cuenta o mundo.
  - Algunas mejoras serán visuales (apariencia del pueblo) y otras funcionales (acceso a nuevas mecánicas de juego).
  - El sistema será persistente y escalará con el progreso del jugador o grupo.

## 11. Árboles de Habilidades

- Sistema visual y funcional de habilidades pasivas/activas.
- Requiere puntos para desbloqueo.
- Personalización por clase.

## 12. Clases de Personaje

- Diferentes estilos de juego:
  - Guerrero
  - Mago
  - Arquero, etc.
- Cada clase tiene habilidades y estadísticas únicas.

## 13. Tipos de Ataques

- Cuerpo a cuerpo, a distancia, mágicos.
- AoE (área), proyectiles, ataques dirigidos.
- Posibilidad de combinaciones (híbridos).

## 14. Sistema de Apuntado

- Orientación con el mouse o stick.
- Permite interacciones en 360°.
- Visualización con flecha o cursor.

---

Este documento se irá completando y expandiendo con los detalles específicos de cada sección a medida que avancemos en el desarrollo.

