from __future__ import annotations
from typing import Any, Optional
from roguelike_engine.buildings import Building
from roguelike_engine.buildings.services.types import CameraProtocol


def create_building(
    *,
    rel_x: int,
    rel_y: int,
    image_path: str,
    camera: Optional[CameraProtocol] = None,
    solid: bool = True,
    scale: tuple[int, int] | None = None,
    split_ratio: float = 0.5,
    z_bottom: int | None = None,
    z_top: int | None = None,
) -> Building:
    """
    Constructor directo y tipado para crear un Building.

    Ejemplo:
        b = create_building(
            rel_x=10, rel_y=20, image_path="assets/buildings/house.png", camera=camera,
            solid=True, scale=(128, 96), split_ratio=0.45
        )
    """
    return Building(
        rel_x=rel_x,
        rel_y=rel_y,
        image_path=image_path,
        camera=camera,
        solid=solid,
        scale=scale,
        split_ratio=split_ratio,
        z_bottom=z_bottom,
        z_top=z_top,
    )


def build_from_config(cfg: dict[str, Any], camera: Optional[CameraProtocol] = None) -> Building:
    """
    Crea un Building a partir de un diccionario de configuración (p. ej. JSON).
    Campos soportados (todos opcionales salvo image_path):
      - rel_x: int (default 0)
      - rel_y: int (default 0)
      - image_path: str (OBLIGATORIO)
      - solid: bool (default True)
      - scale: [w, h] (default None)
      - split_ratio: float (default 0.5)
      - z_bottom: int (default None)
      - z_top: int (default None)
      - zone: str (opcional; se asigna tras construir)
      - collision_map: list[list[str]] (opcional; se carga tras construir)
      - collider_scope: "CG"|"CU" (opcional; se aplica al modelo)
      - images_by_state: dict[str,str] (opcional; multi-estado visual)
      - state_thresholds: list[dict] (opcional; umbrales de estado)
    """
    image_path = cfg["image_path"]
    rel_x = int(cfg.get("rel_x", 0))
    rel_y = int(cfg.get("rel_y", 0))
    solid = bool(cfg.get("solid", True))
    scale_val = cfg.get("scale")
    scale = tuple(scale_val) if isinstance(scale_val, (list, tuple)) else None
    split_ratio = float(cfg.get("split_ratio", 0.5))
    z_bottom = cfg.get("z_bottom")
    z_top = cfg.get("z_top")

    b = create_building(
        rel_x=rel_x,
        rel_y=rel_y,
        image_path=image_path,
        camera=camera,
        solid=solid,
        scale=scale,
        split_ratio=split_ratio,
        z_bottom=z_bottom,
        z_top=z_top,
    )

    # Campos opcionales post-construcción
    if "zone" in cfg and cfg["zone"] is not None:
        b.assign_zone(str(cfg["zone"]))

    if "collision_map" in cfg and cfg["collision_map"]:
        try:
            b.collision_map = cfg["collision_map"]  # type: ignore[assignment]
        except Exception:
            pass

    if "collider_scope" in cfg:
        val = cfg["collider_scope"]
        if val in ("CG", "CU"):
            b.collider_scope = val

    # Multi-estado visual
    images_by_state = cfg.get("images_by_state")
    if isinstance(images_by_state, dict) and images_by_state:
        initial_state = cfg.get("initial_visual_state")
        b.set_images_by_state(images_by_state, initial_state=initial_state)

    state_thresholds = cfg.get("state_thresholds")
    if isinstance(state_thresholds, list) and state_thresholds:
        b.set_state_thresholds(state_thresholds)

    return b


__all__ = [
    "create_building",
    "build_from_config",
]
