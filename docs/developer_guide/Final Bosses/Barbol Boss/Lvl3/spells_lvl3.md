# Spells para Final Boss Barbol

## Asset utilizado para asset del boss lvl 3.

Asset utilizado: D:\Python\RogueLike\assets\npc\monsters\barbol boss\final_boss_barbol_lvl3_down.png


Esta tercera forma claramente representa la **fase final del boss**, una fusión entre **fuego demoníaco y corrupción arbórea** —una entidad ancestral que ha sido consumida por energía infernal.
Su corazón ígneo y el cráneo incrustado en el tronco sugieren un combate apocalíptico: lento, devastador y cargado de ataques de área, fuego y corrupción combinada.

Aquí tienes **10 hechizos diseñados específicamente para esta forma final (“Barböl Ígneo” o “Elderroot Infernal”)**, pensados para su uso dentro del FSM del boss y compatibles con el sistema de partículas y JSON de hechizos del editor (`spells_editor_controller.py` + `particle_preview.py`):

---

### 🔥 **1. Núcleo Ardiente (burning_core)**

* **Tipo:** Buff / Fase 3.
* **Descripción:** Activa el corazón ígneo, aumentando todo el daño un 50% y generando calor continuo que daña al jugador cercano.
* **Visual:** Aura roja anaranjada pulsante desde el pecho, partículas incandescentes flotando.
* **FSM:** Estado `rage_phase`.

---

### 🌋 **2. Erupción Ígnea (infernal_eruption)**

* **Tipo:** AoE / Fuego.
* **Descripción:** Libera una serie de explosiones de magma alrededor del boss en anillos concéntricos.
* **Visual:** Efectos de fuego y roca fundida ascendiendo del suelo.
* **FSM:** `attack` → transición “idle” tras 3 erupciones.

---

### ⚡ **3. Golpe del Abismo (abyss_slam)**

* **Tipo:** Físico / Magma.
* **Descripción:** Golpea el suelo con su brazo ardiente provocando una onda expansiva.
* **Visual:** Estallido rojo-anaranjado con grietas luminosas en el suelo.
* **Efecto secundario:** Derribo y quemadura.

---

### 🌑 **4. Llamas de la Corrupción (corrupted_flames)**

* **Tipo:** Fuego + Veneno (mixto).
* **Descripción:** Las llamas del boss dejan residuos tóxicos que siguen dañando al jugador.
* **Visual:** Fuego verde-anaranjado, humo oscuro.
* **FSM:** Acción `update_action` con daño por tiempo.

---

### 💀 **5. Pacto de Ceniza (ash_pact)**

* **Tipo:** Pasivo / Reanimación.
* **Descripción:** Al morir, su cuerpo arde y genera dos “Espíritus de Fuego Fúngico” que continúan atacando.
* **Visual:** Doble explosión de fuego y humo púrpura.
* **FSM:** Acción `on_death`.

---

### 🌪️ **6. Tormenta Infernal (hellstorm)**

* **Tipo:** Ultimate / Fuego.
* **Descripción:** Convoca una lluvia de fuego que cubre toda la arena, alternando zonas seguras.
* **Visual:** Meteoroides pequeños descendiendo; partículas rojas, amarillas y negras.
* **Uso:** Activado en <30% HP.

---

### 🕳️ **7. Llama Devoraalmas (soulflame)**

* **Tipo:** Hechizo canalizado.
* **Descripción:** Dispara un rayo continuo de energía ígnea desde el pecho, drenando vida al jugador.
* **Visual:** Rayo con núcleo naranja y borde violeta; partículas ascendentes.
* **FSM:** Estado `channel_beam`.

---

### 🌲 **8. Último Aliento del Bosque (forest_last_breath)**

* **Tipo:** Curación + Daño radial.
* **Descripción:** Absorbe energía del entorno curándose mientras libera un estallido de fuego y hojas marchitas.
* **Visual:** Mezcla de partículas verdes y rojas, forma esférica.
* **FSM:** `heal_over_time` → transición “attack”.

---

### 💫 **9. Corazón del Cataclismo (cataclysm_heart)**

* **Tipo:** Ultimate final.
* **Descripción:** El corazón arde al máximo, y tras 10 segundos explota en una onda expansiva que destruye todo alrededor.
* **Visual:** Núcleo rojo-blanco brillante con expansión radial de fuego y ceniza.
* **FSM:** Estado “death_explosion”.

---

### 🔥 **10. Maldición de las Raíces Negras (blackroot_curse)**

* **Tipo:** Maleficio / Área.
* **Descripción:** Raíces negras se prenden fuego y persiguen al jugador, estallando al contacto.
* **Visual:** Raíces incandescentes que avanzan ondulando.
* **FSM:** Acción periódica “summon_projectile”.

---