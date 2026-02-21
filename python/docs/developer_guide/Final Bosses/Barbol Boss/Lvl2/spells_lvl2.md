# Spells para Final Boss Barbol

## Asset utilizado para asset del boss lvl 2.

Asset utilizado: D:\Python\RogueLike\assets\npc\monsters\barbol boss\final_boss_barbol_lvl2_down.png


Esta nueva versión del boss tiene un aire **más corrupto y oscuro**, con energía verde brillante y un tono fúngico (ese hongo púrpura en el hombro es una pista visual muy potente). Esto sugiere una evolución o **fase 2** del mismo “Ent” —más agresiva, mágica y venenosa.

A continuación te propongo **10 hechizos únicos** alineados con esta estética —más tóxica, corrupta y arcana— para tu boss final “Barböl Corrupto” o “Elderroot Corrupto”.

---

### 💀 **1. Descarga Fúngica (fungal_burst)**

* **Tipo:** Veneno / AoE.
* **Descripción:** Expulsa esporas tóxicas en todas direcciones que explotan tras 2 s liberando gas venenoso.
* **Visual:** Neblina púrpura con partículas esféricas verdes y violetas.
* **FSM:** Acción `attack_area` con transición a “idle” después de explosión.

---

### 🌌 **2. Raíces Abisales (abyssal_roots)**

* **Tipo:** Control / Invocación.
* **Descripción:** Raíces negras emergen del suelo y atrapan al jugador, infligiendo daño de corrupción.
* **Visual:** Tentáculos de sombra con resplandor verde central.
* **Extra:** Usa `effect.radius=3.5`, animación de estiramiento vertical.

---

### 🧠 **3. Eco de la Corrupción (corruption_echo)**

* **Tipo:** Maleficio / Debuff.
* **Descripción:** Infecta al jugador con una enfermedad que replica cada hechizo que lanza (daño reflejado).
* **Visual:** Aura verde pulsante que se adhiere al objetivo.
* **FSM:** Acción de “curse_on_hit”.

---

### 🔮 **4. Núcleo Venenoso (venom_core)**

* **Tipo:** Canalización / Buff.
* **Descripción:** El corazón del boss se ilumina y durante 8 s todos los ataques infligen veneno adicional.
* **Visual:** Luz interior verde pulsante, partículas de savia tóxica ascendentes.
* **FSM:** Estado `venom_phase`.

---

### 🦠 **5. Lluvia de Esporas (spore_rain)**

* **Tipo:** Área / Ambiental.
* **Descripción:** Oscurece el cielo y llueven esporas que dejan charcos corrosivos en el suelo.
* **Visual:** Partículas descendentes con niebla y salpicaduras verdes.
* **Uso:** “phase transition” a mitad de combate.

---

### 🌳 **6. Marcha de la Corrupción (corrupt_growth)**

* **Tipo:** Invocación / Terreno.
* **Descripción:** Convierte zonas del suelo en raíces vivas que atacan al jugador si permanece encima.
* **Visual:** Animación de raíces emergentes con brillo.
* **FSM:** Acción periódica (`update_action`).

---

### 🧪 **7. Hongo Parásito (parasitic_mushroom)**

* **Tipo:** Summon / Control mental.
* **Descripción:** Planta un hongo en enemigos muertos, que revive como un minion fúngico.
* **Visual:** Mini zombis vegetales con aura púrpura.
* **Integración ECS:** Spawner → “spawn_services.spawn_entity(‘fungal_minion’)”.

---

### 🌋 **8. Erupción de Savia (sap_erupt)**

* **Tipo:** Defensa reactiva.
* **Descripción:** Al recibir daño crítico, expele savia ácida que inflige daño en cono frontal.
* **Visual:** Goteo viscoso verde-neón.
* **FSM:** Acción “on_damage_taken”.

---

### 🌑 **9. Pulso del Abismo (abyss_pulse)**

* **Tipo:** Ultimate.
* **Descripción:** Lanza un pulso oscuro que recorre toda la arena del combate, dañando y empujando.
* **Visual:** Ondas concéntricas negras con bordes verdes.
* **FSM:** “rage_phase → pulse_attack”.

---

### 🔥 **10. Corazón Putrefacto (rotten_heart)**

* **Tipo:** Fase final / Pasivo.
* **Descripción:** Cuando su vida cae por debajo del 15 %, libera continuamente gases venenosos y se autodestruye al morir, causando una gran explosión de corrupción.
* **Visual:** Emanación verde intensa seguida de explosión púrpura.
* **FSM:** “death_trigger → corruption_explosion”.

---