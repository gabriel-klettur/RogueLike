"""
Module: class_change_request.py
One-shot component that requests a player class change.
Consumed by ClassChangeSystem on the next ECS update tick.
"""


class ClassChangeRequest:
    """
    Attached to the player entity to request a class change.

    Attributes:
        new_class: The target class name (e.g. 'warrior', 'mage').
    """

    def __init__(self, new_class: str):
        self.new_class = new_class
