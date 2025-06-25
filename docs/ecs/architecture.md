# Arquitectura del ECS

En RogueLike, el ECS está compuesto de los siguientes elementos:

## 1. World (`ECSWorld`)
- Gestión de entidades y componentes.
- Ciclo de actualización: invoca sistemas.

## 2. Entity (`Entity`)
- Identificador único.
- Agrupa componentes.

## 3. Component (`Component`)
- Clases simples con datos puros.

## 4. System (`System`)
- Lógica que aplica comportamiento a entidades con componentes específicos.

## Diagrama de alto nivel

```plaintext
[World] --> {Entities}
   |          |
   v          v
[Systems] <--[Components]
```
