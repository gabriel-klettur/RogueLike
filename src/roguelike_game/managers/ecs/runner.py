"""
Runner de ECS: maneja update y render.
"""
class ECSRunner:
    """
    Ejecuta update y render de ECSWorld.
    """
    def update(self, ecs_world, camera):
        ecs_world.update(camera)

    def render(self, ecs_world, screen, camera):
        ecs_world.render(screen, camera)
