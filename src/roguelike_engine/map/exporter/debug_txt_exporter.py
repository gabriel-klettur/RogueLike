#Path: src/roguelike_engine/map/exporter/debug_txt_exporter.py

import os
import re
import logging
from roguelike_engine.config_map import DEBUG_MAPS_DIR
from .interfaces import MapExporter

logger = logging.getLogger(__name__)

class DebugTxtExporter(MapExporter):
    """
    Exporta el mapa como archivo de texto plano con numeración automática.
    """
    def __init__(
        self,
        directory: str = None,
        prefix: str = "map",
        extension: str = ".txt",
        zero_pad: int = 3
    ):
        self.directory = directory or DEBUG_MAPS_DIR
        self.prefix    = prefix
        self.extension = extension
        self.zero_pad  = zero_pad

    def export(self, map_data: list[list[str]], output_dir: str = None) -> str:
        target = output_dir or self.directory
        os.makedirs(target, exist_ok=True)

        pattern = re.compile(
            fr"^{re.escape(self.prefix)}_(\d{{{self.zero_pad}}}){re.escape(self.extension)}$"
        )
        existing = [f for f in os.listdir(target) if pattern.match(f)]

        if existing:
            nums = [int(pattern.match(f).group(1)) for f in existing]
            next_num = max(nums) + 1
        else:
            next_num = 1

        filename = f"{self.prefix}_{next_num:0{self.zero_pad}}{self.extension}"
        filepath = os.path.join(target, filename)

        with open(filepath, "w", encoding="utf-8") as f:
            for row in map_data:
                line = "".join(row) if isinstance(row, list) else row
                f.write(line + "\n")

        logger.info("Mapa de debug guardado como: %s", filepath)
        return filename
