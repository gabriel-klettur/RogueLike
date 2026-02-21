from __future__ import annotations

from typing import Any, Dict, Tuple, Optional


def refresh_items_catalog(controller: Any) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    try:
        from roguelike_game.managers.items.loader import ItemsLoader
        loader = ItemsLoader()
        items, assets = loader.load()
    except Exception:
        import logging
        logging.getLogger(__name__).exception("[ItemCatalogService] ItemsLoader.load() failed")
        return getattr(controller.model, 'items', {}), getattr(controller.model, 'assets', {})

    controller.model.items = items
    controller.model.assets = assets

    try:
        controller.picker_controller.model.items = controller.model.items
    except Exception:
        pass
    try:
        controller.picker_controller.model.assets = controller.model.assets
        controller.picker_controller.view.assets = controller.model.assets
    except Exception:
        pass
    try:
        controller.properties_controller.set_items(controller.model.items)
    except Exception:
        pass

    game = getattr(controller, 'game', None)
    if game is not None:
        try:
            game.items = items
            game.item_assets = assets
        except Exception:
            pass
        try:
            world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
            if world:
                from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
                from roguelike_game.ecs.systems.items.consume_system import ConsumeSystem
                from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem
                from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
                from roguelike_game.ecs.systems.inventory.inventory_editor_system import InventoryEditorSystem
                from roguelike_game.ecs.components.transform.scale import Scale

                for sys in list(getattr(world, 'update_systems', [])):
                    try:
                        if isinstance(sys, MapLoadDropsSystem):
                            sys.items = items
                        elif isinstance(sys, ConsumeSystem):
                            sys.items = items
                        elif isinstance(sys, DropHoverRenderSystem):
                            sys.items = items
                        elif isinstance(sys, InventoryUISystem):
                            sys.items = items
                            try:
                                sys.icon_surfaces = {iid: assets.get(iid) for iid in items.keys()}
                            except Exception:
                                pass
                        elif isinstance(sys, InventoryEditorSystem):
                            sys.items = items
                            try:
                                sys.images = {}
                            except Exception:
                                pass
                    except Exception:
                        pass

                # Mirror items into render systems where applicable (e.g., DropHoverRenderSystem)
                for sys in list(getattr(world, 'render_systems', [])):
                    try:
                        if isinstance(sys, DropHoverRenderSystem):
                            sys.items = items
                    except Exception:
                        pass

                try:
                    comps = world.components
                    phys_map = comps.get('PhysicalItemComponent', {})
                    sprite_map = comps.get('Sprite', {})
                    scale_map = comps.get('Scale', {})
                    for eid, phys in list(phys_map.items()):
                        spr = sprite_map.get(eid)
                        if spr is None:
                            continue
                        model = items.get(phys.item_id)
                        new_img = assets.get(phys.item_id)
                        if new_img is not None:
                            try:
                                spr.image = new_img
                            except Exception:
                                pass
                        try:
                            new_scale = getattr(model, 'scale_map', None)
                            if new_scale is not None:
                                sc = scale_map.get(eid)
                                if sc is None:
                                    scale_map[eid] = Scale(new_scale)
                                else:
                                    sc.scale = new_scale
                        except Exception:
                            pass
                except Exception:
                    import logging
                    logging.getLogger(__name__).exception("[ItemCatalogService] Failed to update existing drop sprites after edit")
        except Exception:
            import logging
            logging.getLogger(__name__).exception("[ItemCatalogService] Failed updating ECS systems items cache")

    try:
        controller._items_models = items
    except Exception:
        pass

    try:
        if hasattr(controller, '_hover_renderer') and controller._hover_renderer is not None:
            controller._hover_renderer.items = items
    except Exception:
        pass

    return items, assets
