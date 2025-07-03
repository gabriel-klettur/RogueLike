# Guía de desarrollo: Especificación de Ítems

Este documento detalla los metadatos y atributos de cada ítem disponible en el juego.

## Ítems iniciales

1. **Orbe de Experiencia**
   ```json
   {
     "id": "experience_orb",
     "name": "Orbe de Experiencia",
     "icon": "assets/items/exp_orb.png",
     "description": "Objeto que otorga puntos de experiencia al recogerse.",
     "stackable": false
   }
   ```

2. **Oro (Gold)**
   ```json
   {
     "id": "gold",
     "name": "Oro",
     "icon": "assets/items/gold_coin.png",
     "description": "Monedas de oro para comprar y comerciar.",
     "stackable": true,
     "max_stack": 999
   }
   ```

3. **Madera (Wood)**
   ```json
   {
     "id": "wood",
     "name": "Madera",
     "icon": "assets/items/wood.png",
     "description": "Recurso básico de madera.",
     "stackable": true,
     "max_stack": 99
   }
   ```

---

> Para más ítems, editar `data/items.json` y actualizar esta guía en consecuencia.
