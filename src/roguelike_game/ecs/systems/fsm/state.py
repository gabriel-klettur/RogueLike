import abc

class State(abc.ABC):
    """
    Abstract base class for FSM states.
    """

    @abc.abstractmethod
    def enter(self, entity):
        """Called when the state is entered."""
        pass

    @abc.abstractmethod
    def execute(self, entity, dt):
        """Called each update tick while state is active."""
        pass

    @abc.abstractmethod
    def exit(self, entity):
        """Called when exiting the state."""
        pass