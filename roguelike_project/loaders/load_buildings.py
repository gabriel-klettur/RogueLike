from roguelike_project.entities.buildings.building import Building


#! El orden funciona como la posicion z de los edificios para el orden del renderizado
def load_buildings():
    return [        
        Building(-1000, -2500, "assets/views/horizonte_1.png", solid=True, scale=(2048, 2048)),
        Building(3000, -900, "assets/buildings/castles/castle_1.png", scale=(3072, 2048)),
        Building(1800, -600, "assets/buildings/temples/catholic.png", solid=True, scale=(1024, 1024)),
        Building(500, -600, "assets/buildings/temples/satanist.png", solid=True, scale=(1024, 1024)),
        Building(1950, 400, "assets/buildings/shops/alchemy_1.png", solid=True, scale=(550, 845)),
        Building(3200, 900, "assets/buildings/shops/healer_1.png", solid=True, scale=(600, 600)),
        Building(4300, 966, "assets/buildings/shops/healer.png", solid=True, scale=(768, 768))
    ]
