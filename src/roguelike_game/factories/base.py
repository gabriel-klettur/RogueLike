"""
Base Factory Interface
"""
from abc import ABC, abstractmethod


class Factory(ABC):
    """Interfaz base para fábricas."""

    @abstractmethod
    def create(self, *args, **kwargs):
        """Crear entidad o componente."""
        pass
