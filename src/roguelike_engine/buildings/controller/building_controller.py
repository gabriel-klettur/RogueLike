from roguelike_engine.buildings.view.building_view import BuildingView

class BuildingController:
    """
    Controlador que orquesta la creación y uso de BuildingModel + BuildingView.
    • Se encarga de asignar self.model.zone tras cargar (por ejemplo, desde JSON o base de datos).
    • Se encarga de generar o cargar el collision_map (matriz de strings) y asignarla al modelo.
    • Proporciona métodos para renderizar ambas partes, en el orden correcto de Z.
    """

    def __init__(self, model, camera):
        """
        model: instancia de BuildingModel
        camera: instancia de la cámara (para la vista)
        """
        self.model = model
        self.view = None
        if model.image is not None:            
            self.view = BuildingView(model, camera)

    def assign_zone(self, zone_name: str):
        """
        Asigna la zona al modelo y actualiza sus coordenadas relativas→absolutas.
        Llama a self.update_rect() para que la rect de colisión se sincronice.
        """
        self.model.zone = zone_name
        # Al cambiar zona, debemos mover rect:
        abs_x, abs_y = self.model.x, self.model.y
        # Si existe un rect, lo actualizamos:
        try:
            self.model.rect.x = abs_x
            self.model.rect.y = abs_y
        except AttributeError:
            pass

    def load_collision_map(self, collision_data: list[list[str]]):
        """
        Carga el mapa de colisión (lista de strings) en el modelo:
        • El modelo invalidará su cache de collision_tiles automáticamente.
        """
        self.model.collision_map = collision_data

    def render(self, screen):
        """
        Método público para que el “game loop” llame:
        1. Dibuja la parte inferior (o “bottom”) en su z_layer correspondiente.
        2. Dibuja la parte superior (o “top”).
        """
        if not self.view:
            return

        # Importar Z_LAYERS para decidir el orden de llamada al render de cada parte
        from roguelike_engine.config.config_z_layer import Z_LAYERS

        # ─ Primero parte “bottom” ─
        # asumiendo que el world renderer (RenderSystem) va a agrupar por z-layer:
        # aquí simplemente lo dibujamos en pantalla, pero hay que asegurarse de que la llamada 
        # a BuildingController.render_bottom() se haga antes de renderizar la capa “building_high”.
        self.view.render_part(screen, top=False)

        # ─ Luego parte “top” ─
        self.view.render_part(screen, top=True)

    def update_on_camera_change(self):
        """
        Si la cámara cambió (zoom, offset), invalidamos caches de vista:
        """
        if self.view:
            self.view.clear_caches()