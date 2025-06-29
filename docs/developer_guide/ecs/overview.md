# Visión General del ECS

El patrón Entity-Component-System (ECS) de RogueLike es una arquitectura que separa datos (Componentes) de la lógica (Sistemas), gestionada por un World central.

## ¿Qué es ECS?

- **Entity**: Identificador único sin comportamiento.
- **Component**: Contenedor de datos.
- **System**: Lógica que opera sobre componentes específicos.

## Beneficios

- Rendimiento y escalabilidad.
- Flexibilidad para añadir nuevos comportamientos.
- Código desacoplado y mantenible.
