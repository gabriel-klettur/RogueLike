from __future__ import annotations

"""Acciones relacionadas con instancias: selección y persistencia de cambios."""
from typing import Optional, Any


def on_instance_selection_changed(controller: Any, selected_index: Optional[int], inst: Optional[dict]) -> None:
    """Mantiene el panel de Propiedades en sincronía con la selección en Instancias."""
    try:
        active_tool = getattr(getattr(controller, 'spawner_toolbar', None), 'model', None)
        active_tool = getattr(active_tool, 'active_tool', None)
        instances_visible = (active_tool == 'spawner_instances')
    except Exception:
        instances_visible = True
    try:
        controller.instance_properties.set_instance(inst, index=selected_index)
        controller.instance_properties.model.visible = bool(instances_visible and inst is not None)
    except Exception:
        pass
    # When a spawner instance is selected while the editor is open, ensure all mapped
    # visuals are present and tagged, and apply editor-only visibility according to the
    # Visuals table (eye toggles).
    try:
        if inst is not None and bool(getattr(getattr(controller, 'model', None), 'visible', False)):
            ip = getattr(controller, 'instance_properties', None)
            if ip is not None and hasattr(ip, 'visuals'):
                ip.visuals.reveal_all_mapped_buildings()
    except Exception:
        pass
    try:
        if inst is not None:
            setattr(controller.model, 'tutorial_instance_selected_pulse', True)
    except Exception:
        pass


def on_instance_saved(controller: Any, inst: dict, changed_key: Optional[str] = None) -> None:
    """Al guardar una instancia, re-enlaza su visual si cambió el `building_id`."""
    try:
        if changed_key is not None:
            ck = str(changed_key)
            if ('building_id' not in ck):
                return
    except Exception:
        pass
    try:
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        if not world:
            return
        blds = getattr(world, 'buildings', None) or []
        inst_id = None
        try:
            inst_id = str(inst.get('id')) if inst and inst.get('id') is not None else None
        except Exception:
            inst_id = None
        bld_id = None
        try:
            ov = inst.get('overrides') if isinstance(inst, dict) else None
            if isinstance(ov, dict) and ov.get('building_id') is not None:
                bld_id = int(ov.get('building_id'))
            elif inst.get('building_id') is not None:
                bld_id = int(inst.get('building_id'))
        except Exception:
            pass
        if inst_id is None or bld_id is None:
            return
        target = None
        for ob in blds:
            try:
                if getattr(ob, 'id', None) == bld_id:
                    target = ob
                    break
            except Exception:
                continue
        if target is None:
            return
        try:
            setattr(target, '_is_spawner_visual', True)
            setattr(target, 'spawner_instance_id', inst_id)
            setattr(target, 'spawn_id', inst_id)
            comps = getattr(world, 'components', {})
            if 'SpawnerConfig' in comps:
                for eid in world.get_entities_with('SpawnerConfig'):
                    try:
                        cfg = comps['SpawnerConfig'][eid]
                        if getattr(target, 'spawn_id', None) == inst_id:
                            setattr(target, '_spawner_eid', eid)
                            setattr(target, '_world_ref', world)
                            break
                    except Exception:
                        continue
        except Exception:
            pass
    except Exception:
        pass
