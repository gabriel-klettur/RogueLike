# Path: src/roguelike_game/entities/npc/interfaces.py
from abc import ABC, abstractmethod

class IModel(ABC):
    @abstractmethod
    def take_damage(self, amount: float):
        pass

class IController(ABC):
    @abstractmethod
    def update(self, state):
        pass

class IView(ABC):
    @abstractmethod
    def render(self, screen, camera):
        pass

class IEntity(ABC):
    @abstractmethod
    def update(self, state):
        pass

    @abstractmethod
    def render(self, screen, camera):
        pass