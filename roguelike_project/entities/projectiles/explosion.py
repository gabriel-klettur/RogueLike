import pygame
from roguelike_project.utils.loader import load_image

class Explosion:
    def __init__(self, x, y, frames, frame_duration=5):
        self.x = x
        self.y = y
        self.frames = frames
        self.frame_duration = frame_duration
        self.current_frame = 0
        self.frame_timer = 0
        self.finished = False

    def update(self):
        self.frame_timer += 1
        if self.frame_timer >= self.frame_duration:
            self.frame_timer = 0
            self.current_frame += 1
            if self.current_frame >= len(self.frames):
                self.finished = True

    def render(self, screen, camera):
        if self.finished:
            return

        if not camera.is_in_view(self.x, self.y, (64, 64)):  # ✅ Visibilidad
            return

        frame = self.frames[self.current_frame]
        scaled = pygame.transform.scale(frame, (64, 64))
        screen.blit(scaled, camera.apply((self.x, self.y)))
