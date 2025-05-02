class SlashController:
    def __init__(self, model):
        self.model = model

    def update(self):
        self.model.update()

    def is_finished(self):
        return self.model.is_finished()
# Path: src/roguelike_game/systems/combat/spells/slash/controller.py