import pygame
from roguelike_project.core.game.logic.events import handle_events
from roguelike_project.core.game.render.render import render_game
from roguelike_project.core.game.logic.state import GameState

from roguelike_project.entities.player.base import Player
from roguelike_project.entities.obstacle import Obstacle
from roguelike_project.ui.menu import Menu
from roguelike_project.core.camera import Camera
from roguelike_project.network.client import WebSocketClient

from roguelike_project.map.map_generator import generate_dungeon_map, merge_handmade_with_generated
from roguelike_project.map.lobby_map import LOBBY_MAP
from roguelike_project.map.map_loader import load_map_from_text

#!------------------------ Monstruos -----------------------------------------
from roguelike_project.entities.npc.monster import Monster

#!------------------------ Edificios -----------------------------------------
from roguelike_project.entities.buildings.building import Building


class Game:
    def __init__(self, screen):
        dungeon = generate_dungeon_map(60, 40)
        print("\n🗺️ Mapa generado:")
        for row in dungeon:
            print("".join(row))
        self.map_data = merge_handmade_with_generated(LOBBY_MAP, dungeon, offset_x=5, offset_y=5)
        print("\n🔗 Mapa fusionado con Lobby:")
        for row in self.map_data:
            print("".join(row))
        self.tiles = load_map_from_text(self.map_data)

        self.state = GameState(
            screen=screen,
            background=None,
            player=Player(600, 600),
            obstacles=[
                Obstacle(300, 700),
                Obstacle(600, 725),
                Building(
                    x=1300,
                    y=-600,
                    image_path="assets/buildings/temples/catholic.png",
                    scale=(1024, 1024)  
                ),
                Building(
                    x=0,
                    y=-600,
                    image_path="assets/buildings/temples/satanist.png",
                    scale=(1024, 1024)  
                ),
                Building(
                    x=1300,
                    y=200,
                    image_path="assets/buildings/shops/alchemy_1.png",        
                    scale=(512, 1024)            
                ),
                Building(
                    x=1800,
                    y=400,
                    image_path="assets/buildings/shops/healer_1.png",                    
                    scale=(512, 512)            
                ),
                Building(
                    x=2500,
                    y=500,
                    image_path="assets/buildings/shops/healer.png",                     
                    scale=(512, 512)            
                ),
                Building(
                    x=2900,
                    y=-600,
                    image_path="assets/buildings/castles/castle_1.png",                                        
                    scale=(3072, 2048)            
                ),
            ],
            enemies=[
                Monster(800, 600),  # 📍 posición inicial
                Monster(800, 800),  # 📍 posición inicial
            ],
            camera=Camera(1200, 800),
            clock=pygame.time.Clock(),
            font=pygame.font.SysFont("Arial", 18),
            menu=None,
            tiles=self.tiles
        )

        self.state.menu = Menu(state=self.state)
        self.state.remote_entities = {}

        if self.state.mode == "online":
            try:
                self.websocket = WebSocketClient("ws://localhost:8000/ws", self.state.player)
                self.websocket.start()
                self.state.websocket_connected = True
                print("✅ Conectado al servidor WebSocket")
            except Exception as e:
                print(f"❌ Error al conectar WebSocket: {e}")
                self.state.websocket_connected = False

    def handle_events(self):
        handle_events(self.state)

    def update(self):
        self.state.camera.update(self.state.player)

        solid_tiles = [tile for tile in self.state.tiles if tile.solid]
        enemies = self.state.enemies + list(self.state.remote_entities.values())

        for projectile in self.state.player.projectiles:
            projectile.update(solid_tiles=solid_tiles, enemies=enemies)

        for enemy in enemies:
            enemy.update()

        self.state.player.projectiles = [p for p in self.state.player.projectiles if p.alive]

    def render(self):
        render_game(self.state)
