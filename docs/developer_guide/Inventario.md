# Guía de desarrollo: Sistema de Inventario

## 1. Introducción
Este documento describe el diseño e implementación del sistema de inventario para el jugador. Incluye:
- Definición de ítems (monedas, experiencia, madera).
- Configuración de caídas (drops) al morir NPCs.
- Componente de inventario en el jugador.
- Interfaz de usuario (UI) y manejo de entrada.

## 2. Datos y formato JSON

### 2.1 `items.json`
Definir todos los ítems en `data/items.json`:
```json
[
  {
    "id": "coins",
    "name": "Monedas",
    "icon": "assets/items/coin.png",
    "description": "Monedas de oro",
    "stackable": true,
    "max_stack": 999
  },
  {
    "id": "experience",
    "name": "Experiencia",
    "icon": "assets/items/exp.png",
    "description": "Puntos de experiencia",
    "stackable": false
  },
  {
    "id": "wood",
    "name": "Madera",
    "icon": "assets/items/wood.png",
    "description": "Recurso de madera",
    "stackable": true,
    "max_stack": 99
  }
]
```

### 2.2 `monsters.json`
Mantener caídas en `data/monsters.json`:
```json
"barbol": {
  ...,  
  "drops": [
    { "item": "coins",      "min": 1,  "max": 5,  "chance": 0.8 },
    { "item": "experience", "min": 5,  "max": 15, "chance": 1.0 },
    { "item": "wood",       "min": 0,  "max": 3,  "chance": 0.5 }
  ]
}
```

### 2.3 `inventory_player.json`
Estructura de inventario del jugador en `data/inventory_player.json`:
```json
{
  "capacity": 20,
  "slots": [
    { "item": "coins",      "quantity": 10 },
    null,
    { "item": "wood",       "quantity": 5 },
    ...
  ]
}
```
- `capacity`: número total de ranuras.
- `slots`: lista de ranuras (objeto o `null`).
- Cada ranura define `item` y `quantity`.

## 3. Modelo de Ítems
Definir una clase base `Item` con atributos comunes:
```python
class Item:
    def __init__(self, id: str, icon_path: str, stackable: bool = True):
        self.id = id
        self.icon_path = icon_path
        self.stackable = stackable
```
Subclases o componentes ECS:
- `CoinItem`, `ExperienceItem`, `WoodItem`.

## 4. Sistema de Caída (Drops)
Al morir un NPC, procesar su plantilla JSON y generar `DroppedItem` en el suelo:
```python
import random

def on_npc_death(npc):
    template = npc.template  # datos cargados desde monsters.json
    for drop in template["drops"]:
        if random.random() <= drop["chance"]:
            qty = random.randint(drop["min"], drop["max"])
            if qty > 0:
                spawn_dropped_item(drop["item"], qty, position=npc.position)
```

## 5. Componente Inventory en el jugador
```python
from typing import List, Optional

class ItemStack:
    def __init__(self, item_id: str, quantity: int):
        self.item_id = item_id
        self.quantity = quantity

class InventoryComponent:
    def __init__(self, capacity: int = 20):
        # Lista de ItemStack o None para ranuras vacías
        self.slots: List[Optional[ItemStack]] = [None] * capacity

    def add(self, item_id: str, qty: int) -> bool:
        # Lógica para apilar o usar ranura vacía
        ...

    def remove(self, item_id: str, qty: int) -> bool:
        ...

    def has(self, item_id: str, qty: int) -> bool:
        ...
```

## 6. Diseño de la UI
- **Ventana modal** sobre el juego.
- **Grid de ranuras** (por ejemplo, 5×4).
- Cada ranura muestra icono y cantidad.
- **Drag & Drop** entre ranuras y al suelo.
- **Tooltip** con nombre y descripción al pasar el ratón.

## 7. Manejo de entrada
- Mapear tecla `I` para alternar la ventana de inventario:
```python
if input.is_key_pressed("I"):
    inventory_window.toggle()
```
- Pausar la lógica de movimiento y combate mientras la UI esté abierta.

## 8. Ejemplos de archivo JSON y código
Ver secciones anteriores para fragmentos completos y adaptarlos al proyecto.

## 9. Extensiones futuras
- **Orden y filtros** automáticos.
- **Drop desde el suelo** hacia el inventario.
- **Sistema de crafting**, comerciantes y equipamiento.
