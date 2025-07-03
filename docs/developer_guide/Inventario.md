# Guía de desarrollo: Sistema de Inventario

## 1. Introducción
Este documento describe el diseño e implementación del sistema de inventario para el jugador. Incluye:
- Definición de ítems (monedas, experiencia, madera).
- Configuración de caídas (drops) al morir NPCs.
- Componente de inventario en el jugador.
- Interfaz de usuario (UI) y manejo de entrada.

## 2. Datos y formato JSON en `data/monsters.json`
Añadir un nuevo campo `drops` a cada monstruo:
```json
"barbol": {
  ...
  "spawn_margin": 0,
  "drops": [
    { "item": "coins",      "min": 1,  "max": 5,  "chance": 0.8 },
    { "item": "experience", "min": 5,  "max": 15, "chance": 1.0 },
    { "item": "wood",       "min": 0,  "max": 3,  "chance": 0.5 }
  ]
},
```
- `item`: identificador único.
- `min`/`max`: rango de cantidad posible.
- `chance`: probabilidad de que el drop ocurra (0.0–1.0).

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
