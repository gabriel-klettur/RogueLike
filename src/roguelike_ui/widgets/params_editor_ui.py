import json, os
import pygame
from roguelike_ui.widgets.text_input import TextInput
from jsonschema import Draft7Validator, ValidationError

class ParamsEditorUI:
    """
    Widget para editar dinámicamente los 'params' de una instancia de ítem basado en schema JSON.
    """
    def __init__(self, schema_path: str, font: pygame.font.Font, margin: int = 5):
        self.font = font
        self.margin = margin
        # Cargar esquema JSON y subschema params
        try:
            with open(schema_path, 'r') as f:
                schema = json.load(f)
            definitions = schema.get('definitions', {})
            params_schema = definitions.get('params', {})
        except Exception:
            params_schema = {}
        self.schema = params_schema
        self.validator = Draft7Validator(self.schema)
        # Campos: lista de tuplas (key, TextInput)
        self.fields: list[tuple[str, TextInput]] = []
        self.values: dict = {}

    def load_values(self, data: dict):
        """Inicializa los TextInput con los valores actuales."""
        self.values = data.copy()
        self.fields = []
        for key in self.schema.get('properties', {}):
            initial = str(self.values.get(key, ''))
            ti = TextInput(self.font)
            ti.activate(initial)
            self.fields.append((key, ti))

    def get_values(self) -> dict:
        """Retorna los valores ingresados. Lanza ValidationError si no valida."""
        out = {}
        for key, ti in self.fields:
            text = ti.text
            if text == '':
                continue
            # Convertir tipo básico según schema
            prop = self.schema['properties'][key]
            t = prop.get('type')
            try:
                if t == 'integer':
                    out[key] = int(text)
                elif t == 'number':
                    out[key] = float(text)
                elif t == 'boolean':
                    out[key] = text.lower() in ('true', '1', 'yes')
                else:
                    out[key] = text
            except ValueError:
                out[key] = text
        # Validar contra el schema
        self.validator.validate(out)
        return out

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Propaga eventos a cada TextInput."""
        for _, ti in self.fields:
            if ti.handle_event(event):
                return True
        return False

    def draw(self, surface: pygame.Surface, rect: pygame.Rect) -> None:
        """Dibuja labels y TextInput en disposición vertical."""
        x = rect.x + self.margin
        y = rect.y + self.margin
        # panel de fondo
        bg = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
        bg.fill((0,0,0,200))
        surface.blit(bg, (rect.x, rect.y))
        for key, ti in self.fields:
            # label
            lbl = self.font.render(f"{key}:", True, (255,255,255))
            surface.blit(lbl, (x, y))
            # input field
            ti.draw(surface, x + lbl.get_width() + self.margin, y)
            y += self.font.get_height() + self.margin
