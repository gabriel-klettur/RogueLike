from roguelike_project.entities.buildings.building import Building


#! El orden funciona como la posicion z de los edificios para el orden del renderizado
def load_buildings():
    return [        
        Building(-1000, -2500, "assets/views/horizonte_1.png", solid=True, scale=(2048, 2048)),        
        Building(2200, -800, "assets/buildings/temples/catholic.png", solid=True, scale=(1024, 1536)),
        Building(3000, -900, "assets/buildings/castles/castle_2.png", scale=(3072, 2048)),
        Building(500, -300, "assets/buildings/temples/satanist.png", solid=True, scale=(1024, 1024)),
        Building(1600, -100, "assets/buildings/others/portal.png", solid=True, scale=(512, 824)),
        Building(1950, 400, "assets/buildings/shops/alchemy_tower.png", solid=True, scale=(550, 845)),
        Building(3200, 900, "assets/buildings/shops/healer_1.png", solid=True, scale=(600, 600)),
        Building(4300, 966, "assets/buildings/shops/healer.png", solid=True, scale=(768, 768)),
        Building(1100, 466, "assets/buildings/others/fuente.png", solid=True, scale=(600, 600)),
        Building(600, 966, "assets/buildings/shops/blacksmith.png", solid=True, scale=(1200, 1200)),
        Building(2100, 1066, "assets/buildings/houses/orden_house.png", solid=True, scale=(1200, 1200)),
        Building(3600, 1266, "assets/buildings/shops/jewlery_shop.png", solid=True, scale=(1000, 1000))        
    ]
