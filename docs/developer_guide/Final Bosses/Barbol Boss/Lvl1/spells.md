# Spells para Final Boss Barbol

## Asset utilizado para asset del boss lvl 1.

Asset utilizado: D:\Python\RogueLike\assets\npc\monsters\barbol boss\final_boss_barbol_lvl1_down.png

Por su aspecto —una entidad vegetal masiva, de raíces y ramas antiguas, probablemente con poder natural o mágico ligado a la tierra— podemos definir una serie de **hechizos temáticos y visualmente coherentes** con su diseño y el sistema de hechizos descrito en tu `README_SPELLS.md`.

Aquí tienes **10 hechizos diseñados específicamente para este boss**, con nombres, efectos y posibles integraciones en el runtime/FSM del juego:

---

### 🌿 **1. Látigos de Raíz (root_whip)**

* **Tipo:** Ataque físico/terrestre.
* **Descripción:** Golpea con raíces emergentes del suelo en un radio circular.
* **Efecto secundario:** Empuja al jugador y lo ralentiza brevemente.
* **Visual (particle_preview):** Lianas marrón-verdosas que salen del suelo, efecto de polvo.
* **Uso FSM:** Acción `attack` → transición a `idle` tras cooldown.

---

### 🌫️ **2. Polvo de Esporas (spore_burst)**

* **Tipo:** Área/veneno.
* **Descripción:** Libera una nube de esporas que reduce la visibilidad y causa daño por veneno por 5s.
* **Visual:** Partículas verdes flotantes semitranslúcidas (`effect.density=0.4`).
* **FSM:** Acción “on_exit attack” o “on_hit” para cast automático cuando recibe daño.

---

### 🌪️ **3. Tormenta de Hojas (leaf_tempest)**

* **Tipo:** Área / viento.
* **Descripción:** Crea un vórtice de hojas cortantes que inflige daño por ráfagas múltiples.
* **Visual:** Sprite circular con movimiento rotacional, partículas verdes y amarillas.
* **Cooldown:** Alto, usado en fase 2.

---

### 🌱 **4. Regeneración Ancestral (ancient_regrowth)**

* **Tipo:** Curación pasiva.
* **Descripción:** Se planta en el suelo y canaliza energía natural para regenerar su salud.
* **Visual:** Aura verde con raíces resplandecientes que se retraen hacia el cuerpo.
* **FSM:** Estado “regrow” con acción “heal_over_time”.

---

### ⚡ **5. Ira del Bosque (forest_wrath)**

* **Tipo:** Ultimate / mágico.
* **Descripción:** Invoca múltiples pilares de energía natural (rayos verdes) que caen alrededor del jugador.
* **Visual:** Partículas verticales con iluminación verde intensa.
* **FSM:** Transición desde “rage_phase” tras <30% HP.

---

### 🌾 **6. Llamado de los Brotes (seedling_spawn)**

* **Tipo:** Invocación.
* **Descripción:** Hace brotar pequeños “minions” (raíces móviles o esporas vivas) que atacan al jugador.
* **Visual:** Pequeñas explosiones de tierra y hojas.
* **Integración:** Spawn en ECS → `spawn_services.py`.

---

### 🌋 **7. Explosión de Savia (sap_explosion)**

* **Tipo:** Daño cercano (aoe melee).
* **Descripción:** Al recibir daño crítico, libera savia ardiente que quema y empuja a los enemigos.
* **Visual:** Chorro viscoso dorado/verde con burbujas y destellos.
* **FSM:** Acción “on_damage_taken”.

---

### 🌳 **8. Escudo de Corteza (bark_shield)**

* **Tipo:** Defensa temporal.
* **Descripción:** Endurece su piel, reduciendo el daño recibido un 60% durante 4s.
* **Visual:** Textura de corteza que cubre el sprite; sonido grave.
* **FSM:** Estado “defend” con `cooldown` controlado.

---

### 🔥 **9. Corazón Ígneo (heart_of_emberwood)**

* **Tipo:** Buff / Fase 2.
* **Descripción:** Su savia se inflama, cambiando ataques de tierra a fuego (raíces ardientes).
* **Visual:** Ramas rojizas, partículas de fuego mezcladas con hojas.
* **Integración:** Trigger de FSM → transición “phase_change”.

---

### 🌀 **10. Rugido del Bosque (forest_roar)**

* **Tipo:** Control / grito sónico.
* **Descripción:** Emite un rugido que empuja a todos los jugadores cercanos y cancela hechizos activos.
* **Visual:** Ondas concéntricas verdes.
* **FSM:** Acción de transición de “rage_phase” → “attack” en bucle.