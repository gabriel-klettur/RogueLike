from __future__ import annotations

from typing import Any, Dict, Set

from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.ecs.components.rendering.light_component import LightComponent


class TorchLightSystem:
    """Auto-aplica luz de antorcha a:
    - Drops en el suelo cuyo item sea una antorcha
    - Entidades que posean en su inventario al menos una antorcha

    La luz se representa como LightComponent y LightingSyncSystem ya se encarga de
    sincronizarla con el LightingManager.
    """

    def __init__(self, perf_log: dict | None = None) -> None:
        self.perf_log = perf_log
        # Cache de catálogo de ítems (id -> ItemModel)
        self._items: Dict[str, Any] = ItemsLoader().load()[0]
        # EIDs a los que este sistema les añadió luz (para no interferir con luces manuales)
        self._torch_drop_eids: Set[int] = set()
        self._torch_carrier_eids: Set[int] = set()

    # ---- Helpers -------------------------------------------------------------
    def _is_torch_model(self, model: Any | None, item_id: str) -> bool:
        if model is None:
            # Heurística por id si no hay modelo
            lid = (item_id or "").lower()
            return ("torch" in lid) or ("antorcha" in lid)
        name = str(getattr(model, "name", "") or "").lower()
        iid = str(getattr(model, "id", item_id) or "").lower()
        return ("torch" in name) or ("antorcha" in name) or ("torch" in iid) or ("antorcha" in iid)

    def _torch_light_component(self) -> LightComponent:
        # Preset antorcha (coherente con Editor)
        return LightComponent(
            radius=160,
            color=(255, 200, 140),
            intensity=1.0,
            falloff=2.0,
            enabled=True,
            flicker_amp=0.15,
            flicker_speed=2.5,
        )

    # ---- Update --------------------------------------------------------------
    def update(self, world: Any, camera: Any | None = None) -> None:
        comps = world.components
        # 1) Drops en el suelo (PhysicalItemComponent)
        drop_store = comps.get('PhysicalItemComponent', {})
        light_store = comps.setdefault('LightComponent', {})
        for eid, drop in list(drop_store.items()):
            item_id = getattr(drop, 'item_id', None)
            model = self._items.get(item_id) if item_id is not None else None
            is_torch = self._is_torch_model(model, str(item_id))
            if is_torch:
                if eid not in light_store:
                    light_store[eid] = self._torch_light_component()
                self._torch_drop_eids.add(eid)
            else:
                # Si antes le pusimos luz y ya no aplica, retírala
                if eid in self._torch_drop_eids and eid in light_store:
                    try:
                        del light_store[eid]
                    except Exception:
                        pass
                    self._torch_drop_eids.discard(eid)
        # Limpiar referencias a eids desaparecidos
        self._torch_drop_eids.intersection_update(drop_store.keys())

        # 2) Carriers con inventario
        inv_store = comps.get('InventoryComponent', {})
        for eid, inv in list(inv_store.items()):
            has_torch = False
            try:
                for st in getattr(inv, 'slots', []) or []:
                    if st is None:
                        continue
                    iid = getattr(st, 'item_id', None)
                    if iid is None:
                        continue
                    model = self._items.get(iid)
                    if self._is_torch_model(model, str(iid)):
                        has_torch = True
                        break
            except Exception:
                has_torch = False
            if has_torch:
                if eid not in light_store:
                    light_store[eid] = self._torch_light_component()
                self._torch_carrier_eids.add(eid)
            else:
                if eid in self._torch_carrier_eids and eid in light_store:
                    try:
                        del light_store[eid]
                    except Exception:
                        pass
                    self._torch_carrier_eids.discard(eid)
        # Limpiar referencias a eids desaparecidos
        self._torch_carrier_eids.intersection_update(inv_store.keys())
