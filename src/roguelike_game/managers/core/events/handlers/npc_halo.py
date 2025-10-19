import pygame
from roguelike_ui.ui_blocker import is_blocked
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble


def consume_npc_halo_click(game, events, consumed_idx: set) -> set:
    try:
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        state = getattr(world, 'state', None) if world else None
        camera = getattr(game, 'camera', None)
        if world and state and camera and not bool(getattr(state, 'chat_open', False)):
            comps = getattr(world, 'components', {})
            pos_map = comps.get('Position', {}) or {}
            chat_map = comps.get('ChatComponent', {}) or {}
            sprite_map = comps.get('Sprite', {}) or {}
            scale_map = comps.get('Scale', {}) or {}
            multi_map = comps.get('MultiCollider', {}) or {}
            player_eid = getattr(world, 'player_entity', None)
            player_pos = pos_map.get(player_eid)
            if player_pos and chat_map:
                for i, ev in enumerate(events):
                    if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                        mx, my = getattr(ev, 'pos', pygame.mouse.get_pos())
                        try:
                            if is_blocked(mx, my):
                                continue
                        except Exception:
                            pass
                        for eid, chat in list(chat_map.items()):
                            npc_pos = pos_map.get(eid)
                            if not npc_pos:
                                continue
                            try:
                                dx = float(getattr(npc_pos, 'x', 0.0)) - float(getattr(player_pos, 'x', 0.0))
                                dy = float(getattr(npc_pos, 'y', 0.0)) - float(getattr(player_pos, 'y', 0.0))
                                dist = (dx*dx + dy*dy) ** 0.5
                                rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
                                if dist > rng:
                                    continue
                            except Exception:
                                continue
                            try:
                                wx = float(getattr(npc_pos, 'x', 0.0))
                                wy = float(getattr(npc_pos, 'y', 0.0))
                            except Exception:
                                continue
                            spr = sprite_map.get(eid)
                            scl_comp = scale_map.get(eid)
                            scl = float(getattr(scl_comp, 'scale', 1.0) or 1.0)
                            world_cx = world_cy = None
                            base_size = None
                            if spr and hasattr(spr, 'image') and spr.image:
                                try:
                                    sw, sh = spr.image.get_size()
                                    world_cx = wx + (sw * scl) / 2.0
                                    world_cy = wy + (sh * scl) / 2.0
                                    base_size = min(sw, sh) * scl
                                except Exception:
                                    world_cx = world_cy = None
                                    base_size = None
                            feet_r = None
                            if world_cx is None or world_cy is None:
                                try:
                                    mc = multi_map.get(eid)
                                    if mc and hasattr(mc, 'colliders'):
                                        feet = mc.colliders.get('feet')
                                        if feet is not None:
                                            if hasattr(feet, 'offset_x') and hasattr(feet, 'offset_y'):
                                                world_cx = wx + float(feet.offset_x)
                                                world_cy = wy + float(feet.offset_y)
                                            if hasattr(feet, 'radius'):
                                                feet_r = float(getattr(feet, 'radius', 0.0) or 0.0)
                                except Exception:
                                    pass
                            if world_cx is None or world_cy is None:
                                world_cx, world_cy = wx, wy
                            halo_r_world = None
                            if base_size is not None:
                                try:
                                    halo_r_world = max(12.0, float(base_size) * 0.25)
                                except Exception:
                                    halo_r_world = None
                            if halo_r_world is None and feet_r is not None:
                                halo_r_world = feet_r
                            if halo_r_world is None:
                                halo_r_world = 18.0
                            halo_r_screen = int(max(6.0, halo_r_world * 1.1) * (getattr(camera, 'zoom', 1.0) or 1.0))
                            try:
                                cx, cy = camera.apply((world_cx, world_cy))
                            except Exception:
                                continue
                            dxs = float(mx - cx)
                            dys = float(my - cy)
                            if (dxs*dxs + dys*dys) <= float(halo_r_screen * halo_r_screen):
                                try:
                                    state.chat_open = True
                                    state.chat_bind_target(eid)
                                    state.chat_input_buffer = ""
                                    greeting = getattr(chat, 'greeting', None)
                                    if greeting:
                                        state.chat_add_message('NPC', str(greeting))
                                        try:
                                            push_bubble(world, eid, str(greeting), color=(255, 235, 180), ttl_ms=2600)
                                        except Exception:
                                            pass
                                except Exception:
                                    pass
                                consumed_idx.add(i)
                                break
                        if i in consumed_idx:
                            continue
    except Exception:
        pass
    return consumed_idx
