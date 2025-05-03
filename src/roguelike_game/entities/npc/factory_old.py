# Path: src/roguelike_game/entities/npc/factory_old.py

from src.roguelike_game.entities.npc.interfaces import IEntity
from src.roguelike_game.entities.npc.models.monster_model import MonsterModel
from src.roguelike_game.entities.npc.controllers.monster_controller import MonsterController
from src.roguelike_game.entities.npc.views.monster_view import MonsterView
from src.roguelike_game.entities.npc.models.elite_model import EliteModel
from src.roguelike_game.entities.npc.controllers.elite_controller import EliteController
from src.roguelike_game.entities.npc.views.elite_view import EliteView

class NPC(IEntity):
    """
    Wrapper que unifica Model, Controller y View en una sola entidad.
    Delega update y render, y expone propiedades x, y, sprite_size y mask
    para que otros sistemas (colisiones, renderizado Z, etc.) accedan a ellas.
    """
    def __init__(self, model, controller, view):
        self.model = model
        self.controller = controller
        self.view = view

    @property
    def x(self):
        return self.model.x

    @property
    def y(self):
        return self.model.y

    @property
    def sprite_size(self):
        # Preferimos la constante SPRITE_SIZE de la view, si está definida
        size = getattr(self.view, 'SPRITE_SIZE', None)
        if size is not None:
            return size
        # Fallback al sprite_size del modelo
        return getattr(self.model, 'sprite_size', None)

    @property
    def mask(self):
        # Exponemos la máscara calculada en la view para colisiones
        return getattr(self.view, 'mask', None)

    def update(self, state):
        # Lógica de IA y movimiento
        self.controller.update(state)

    def render(self, screen, camera):
        # Pintado de sprite y barra de salud
        self.view.render(screen, camera)

    def __getattr__(self, name):
        # Delegar cualquier otro atributo al modelo
        return getattr(self.model, name)


class NPCFactory:
    """
    Crea instancias de NPC (model+controller+view) según tipo.
    """
    _mapping = {
        "monster": (MonsterModel, MonsterController, MonsterView),
        "elite":   (EliteModel,   EliteController,   EliteView),
    }

    @staticmethod
    def create(npc_type: str, x: float, y: float, **kwargs) -> IEntity:
        if npc_type not in NPCFactory._mapping:
            raise ValueError(f"Unknown NPC type: {npc_type}")
        ModelCls, CtrlCls, ViewCls = NPCFactory._mapping[npc_type]
        model      = ModelCls(x, y, **kwargs)
        controller = CtrlCls(model)
        view       = ViewCls(model)
        return NPC(model, controller, view)
