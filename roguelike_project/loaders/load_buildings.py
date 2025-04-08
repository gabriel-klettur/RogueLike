from roguelike_project.entities.buildings.building import Building

def load_buildings():
    return [
        Building(1300, -600, "assets/buildings/temples/catholic.png", (1024, 1024)),
        Building(0, -600, "assets/buildings/temples/satanist.png", (1024, 1024)),
        Building(1300, 200, "assets/buildings/shops/alchemy_1.png", (512, 1024)),
        Building(1800, 400, "assets/buildings/shops/healer_1.png", (512, 512)),
        Building(2500, 500, "assets/buildings/shops/healer.png", (512, 512)),
        Building(2900, -600, "assets/buildings/castles/castle_1.png", (3072, 2048)),
    ]
