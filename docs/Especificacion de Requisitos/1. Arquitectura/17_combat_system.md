# ⚔️ 17. Combat System – PvE, PvP, Daño y Hitboxes

Este documento define el sistema de combate del juego, tanto para interacciones jugador-vs-entorno (PvE) como jugador-vs-jugador (PvP), incluyendo ataques, colisiones, y tipos de daño.

---

## 🎯 Objetivo

- Proveer un sistema de combate **intuitivo pero expandible**, desde mecánicas simples hasta escenarios avanzados con efectos especiales.
- Soportar **distintos estilos de ataque**: cuerpo a cuerpo, a distancia, mágico y mixto.
- Adaptarse tanto a enemigos IA como al PvP futuro.

---

## 💥 1. Tipos de Ataque

En el juego se contemplan múltiples estilos de ataque, cada uno con características específicas que afectan la velocidad, alcance, precisión y tipo de daño. Esta flexibilidad permitirá implementar diferentes clases y estilos de combate.

### 🥊 Cuerpo a Cuerpo (Melee)

Ataques que requieren proximidad al objetivo.

| Tipo              | Ejemplo                 | Velocidad | Daño     | Precisión | Notas                         |
|-------------------|--------------------------|-----------|----------|-----------|-------------------------------|
| Golpe rápido      | Puños, dagas             | Alta      | Bajo     | Alta      | Ideal para ataques rápidos.   |
| Arma media        | Espadas cortas           | Media     | Media    | Alta      | Equilibrio entre daño y cadencia. |
| Arma pesada       | Mazas, espadones         | Baja      | Alta     | Media     | Animación más lenta, pero gran impacto. |
| Ataque en área    | Espadas anchas, martillos| Baja      | Media/Alta | Media   | Golpea a múltiples enemigos alrededor. |

---

### 🏹 A Distancia (Ranged)

Permite atacar sin contacto directo. Puede requerir puntería o auto-apuntado.

| Tipo             | Ejemplo                    | Puntería | Daño     | Velocidad | Notas                          |
|------------------|-----------------------------|----------|----------|-----------|-------------------------------|
| Proyectil físico | Flechas, cuchillos          | Manual   | Medio    | Media     | Se ve afectado por el movimiento del objetivo. |
| Lanzamiento rápido | Shuriken, piedras         | Manual   | Bajo     | Alta      | Útil para debilitar o interrumpir.             |
| Proyectil especial | Boomerangs, trampas       | Mixto    | Variable | Lenta     | Efectos adicionales (stun, rebote).            |

---

### 🔮 Mágico (Magic)

Ataques basados en maná, pueden ser dirigidos, automáticos o en área.

| Tipo                  | Ejemplo                      | Puntería  | Daño    | Consumo de Maná | Notas                           |
|-----------------------|-------------------------------|-----------|---------|------------------|---------------------------------|
| Proyectil mágico      | Bola de fuego, rayo           | Manual    | Alto    | Medio             | Requiere puntería y cooldown.   |
| Hechizo auto-dirigido | Misil mágico, veneno guiado   | Automático| Medio   | Medio             | Ideal para principiantes.       |
| Hechizo AoE           | Explosión, tormenta, zona lenta | Zona     | Alta    | Alto              | Afecta múltiples enemigos.      |
| Hechizo de línea      | Rayo, corte mágico             | Dirección| Alta    | Medio             | Atraviesa varios enemigos.      |
| Magia de target       | Curar o dañar a objetivo fijo | Selección| Variable| Medio             | No necesita puntería manual.    |

#### 📚 Extensión del sistema mágico (Futuro)

##### Magias de Invocación
- Invocan criaturas con IA (ataque, defensa, utilidad).

##### Magias Elementales
- Fuego, hielo, rayo, tierra, viento, oscuridad, luz, etc.
- Efectos adicionales como quemaduras, ralentizaciones, etc.

##### Maldiciones (Debuffs)
- Reducción de stats, control invertido, daño progresivo, etc.

##### Encantos (Buffs)
- Mejora de defensa, ataque, velocidad, regeneración, etc.

---

### 🍷 Uso de Pociones (Futuro)

Las pociones deben integrarse al combate, con cooldowns o animaciones. Tipos comunes:

| Tipo de Poción       | Efecto                                               |
|----------------------|------------------------------------------------------|
| Vida instantánea     | Recupera HP al instante.                             |
| Regeneración         | Recupera HP lentamente durante unos segundos.        |
| Energía/Estamina     | Permite atacar más rápido o esquivar.                |
| Potenciadoras        | Aumentan daño o defensa por tiempo limitado.         |
| Antídoto             | Cura efectos negativos (veneno, ceguera, etc).       |
| Explosiva            | Poción arrojable, explota al contacto.               |

---

### 🌀 Híbridos y Combinados (Futuro)

| Tipo                 | Ejemplo                       | Notas                                                    |
|----------------------|-------------------------------|----------------------------------------------------------|
| Armas encantadas     | Espada con fuego o veneno     | Combina daño físico con mágico.                          |
| Combos físicos-mágicos| Golpe seguido de hechizo      | Requiere timing o combinación de botones.                |
| Daño contextual      | Por la espalda, en aire, etc. | Multiplicadores si se cumplen condiciones especiales.    |
| Daño acumulativo     | Quemadura, veneno             | Efectos que escalan con el tiempo.                       |

---

### 🪤 Uso de Ítems en Combate (Futuro)

Algunas clases podrán utilizar objetos estratégicos para alterar el entorno o influir en el desarrollo del combate. Este sistema amplía las opciones de juego y fomenta la creatividad táctica.

| Tipo de Ítem           | Ejemplo                      | Efecto                                                  |
|------------------------|------------------------------|---------------------------------------------------------|
| Trampas físicas        | Trampa de pinchos, red       | Inmoviliza o daña al enemigo al pasar por la zona.      |
| Explosivos             | Granada, barril volátil      | Daño en área, efecto de empuje o desorientación.        |
| Ítems mágicos de uso   | Tótem, pergamino, esfera     | Lanza hechizos o crea zonas mágicas temporales.         |
| Ítems defensivos       | Escudos temporales, barreras | Bloquean daño, alteran rutas enemigas.                  |
| Estimulantes           | Humo, cegadoras              | Modifican IA o comportamiento enemigo.                  |

Estos ítems pueden:

- Requerir fabricación o compra.
- Estar limitados en cantidad (carga).
- Tener animaciones o tiempo de preparación.
- Activarse con cooldown o condiciones.

---

