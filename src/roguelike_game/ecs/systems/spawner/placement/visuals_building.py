from __future__ import annotations

from typing import Optional

from roguelike_engine.buildings.building import Building

from .loaders import load_instances


def append_building_object_in_world(world, inst_entry: dict, tpl_entry: Optional[dict], img_path: Optional[str]) -> None:
    """Create a Building object from an instance entry and append to world's buildings.

    Ensures split_ratio, scale, ids, and spawner-related metadata are applied.
    No-op if a building with the same id already exists in world.buildings.
    """
    try:
        rel_x = int(inst_entry.get('rel_x') or 0)
        rel_y = int(inst_entry.get('rel_y') or 0)
        image_path = img_path or ''
        solid = True
        split_ratio = 0.5
        z_bottom = None
        z_top = None
        scale = None
        if isinstance(tpl_entry, dict):
            solid = bool(tpl_entry.get('solid', True))
            try:
                split_ratio = float(tpl_entry.get('split_ratio', 0.5))
            except Exception:
                split_ratio = 0.5
            z_bottom = tpl_entry.get('z_bottom')
            z_top = tpl_entry.get('z_top')
        try:
            if isinstance(inst_entry.get('overrides'), dict) and isinstance(inst_entry['overrides'].get('scale'), (list, tuple)):
                sc = inst_entry['overrides']['scale']
                if len(sc) >= 2:
                    scale = (int(sc[0]), int(sc[1]))
        except Exception:
            scale = None
        try:
            if isinstance(inst_entry.get('overrides'), dict) and (inst_entry['overrides'].get('split_ratio') is not None):
                try:
                    sr = float(inst_entry['overrides']['split_ratio'])
                    split_ratio = max(0.05, min(sr, 0.95))
                except Exception:
                    pass
        except Exception:
            pass

        b = Building(
            rel_x=rel_x,
            rel_y=rel_y,
            image_path=image_path,
            solid=solid,
            scale=scale,
            split_ratio=split_ratio,
            z_bottom=z_bottom,
            z_top=z_top,
        )
        try:
            setattr(b, 'id', inst_entry.get('id'))
        except Exception:
            pass
        try:
            setattr(b, 'zone', inst_entry.get('zone'))
        except Exception:
            pass
        try:
            setattr(b, '_is_spawner_visual', True)
            sid = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id')
            if sid is not None:
                setattr(b, 'spawner_instance_id', sid)
                setattr(b, 'spawn_id', sid)
        except Exception:
            pass
        try:
            sid_val = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id') or inst_entry.get('spawn_id')
            bid_val = inst_entry.get('id')
            if sid_val is not None and bid_val is not None:
                try:
                    sid_str = str(sid_val)
                    bid_int = int(bid_val)
                except Exception:
                    sid_str = None
                    bid_int = None
                if sid_str is not None and bid_int is not None:
                    for inst in (load_instances() or []):
                        try:
                            if str(inst.get('id')) != sid_str:
                                continue
                            vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                            for _, v in list(vis.items()):
                                try:
                                    if isinstance(v, dict):
                                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                    else:
                                        vid = int(v)
                                except Exception:
                                    vid = None
                                if vid is not None and int(vid) == int(bid_int):
                                    try:
                                        if isinstance(v, dict) and (v.get('split_ratio') is not None):
                                            sr = float(v.get('split_ratio'))
                                            sr = max(0.05, min(sr, 0.95))
                                            b.split_ratio = float(sr)
                                    except Exception:
                                        pass
                                    raise StopIteration
                            raise StopIteration
                        except StopIteration:
                            break
                        except Exception:
                            continue
        except Exception:
            pass
        try:
            if getattr(world, 'buildings', None) is None:
                world.buildings = []
        except Exception:
            pass
        try:
            for ob in getattr(world, 'buildings', []) or []:
                if getattr(ob, 'id', None) == inst_entry.get('id'):
                    return
            world.buildings.append(b)
        except Exception:
            pass
    except Exception:
        pass
