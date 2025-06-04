Plan de alto nivel para añadir persistencia de colisiones por zona:

1) Definir modelo de datos    [COMPLETADO]
Añadir en MapManager un diccionario collision_layers: dict[str, list[list[str]]] igual que map.layers.
Cada zona (p.ej. "zone_1") tendrá su propia submatriz de colisiones ("#"/ ".").
+ **Estado**: Paso implementado en `MapManager.__init__`, se añadió `collision_layers`.

2) Carga al iniciar el mapa    [COMPLETADO]
En el constructor de MapManager, tras cargar map.matrix, invocar un load_collision_layers(zone_name) que:
• Busca archivos JSON (p.ej. data/collisions/zone_1.json).
• Si existe, llena collision_layers[zone_name] y marca tile.solid.
• Si no, inicializa la capa con base en map.matrix.
+ **Estado**: Paso implementado en `MapManager.__init__` y método `_load_collision_layers`, los archivos JSON se cargan/crean y se aplican a `tile.solid`.

3) Guardado tras editar    [COMPLETADO]
Crear función utilitaria save_collision_layers(zone_name, collision_subgrid) en paralelo a save_layers.
En TileEditorController.apply_brush, al detectar un cambio de colisión en una zona:
• Actualizar collision_layers[zone_name][local_row][local_col].
• Llamar a save_collision_layers(zone_name, collision_layers[zone_name]).
+ **Estado**: Paso implementado en `MapManager.save_collision_layers` y `TileEditorController.apply_brush`, guarda cambios en JSON.

4) Ajustes en MapManager    [COMPLETADO]
Exponer métodos get_zone_for(row,col) para centralizar lógica de zonas.
Usar ese método tanto en carga como en guardado.
+ **Estado**: Paso implementado, agregados `get_zone_for` y `get_zone_offset` en `MapManager`, usados en carga y guardado.

5) Tests automatizados
Unit tests para:
• Leer/guardar collision_layers.
• Al reiniciar el juego/ver mapa, las colisiones persistidas se reflejan en map.solid_tiles.

6) Documentación
README / wiki con:
• Formato de archivos de colisiones.
• Cómo añadir nuevas zonas.
• Casos de fallo y regeneración de capas.
Iteración y QA
Probar: editar colisiones en distintas zonas, reiniciar juego y verificar persistencia.
Ajustar rendimiento (evitar guardados demasiado frecuentes, tal vez agrupar por zonas).