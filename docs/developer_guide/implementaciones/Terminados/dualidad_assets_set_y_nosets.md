# Dualidad de assets_set y no-sets

Este documento describe los pasos para implementar, en el juego y en el editor, la funcionalidad de alternar entre cargas de sprites por hoja (`sets`) o por archivos individuales (`no-sets`), según la propiedad `active_set` en `new_players.json`.

---

## 1. Esquema JSON

Cada clase bajo `players.classes.<id>.assets` incluirá:

- **active_set**: string, valor por defecto `"sets"`, o `"no-sets"`.
- **sets**:
  - `sprites_set`: rutas de hoja de sprites por estado.
  - `sprites_data_set`: escalas por estado.
- **no-sets**: mapas estado→dirección→ruta|null.

Ejemplo:
```json
"assets":{
  "active_set":"sets",
  "sets":{ /* ... */ },
  "no-sets":{ /* ... */ }
}
```

## 2. Loader de sprites

En `load_and_scale_sprites(class_id)`:
1. Leer `active = entry["assets"]["active_set"]`.
2. Si `active == "sets"`: usar la hoja:
   - Cargar y escalar con `sprites_set` + `sprites_data_set`.
3. Si `active == "no-sets"`:
   - Para cada estado y dirección en `no-sets`, si existe ruta:
     - `load_image(path)`, escalar con factor global o por defecto.
     - Generar lista de frames (longitud 1) para animación.

El retorno será siempre `dict[state]→list[Surface]`.

## 3. Generación de animadores

`build_animator_map(sprites_dict)` no cambia: recibe el mismo formato `{state: [...frames]}`.

## 4. Integración ECS

Al crear entidades `Player`:
```python
anim_map = build_animator_map(load_and_scale_sprites(class_id))
world.components[Animator][eid].animations = anim_map
```
No se requiere lógica adicional, pues el loader ya varía según `active_set`.

## 5. Editor de entidades

Ya implementado combobox en AssetsGridPanel:
- Persistir `active_set` en JSON y modelo.
- Al togglear, invocar recarga en tiempo real similar a `_on_asset_chosen`.

## 6. Recarga en caliente

Tras cambiar `active_set`:
1. Llamar `load_and_scale_sprites` para la clase.
2. Reasignar `Animator.animations` y la imagen inicial en los componentes ECS.

## 7. Pruebas

- Unit tests para `load_and_scale_sprites`:
  - Modo `sets`, validar frames y escalas.
  - Modo `no-sets`, validar cargas individuales.
- En editor, togglear combobox y confirmar recarga visual inmediata.

---

**Fin de la especificación**