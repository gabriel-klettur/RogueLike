# src/roguelike_game/entities/npc/base/interfaces.py

from abc import ABC, abstractmethod

class IModel(ABC):
    @abstractmethod
    def take_damage(self, amount: float):
        """Inflige daño al modelo."""
        pass

class IController(ABC):
    @abstractmethod
    def update(self, state):
        """Actualiza el estado del modelo dentro de GameState."""
        pass

class IView(ABC):
    @abstractmethod
    def render(self, screen, camera):
        """Dibuja la vista en pantalla."""
        pass

class IEntity(ABC):
    @abstractmethod
    def update(self, state):
        """Llama a controller.update(...)."""
        pass

    @abstractmethod
    def render(self, screen, camera):
        """Llama a view.render(...)."""
        pass
