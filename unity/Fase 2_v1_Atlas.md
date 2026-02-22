# Fase 2 — Atlas de Sprites: Analisis y Plan de Normalizacion

> **IMPORTANTE:** Este analisis se realiza sobre los assets de **Unity**
> (`unity/Valkur/Assets/_Project/Art/`), NO sobre `python/assets/`.
> Los archivos originales de Python **no se tocan**. Toda normalizacion
> se aplica exclusivamente sobre los assets ya migrados en el proyecto Unity.

> Datos generados automaticamente por `python/scripts/unity_asset_audit.py`.

---

## 1. Resumen Ejecutivo

- **Total archivos de imagen:** 1197
- **Tamano total:** 644.8 MB
- **Archivos que necesitan cambios:** 807 (76%)
- **Archivos ya correctos:** 250 (24%)

**Respuesta: NO se pueden crear atlas directamente.** Los problemas criticos:

| Problema                           | Archivos afectados | Severidad |
| ---------------------------------- | ------------------ | --------- |
| Tiles a 48x48 (deben ser 32x32)    | 216                | CRITICO   |
| Tiles a 64x64 (deben ser 32x32)    | 159                | CRITICO   |
| Tilesets sin cortar (>64px)        | 29                 | CRITICO   |
| Tiles a 16x16 (deben ser 32x32)    | 20                 | MEDIO     |
| NPCs sobredimensionados (>256px)   | 90                 | CRITICO   |
| Buildings sobredimensionados       | 93                 | CRITICO   |
| Items sobredimensionados (>64px)   | 48                 | CRITICO   |
| UI sobredimensionada (>128px)      | 122                | ALTO      |
| Spells sobredimensionados (>128px) | 18                 | ALTO      |
| VFX sobredimensionados             | 5                  | MEDIO     |

---

## 2. Inventario por Categoria

| Categoria            | Archivos       | Tamano             | Dimensiones dominantes                     | Modos color     |
| -------------------- | -------------- | ------------------ | ------------------------------------------ | --------------- |
| **Buildings**  | 96             | 153.7 MB           | 1024x1024(58), 1024x1536(13), 1536x1024(4) | RGBA:92, RGB:4  |
| **Characters** | 16             | 2.4 MB             | 5120x128(10), 5248x128(5), 512x128(1)      | RGBA:16         |
| **Items**      | 48             | 73.7 MB            | 1024x1024(45), 1024x1536(3)                | RGBA:47, RGB:1  |
| **Misc**       | 5              | 2.3 MB             | 600x403(1), 800x800(1), 64x64(1)           | RGBA:4, RGB:1   |
| **NPC**        | 97             | 151.0 MB           | 1024x1024(75), 256x256(7), 1024x1536(6)    | RGBA:80, RGB:17 |
| **Spells**     | 18             | 27.3 MB            | 1024x1024(16), 1536x1024(2)                | RGBA:17, RGB:1  |
| **Sprites**    | 2              | 0.0 MB             | 32x32(2)                                   | RGBA:2          |
| **Tiles**      | 564            | 28.8 MB            | 48x48(216), 64x64(159), 32x32(140)         | RGB:9, RGBA:555 |
| **UI**         | 122            | 202.5 MB           | 1024x1024(77), 1536x1024(41), 600x403(1)   | RGBA:50, RGB:72 |
| **VFX**        | 229            | 3.0 MB             | 256x256(224), 475x475(3), 1024x1024(1)     | RGBA:229        |
| **TOTAL**      | **1197** | **644.8 MB** |                                            |                 |

---

## 3. TILES — ✅ COMPLETADO (ver §15)

Todos los tiles son ahora **32x32 RGBA**. Ver seccion 15 para resultados detallados.
Ruta base: `_Project/Art/Tiles/`

**Archivos originales modificados: 424 → 12,401 tiles individuales (32x32 RGBA)**

<details>
<summary>Listado original de archivos (pre-normalizacion)</summary>

### TILES_resize_48_to_32 (216 archivos)

| #   | Archivo                                                     | Dimensiones | Modo | Tamano |
| --- | ----------------------------------------------------------- | ----------- | ---- | ------ |
| 1   | `Tiles/ready/ocean_grass/tileset_test_1.png`              | 48x48       | RGBA | 1.0KB  |
| 2   | `Tiles/ready/ocean_grass/tileset_test_2.png`              | 48x48       | RGBA | 1.0KB  |
| 3   | `Tiles/ready/ocean_grass/tileset_test_21.png`             | 48x48       | RGBA | 1.0KB  |
| 4   | `Tiles/ready/ocean_grass/tileset_test_22.png`             | 48x48       | RGBA | 0.6KB  |
| 5   | `Tiles/ready/ocean_grass/tileset_test_23.png`             | 48x48       | RGBA | 1.0KB  |
| 6   | `Tiles/ready/ocean_grass/tileset_test_24.png`             | 48x48       | RGBA | 0.9KB  |
| 7   | `Tiles/ready/ocean_grass/tileset_test_25.png`             | 48x48       | RGBA | 0.9KB  |
| 8   | `Tiles/ready/ocean_grass/tileset_test_3.png`              | 48x48       | RGBA | 1.0KB  |
| 9   | `Tiles/ready/ocean_grass/tileset_test_4.png`              | 48x48       | RGBA | 0.8KB  |
| 10  | `Tiles/ready/ocean_grass/tileset_test_41.png`             | 48x48       | RGBA | 1.0KB  |
| 11  | `Tiles/ready/ocean_grass/tileset_test_42.png`             | 48x48       | RGBA | 0.9KB  |
| 12  | `Tiles/ready/ocean_grass/tileset_test_43.png`             | 48x48       | RGBA | 1.0KB  |
| 13  | `Tiles/ready/ocean_grass/tileset_test_44.png`             | 48x48       | RGBA | 1.0KB  |
| 14  | `Tiles/ready/ocean_grass/tileset_test_45.png`             | 48x48       | RGBA | 1.0KB  |
| 15  | `Tiles/ready/ocean_grass/tileset_test_5.png`              | 48x48       | RGBA | 0.8KB  |
| 16  | `Tiles/ready/ocean_grass/tileset_test_61.png`             | 48x48       | RGBA | 0.6KB  |
| 17  | `Tiles/ready/ocean_grass/tileset_test_62.png`             | 48x48       | RGBA | 1.1KB  |
| 18  | `Tiles/ready/sand_ocean/tileset_test_10.png`              | 48x48       | RGBA | 0.9KB  |
| 19  | `Tiles/ready/sand_ocean/tileset_test_109.png`             | 48x48       | RGBA | 1.0KB  |
| 20  | `Tiles/ready/sand_ocean/tileset_test_110.png`             | 48x48       | RGBA | 1.1KB  |
| 21  | `Tiles/ready/sand_ocean/tileset_test_26.png`              | 48x48       | RGBA | 1.1KB  |
| 22  | `Tiles/ready/sand_ocean/tileset_test_27.png`              | 48x48       | RGBA | 1.0KB  |
| 23  | `Tiles/ready/sand_ocean/tileset_test_28.png`              | 48x48       | RGBA | 1.1KB  |
| 24  | `Tiles/ready/sand_ocean/tileset_test_29.png`              | 48x48       | RGBA | 1.0KB  |
| 25  | `Tiles/ready/sand_ocean/tileset_test_30.png`              | 48x48       | RGBA | 1.0KB  |
| 26  | `Tiles/ready/sand_ocean/tileset_test_46.png`              | 48x48       | RGBA | 1.0KB  |
| 27  | `Tiles/ready/sand_ocean/tileset_test_47.png`              | 48x48       | RGBA | 0.9KB  |
| 28  | `Tiles/ready/sand_ocean/tileset_test_48.png`              | 48x48       | RGBA | 1.0KB  |
| 29  | `Tiles/ready/sand_ocean/tileset_test_49.png`              | 48x48       | RGBA | 1.0KB  |
| 30  | `Tiles/ready/sand_ocean/tileset_test_50.png`              | 48x48       | RGBA | 1.1KB  |
| 31  | `Tiles/ready/sand_ocean/tileset_test_6.png`               | 48x48       | RGBA | 0.9KB  |
| 32  | `Tiles/ready/sand_ocean/tileset_test_66.png`              | 48x48       | RGBA | 0.9KB  |
| 33  | `Tiles/ready/sand_ocean/tileset_test_67.png`              | 48x48       | RGBA | 1.0KB  |
| 34  | `Tiles/ready/sand_ocean/tileset_test_68.png`              | 48x48       | RGBA | 1.0KB  |
| 35  | `Tiles/ready/sand_ocean/tileset_test_69.png`              | 48x48       | RGBA | 1.0KB  |
| 36  | `Tiles/ready/sand_ocean/tileset_test_7.png`               | 48x48       | RGBA | 1.0KB  |
| 37  | `Tiles/ready/sand_ocean/tileset_test_70.png`              | 48x48       | RGBA | 0.9KB  |
| 38  | `Tiles/ready/sand_ocean/tileset_test_8.png`               | 48x48       | RGBA | 0.9KB  |
| 39  | `Tiles/ready/sand_ocean/tileset_test_86.png`              | 48x48       | RGBA | 0.9KB  |
| 40  | `Tiles/ready/sand_ocean/tileset_test_87.png`              | 48x48       | RGBA | 0.9KB  |
| 41  | `Tiles/ready/sand_ocean/tileset_test_89.png`              | 48x48       | RGBA | 1.1KB  |
| 42  | `Tiles/ready/sand_ocean/tileset_test_9.png`               | 48x48       | RGBA | 0.9KB  |
| 43  | `Tiles/ready/sand_ocean/tileset_test_90.png`              | 48x48       | RGBA | 1.1KB  |
| 44  | `Tiles/ready/sand_ocean_2/tileset_test_101.png`           | 48x48       | RGBA | 0.9KB  |
| 45  | `Tiles/ready/sand_ocean_2/tileset_test_102.png`           | 48x48       | RGBA | 0.6KB  |
| 46  | `Tiles/ready/sand_ocean_2/tileset_test_103.png`           | 48x48       | RGBA | 1.0KB  |
| 47  | `Tiles/ready/sand_ocean_2/tileset_test_104.png`           | 48x48       | RGBA | 0.9KB  |
| 48  | `Tiles/ready/sand_ocean_2/tileset_test_105.png`           | 48x48       | RGBA | 0.9KB  |
| 49  | `Tiles/ready/sand_ocean_2/tileset_test_121.png`           | 48x48       | RGBA | 1.0KB  |
| 50  | `Tiles/ready/sand_ocean_2/tileset_test_122.png`           | 48x48       | RGBA | 0.8KB  |
| 51  | `Tiles/ready/sand_ocean_2/tileset_test_123.png`           | 48x48       | RGBA | 1.0KB  |
| 52  | `Tiles/ready/sand_ocean_2/tileset_test_124.png`           | 48x48       | RGBA | 1.1KB  |
| 53  | `Tiles/ready/sand_ocean_2/tileset_test_125.png`           | 48x48       | RGBA | 1.0KB  |
| 54  | `Tiles/ready/sand_ocean_2/tileset_test_141.png`           | 48x48       | RGBA | 0.6KB  |
| 55  | `Tiles/ready/sand_ocean_2/tileset_test_142.png`           | 48x48       | RGBA | 0.7KB  |
| 56  | `Tiles/ready/sand_ocean_2/tileset_test_81.png`            | 48x48       | RGBA | 1.0KB  |
| 57  | `Tiles/ready/sand_ocean_2/tileset_test_82.png`            | 48x48       | RGBA | 0.9KB  |
| 58  | `Tiles/ready/sand_ocean_2/tileset_test_83.png`            | 48x48       | RGBA | 1.0KB  |
| 59  | `Tiles/ready/sand_ocean_2/tileset_test_84.png`            | 48x48       | RGBA | 0.8KB  |
| 60  | `Tiles/ready/sand_ocean_2/tileset_test_85.png`            | 48x48       | RGBA | 0.8KB  |
| 61  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_1.png`    | 48x48       | RGBA | 1.0KB  |
| 62  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_2.png`    | 48x48       | RGBA | 1.0KB  |
| 63  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_21.png`   | 48x48       | RGBA | 1.0KB  |
| 64  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_22.png`   | 48x48       | RGBA | 0.6KB  |
| 65  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_23.png`   | 48x48       | RGBA | 1.0KB  |
| 66  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_24.png`   | 48x48       | RGBA | 0.9KB  |
| 67  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_25.png`   | 48x48       | RGBA | 0.9KB  |
| 68  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_3.png`    | 48x48       | RGBA | 1.0KB  |
| 69  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_4.png`    | 48x48       | RGBA | 0.8KB  |
| 70  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_41.png`   | 48x48       | RGBA | 1.0KB  |
| 71  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_42.png`   | 48x48       | RGBA | 0.9KB  |
| 72  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_43.png`   | 48x48       | RGBA | 1.0KB  |
| 73  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_44.png`   | 48x48       | RGBA | 1.0KB  |
| 74  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_45.png`   | 48x48       | RGBA | 1.0KB  |
| 75  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_5.png`    | 48x48       | RGBA | 0.8KB  |
| 76  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_61.png`   | 48x48       | RGBA | 0.6KB  |
| 77  | `Tiles/tileset_1/tiles/ocean_grass/tileset_test_62.png`   | 48x48       | RGBA | 1.1KB  |
| 78  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_10.png`    | 48x48       | RGBA | 0.9KB  |
| 79  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_109.png`   | 48x48       | RGBA | 1.0KB  |
| 80  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_110.png`   | 48x48       | RGBA | 1.1KB  |
| 81  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_26.png`    | 48x48       | RGBA | 1.1KB  |
| 82  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_27.png`    | 48x48       | RGBA | 1.0KB  |
| 83  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_28.png`    | 48x48       | RGBA | 1.1KB  |
| 84  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_29.png`    | 48x48       | RGBA | 1.0KB  |
| 85  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_30.png`    | 48x48       | RGBA | 1.0KB  |
| 86  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_46.png`    | 48x48       | RGBA | 1.0KB  |
| 87  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_47.png`    | 48x48       | RGBA | 0.9KB  |
| 88  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_48.png`    | 48x48       | RGBA | 1.0KB  |
| 89  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_49.png`    | 48x48       | RGBA | 1.0KB  |
| 90  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_50.png`    | 48x48       | RGBA | 1.1KB  |
| 91  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_6.png`     | 48x48       | RGBA | 0.9KB  |
| 92  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_66.png`    | 48x48       | RGBA | 0.9KB  |
| 93  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_67.png`    | 48x48       | RGBA | 1.0KB  |
| 94  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_68.png`    | 48x48       | RGBA | 1.0KB  |
| 95  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_69.png`    | 48x48       | RGBA | 1.0KB  |
| 96  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_7.png`     | 48x48       | RGBA | 1.0KB  |
| 97  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_70.png`    | 48x48       | RGBA | 0.9KB  |
| 98  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_8.png`     | 48x48       | RGBA | 0.9KB  |
| 99  | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_86.png`    | 48x48       | RGBA | 0.9KB  |
| 100 | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_87.png`    | 48x48       | RGBA | 0.9KB  |
| 101 | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_89.png`    | 48x48       | RGBA | 1.1KB  |
| 102 | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_9.png`     | 48x48       | RGBA | 0.9KB  |
| 103 | `Tiles/tileset_1/tiles/sand_ocean/tileset_test_90.png`    | 48x48       | RGBA | 1.1KB  |
| 104 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_101.png` | 48x48       | RGBA | 0.9KB  |
| 105 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_102.png` | 48x48       | RGBA | 0.6KB  |
| 106 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_103.png` | 48x48       | RGBA | 1.0KB  |
| 107 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_104.png` | 48x48       | RGBA | 0.9KB  |
| 108 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_105.png` | 48x48       | RGBA | 0.9KB  |
| 109 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_121.png` | 48x48       | RGBA | 1.0KB  |
| 110 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_122.png` | 48x48       | RGBA | 0.8KB  |
| 111 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_123.png` | 48x48       | RGBA | 1.0KB  |
| 112 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_124.png` | 48x48       | RGBA | 1.1KB  |
| 113 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_125.png` | 48x48       | RGBA | 1.0KB  |
| 114 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_141.png` | 48x48       | RGBA | 0.6KB  |
| 115 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_142.png` | 48x48       | RGBA | 0.7KB  |
| 116 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_81.png`  | 48x48       | RGBA | 1.0KB  |
| 117 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_82.png`  | 48x48       | RGBA | 0.9KB  |
| 118 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_83.png`  | 48x48       | RGBA | 1.0KB  |
| 119 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_84.png`  | 48x48       | RGBA | 0.8KB  |
| 120 | `Tiles/tileset_1/tiles/sand_ocean_2/tileset_test_85.png`  | 48x48       | RGBA | 0.8KB  |
| 121 | `Tiles/tileset_1/tiles/tileset_test_100.png`              | 48x48       | RGBA | 0.5KB  |
| 122 | `Tiles/tileset_1/tiles/tileset_test_106.png`              | 48x48       | RGBA | 0.3KB  |
| 123 | `Tiles/tileset_1/tiles/tileset_test_107.png`              | 48x48       | RGBA | 0.2KB  |
| 124 | `Tiles/tileset_1/tiles/tileset_test_108.png`              | 48x48       | RGBA | 0.2KB  |
| 125 | `Tiles/tileset_1/tiles/tileset_test_11.png`               | 48x48       | RGBA | 0.3KB  |
| 126 | `Tiles/tileset_1/tiles/tileset_test_111.png`              | 48x48       | RGBA | 0.2KB  |
| 127 | `Tiles/tileset_1/tiles/tileset_test_112.png`              | 48x48       | RGBA | 0.2KB  |
| 128 | `Tiles/tileset_1/tiles/tileset_test_113.png`              | 48x48       | RGBA | 0.2KB  |
| 129 | `Tiles/tileset_1/tiles/tileset_test_114.png`              | 48x48       | RGBA | 0.2KB  |
| 130 | `Tiles/tileset_1/tiles/tileset_test_115.png`              | 48x48       | RGBA | 0.2KB  |
| 131 | `Tiles/tileset_1/tiles/tileset_test_116.png`              | 48x48       | RGBA | 0.2KB  |
| 132 | `Tiles/tileset_1/tiles/tileset_test_117.png`              | 48x48       | RGBA | 0.6KB  |
| 133 | `Tiles/tileset_1/tiles/tileset_test_118.png`              | 48x48       | RGBA | 0.7KB  |
| 134 | `Tiles/tileset_1/tiles/tileset_test_119.png`              | 48x48       | RGBA | 0.6KB  |
| 135 | `Tiles/tileset_1/tiles/tileset_test_12.png`               | 48x48       | RGBA | 0.5KB  |
| 136 | `Tiles/tileset_1/tiles/tileset_test_120.png`              | 48x48       | RGBA | 0.6KB  |
| 137 | `Tiles/tileset_1/tiles/tileset_test_126.png`              | 48x48       | RGBA | 0.2KB  |
| 138 | `Tiles/tileset_1/tiles/tileset_test_127.png`              | 48x48       | RGBA | 0.1KB  |
| 139 | `Tiles/tileset_1/tiles/tileset_test_128.png`              | 48x48       | RGBA | 0.1KB  |
| 140 | `Tiles/tileset_1/tiles/tileset_test_129.png`              | 48x48       | RGBA | 0.2KB  |
| 141 | `Tiles/tileset_1/tiles/tileset_test_13.png`               | 48x48       | RGBA | 0.5KB  |
| 142 | `Tiles/tileset_1/tiles/tileset_test_130.png`              | 48x48       | RGBA | 0.3KB  |
| 143 | `Tiles/tileset_1/tiles/tileset_test_131.png`              | 48x48       | RGBA | 0.5KB  |
| 144 | `Tiles/tileset_1/tiles/tileset_test_132.png`              | 48x48       | RGBA | 0.5KB  |
| 145 | `Tiles/tileset_1/tiles/tileset_test_133.png`              | 48x48       | RGBA | 0.2KB  |
| 146 | `Tiles/tileset_1/tiles/tileset_test_134.png`              | 48x48       | RGBA | 0.2KB  |
| 147 | `Tiles/tileset_1/tiles/tileset_test_135.png`              | 48x48       | RGBA | 0.2KB  |
| 148 | `Tiles/tileset_1/tiles/tileset_test_136.png`              | 48x48       | RGBA | 0.2KB  |
| 149 | `Tiles/tileset_1/tiles/tileset_test_137.png`              | 48x48       | RGBA | 0.6KB  |
| 150 | `Tiles/tileset_1/tiles/tileset_test_138.png`              | 48x48       | RGBA | 0.6KB  |
| 151 | `Tiles/tileset_1/tiles/tileset_test_139.png`              | 48x48       | RGBA | 0.6KB  |
| 152 | `Tiles/tileset_1/tiles/tileset_test_14.png`               | 48x48       | RGBA | 0.5KB  |
| 153 | `Tiles/tileset_1/tiles/tileset_test_140.png`              | 48x48       | RGBA | 0.6KB  |
| 154 | `Tiles/tileset_1/tiles/tileset_test_143.png`              | 48x48       | RGBA | 0.3KB  |
| 155 | `Tiles/tileset_1/tiles/tileset_test_144.png`              | 48x48       | RGBA | 0.2KB  |
| 156 | `Tiles/tileset_1/tiles/tileset_test_145.png`              | 48x48       | RGBA | 0.2KB  |
| 157 | `Tiles/tileset_1/tiles/tileset_test_146.png`              | 48x48       | RGBA | 0.1KB  |
| 158 | `Tiles/tileset_1/tiles/tileset_test_147.png`              | 48x48       | RGBA | 0.1KB  |
| 159 | `Tiles/tileset_1/tiles/tileset_test_148.png`              | 48x48       | RGBA | 0.1KB  |
| 160 | `Tiles/tileset_1/tiles/tileset_test_149.png`              | 48x48       | RGBA | 0.1KB  |
| 161 | `Tiles/tileset_1/tiles/tileset_test_15.png`               | 48x48       | RGBA | 0.5KB  |
| 162 | `Tiles/tileset_1/tiles/tileset_test_150.png`              | 48x48       | RGBA | 0.2KB  |
| 163 | `Tiles/tileset_1/tiles/tileset_test_151.png`              | 48x48       | RGBA | 0.5KB  |
| 164 | `Tiles/tileset_1/tiles/tileset_test_152.png`              | 48x48       | RGBA | 0.5KB  |
| 165 | `Tiles/tileset_1/tiles/tileset_test_153.png`              | 48x48       | RGBA | 0.2KB  |
| 166 | `Tiles/tileset_1/tiles/tileset_test_154.png`              | 48x48       | RGBA | 0.2KB  |
| 167 | `Tiles/tileset_1/tiles/tileset_test_155.png`              | 48x48       | RGBA | 0.2KB  |
| 168 | `Tiles/tileset_1/tiles/tileset_test_156.png`              | 48x48       | RGBA | 0.2KB  |
| 169 | `Tiles/tileset_1/tiles/tileset_test_157.png`              | 48x48       | RGBA | 0.7KB  |
| 170 | `Tiles/tileset_1/tiles/tileset_test_158.png`              | 48x48       | RGBA | 0.6KB  |
| 171 | `Tiles/tileset_1/tiles/tileset_test_159.png`              | 48x48       | RGBA | 0.6KB  |
| 172 | `Tiles/tileset_1/tiles/tileset_test_16.png`               | 48x48       | RGBA | 0.3KB  |
| 173 | `Tiles/tileset_1/tiles/tileset_test_160.png`              | 48x48       | RGBA | 0.7KB  |
| 174 | `Tiles/tileset_1/tiles/tileset_test_17.png`               | 48x48       | RGBA | 0.6KB  |
| 175 | `Tiles/tileset_1/tiles/tileset_test_18.png`               | 48x48       | RGBA | 0.8KB  |
| 176 | `Tiles/tileset_1/tiles/tileset_test_19.png`               | 48x48       | RGBA | 0.8KB  |
| 177 | `Tiles/tileset_1/tiles/tileset_test_20.png`               | 48x48       | RGBA | 0.6KB  |
| 178 | `Tiles/tileset_1/tiles/tileset_test_31.png`               | 48x48       | RGBA | 0.4KB  |
| 179 | `Tiles/tileset_1/tiles/tileset_test_32.png`               | 48x48       | RGBA | 0.6KB  |
| 180 | `Tiles/tileset_1/tiles/tileset_test_33.png`               | 48x48       | RGBA | 0.6KB  |
| 181 | `Tiles/tileset_1/tiles/tileset_test_34.png`               | 48x48       | RGBA | 0.6KB  |
| 182 | `Tiles/tileset_1/tiles/tileset_test_35.png`               | 48x48       | RGBA | 0.6KB  |
| 183 | `Tiles/tileset_1/tiles/tileset_test_36.png`               | 48x48       | RGBA | 0.4KB  |
| 184 | `Tiles/tileset_1/tiles/tileset_test_37.png`               | 48x48       | RGBA | 0.6KB  |
| 185 | `Tiles/tileset_1/tiles/tileset_test_38.png`               | 48x48       | RGBA | 0.5KB  |
| 186 | `Tiles/tileset_1/tiles/tileset_test_39.png`               | 48x48       | RGBA | 0.4KB  |
| 187 | `Tiles/tileset_1/tiles/tileset_test_40.png`               | 48x48       | RGBA | 0.6KB  |
| 188 | `Tiles/tileset_1/tiles/tileset_test_51.png`               | 48x48       | RGBA | 0.4KB  |
| 189 | `Tiles/tileset_1/tiles/tileset_test_52.png`               | 48x48       | RGBA | 0.6KB  |
| 190 | `Tiles/tileset_1/tiles/tileset_test_53.png`               | 48x48       | RGBA | 0.6KB  |
| 191 | `Tiles/tileset_1/tiles/tileset_test_54.png`               | 48x48       | RGBA | 0.6KB  |
| 192 | `Tiles/tileset_1/tiles/tileset_test_55.png`               | 48x48       | RGBA | 0.6KB  |
| 193 | `Tiles/tileset_1/tiles/tileset_test_56.png`               | 48x48       | RGBA | 0.4KB  |
| 194 | `Tiles/tileset_1/tiles/tileset_test_57.png`               | 48x48       | RGBA | 0.6KB  |
| 195 | `Tiles/tileset_1/tiles/tileset_test_58.png`               | 48x48       | RGBA | 0.6KB  |
| 196 | `Tiles/tileset_1/tiles/tileset_test_59.png`               | 48x48       | RGBA | 0.6KB  |
| 197 | `Tiles/tileset_1/tiles/tileset_test_60.png`               | 48x48       | RGBA | 0.5KB  |
| 198 | `Tiles/tileset_1/tiles/tileset_test_71.png`               | 48x48       | RGBA | 0.4KB  |
| 199 | `Tiles/tileset_1/tiles/tileset_test_72.png`               | 48x48       | RGBA | 0.6KB  |
| 200 | `Tiles/tileset_1/tiles/tileset_test_73.png`               | 48x48       | RGBA | 0.5KB  |
| 201 | `Tiles/tileset_1/tiles/tileset_test_74.png`               | 48x48       | RGBA | 0.5KB  |
| 202 | `Tiles/tileset_1/tiles/tileset_test_75.png`               | 48x48       | RGBA | 0.6KB  |
| 203 | `Tiles/tileset_1/tiles/tileset_test_76.png`               | 48x48       | RGBA | 0.4KB  |
| 204 | `Tiles/tileset_1/tiles/tileset_test_77.png`               | 48x48       | RGBA | 0.6KB  |
| 205 | `Tiles/tileset_1/tiles/tileset_test_78.png`               | 48x48       | RGBA | 0.6KB  |
| 206 | `Tiles/tileset_1/tiles/tileset_test_79.png`               | 48x48       | RGBA | 0.6KB  |
| 207 | `Tiles/tileset_1/tiles/tileset_test_80.png`               | 48x48       | RGBA | 0.6KB  |
| 208 | `Tiles/tileset_1/tiles/tileset_test_91.png`               | 48x48       | RGBA | 0.4KB  |
| 209 | `Tiles/tileset_1/tiles/tileset_test_92.png`               | 48x48       | RGBA | 0.7KB  |
| 210 | `Tiles/tileset_1/tiles/tileset_test_93.png`               | 48x48       | RGBA | 0.7KB  |
| 211 | `Tiles/tileset_1/tiles/tileset_test_94.png`               | 48x48       | RGBA | 0.7KB  |
| 212 | `Tiles/tileset_1/tiles/tileset_test_95.png`               | 48x48       | RGBA | 0.7KB  |
| 213 | `Tiles/tileset_1/tiles/tileset_test_96.png`               | 48x48       | RGBA | 0.4KB  |
| 214 | `Tiles/tileset_1/tiles/tileset_test_97.png`               | 48x48       | RGBA | 0.6KB  |
| 215 | `Tiles/tileset_1/tiles/tileset_test_98.png`               | 48x48       | RGBA | 0.6KB  |
| 216 | `Tiles/tileset_1/tiles/tileset_test_99.png`               | 48x48       | RGBA | 0.6KB  |

### TILES_resize_64_to_32 (159 archivos)

| #   | Archivo                                              | Dimensiones | Modo | Tamano |
| --- | ---------------------------------------------------- | ----------- | ---- | ------ |
| 1   | `Tiles/multi_tiles/tiles/dirt/multi_3.png`         | 64x64       | RGBA | 1.0KB  |
| 2   | `Tiles/multi_tiles/tiles/grass/multi_141.png`      | 64x64       | RGBA | 0.4KB  |
| 3   | `Tiles/multi_tiles/tiles/grass/multi_142.png`      | 64x64       | RGBA | 0.5KB  |
| 4   | `Tiles/multi_tiles/tiles/grass/multi_143.png`      | 64x64       | RGBA | 0.4KB  |
| 5   | `Tiles/multi_tiles/tiles/grass/multi_146.png`      | 64x64       | RGBA | 1.5KB  |
| 6   | `Tiles/multi_tiles/tiles/grass/multi_147.png`      | 64x64       | RGBA | 0.6KB  |
| 7   | `Tiles/multi_tiles/tiles/grass/multi_148.png`      | 64x64       | RGBA | 0.6KB  |
| 8   | `Tiles/multi_tiles/tiles/grass/multi_152.png`      | 64x64       | RGBA | 1.1KB  |
| 9   | `Tiles/multi_tiles/tiles/grass/multi_153.png`      | 64x64       | RGBA | 1.1KB  |
| 10  | `Tiles/multi_tiles/tiles/grass/multi_155.png`      | 64x64       | RGBA | 0.5KB  |
| 11  | `Tiles/multi_tiles/tiles/grass/multi_156.png`      | 64x64       | RGBA | 0.5KB  |
| 12  | `Tiles/multi_tiles/tiles/grass/multi_157.png`      | 64x64       | RGBA | 0.5KB  |
| 13  | `Tiles/multi_tiles/tiles/grass/multi_158.png`      | 64x64       | RGBA | 1.4KB  |
| 14  | `Tiles/multi_tiles/tiles/grass/multi_159.png`      | 64x64       | RGBA | 1.5KB  |
| 15  | `Tiles/multi_tiles/tiles/grass/multi_16.png`       | 64x64       | RGBA | 1.5KB  |
| 16  | `Tiles/multi_tiles/tiles/grass/multi_160.png`      | 64x64       | RGBA | 1.5KB  |
| 17  | `Tiles/multi_tiles/tiles/grass/multi_161.png`      | 64x64       | RGBA | 0.6KB  |
| 18  | `Tiles/multi_tiles/tiles/grass/multi_162.png`      | 64x64       | RGBA | 0.6KB  |
| 19  | `Tiles/multi_tiles/tiles/grass/multi_166.png`      | 64x64       | RGBA | 1.4KB  |
| 20  | `Tiles/multi_tiles/tiles/grass/multi_167.png`      | 64x64       | RGBA | 1.4KB  |
| 21  | `Tiles/multi_tiles/tiles/grass/multi_2.png`        | 64x64       | RGBA | 1.5KB  |
| 22  | `Tiles/multi_tiles/tiles/grass/multi_20.png`       | 64x64       | RGBA | 1.6KB  |
| 23  | `Tiles/multi_tiles/tiles/grass/multi_34.png`       | 64x64       | RGBA | 1.5KB  |
| 24  | `Tiles/multi_tiles/tiles/grass/multi_48.png`       | 64x64       | RGBA | 1.5KB  |
| 25  | `Tiles/multi_tiles/tiles/grass/multi_6.png`        | 64x64       | RGBA | 1.5KB  |
| 26  | `Tiles/multi_tiles/tiles/grass/multi_62.png`       | 64x64       | RGBA | 1.5KB  |
| 27  | `Tiles/multi_tiles/tiles/grass/multi_71.png`       | 64x64       | RGBA | 1.6KB  |
| 28  | `Tiles/multi_tiles/tiles/grass/multi_72.png`       | 64x64       | RGBA | 1.6KB  |
| 29  | `Tiles/multi_tiles/tiles/grass/multi_73.png`       | 64x64       | RGBA | 1.7KB  |
| 30  | `Tiles/multi_tiles/tiles/grass/multi_74.png`       | 64x64       | RGBA | 1.6KB  |
| 31  | `Tiles/multi_tiles/tiles/grass/multi_76.png`       | 64x64       | RGBA | 1.6KB  |
| 32  | `Tiles/multi_tiles/tiles/grass/multi_85.png`       | 64x64       | RGBA | 1.5KB  |
| 33  | `Tiles/multi_tiles/tiles/grass/multi_86.png`       | 64x64       | RGBA | 1.6KB  |
| 34  | `Tiles/multi_tiles/tiles/grass/multi_87.png`       | 64x64       | RGBA | 1.7KB  |
| 35  | `Tiles/multi_tiles/tiles/grass/multi_88.png`       | 64x64       | RGBA | 1.7KB  |
| 36  | `Tiles/multi_tiles/tiles/grass_dirt/multi_100.png` | 64x64       | RGBA | 1.4KB  |
| 37  | `Tiles/multi_tiles/tiles/grass_dirt/multi_101.png` | 64x64       | RGBA | 1.5KB  |
| 38  | `Tiles/multi_tiles/tiles/grass_dirt/multi_102.png` | 64x64       | RGBA | 1.5KB  |
| 39  | `Tiles/multi_tiles/tiles/grass_dirt/multi_103.png` | 64x64       | RGBA | 1.6KB  |
| 40  | `Tiles/multi_tiles/tiles/grass_dirt/multi_104.png` | 64x64       | RGBA | 1.6KB  |
| 41  | `Tiles/multi_tiles/tiles/grass_dirt/multi_113.png` | 64x64       | RGBA | 1.6KB  |
| 42  | `Tiles/multi_tiles/tiles/grass_dirt/multi_114.png` | 64x64       | RGBA | 1.6KB  |
| 43  | `Tiles/multi_tiles/tiles/grass_dirt/multi_115.png` | 64x64       | RGBA | 1.6KB  |
| 44  | `Tiles/multi_tiles/tiles/grass_dirt/multi_116.png` | 64x64       | RGBA | 1.6KB  |
| 45  | `Tiles/multi_tiles/tiles/grass_dirt/multi_117.png` | 64x64       | RGBA | 1.5KB  |
| 46  | `Tiles/multi_tiles/tiles/grass_dirt/multi_118.png` | 64x64       | RGBA | 1.6KB  |
| 47  | `Tiles/multi_tiles/tiles/grass_dirt/multi_127.png` | 64x64       | RGBA | 1.5KB  |
| 48  | `Tiles/multi_tiles/tiles/grass_dirt/multi_128.png` | 64x64       | RGBA | 1.3KB  |
| 49  | `Tiles/multi_tiles/tiles/grass_dirt/multi_129.png` | 64x64       | RGBA | 1.5KB  |
| 50  | `Tiles/multi_tiles/tiles/grass_dirt/multi_130.png` | 64x64       | RGBA | 1.4KB  |
| 51  | `Tiles/multi_tiles/tiles/grass_dirt/multi_131.png` | 64x64       | RGBA | 1.4KB  |
| 52  | `Tiles/multi_tiles/tiles/grass_dirt/multi_132.png` | 64x64       | RGBA | 1.4KB  |
| 53  | `Tiles/multi_tiles/tiles/grass_dirt/multi_144.png` | 64x64       | RGBA | 1.5KB  |
| 54  | `Tiles/multi_tiles/tiles/grass_dirt/multi_145.png` | 64x64       | RGBA | 1.4KB  |
| 55  | `Tiles/multi_tiles/tiles/grass_dirt/multi_59.png`  | 64x64       | RGBA | 0.8KB  |
| 56  | `Tiles/multi_tiles/tiles/grass_dirt/multi_60.png`  | 64x64       | RGBA | 0.9KB  |
| 57  | `Tiles/multi_tiles/tiles/grass_dirt/multi_90.png`  | 64x64       | RGBA | 1.5KB  |
| 58  | `Tiles/multi_tiles/tiles/grass_dirt/multi_99.png`  | 64x64       | RGBA | 1.5KB  |
| 59  | `Tiles/multi_tiles/tiles/grass_rock/multi_105.png` | 64x64       | RGBA | 1.6KB  |
| 60  | `Tiles/multi_tiles/tiles/grass_rock/multi_106.png` | 64x64       | RGBA | 1.7KB  |
| 61  | `Tiles/multi_tiles/tiles/grass_rock/multi_110.png` | 64x64       | RGBA | 1.5KB  |
| 62  | `Tiles/multi_tiles/tiles/grass_rock/multi_111.png` | 64x64       | RGBA | 1.5KB  |
| 63  | `Tiles/multi_tiles/tiles/grass_rock/multi_119.png` | 64x64       | RGBA | 1.7KB  |
| 64  | `Tiles/multi_tiles/tiles/grass_rock/multi_120.png` | 64x64       | RGBA | 1.7KB  |
| 65  | `Tiles/multi_tiles/tiles/grass_rock/multi_138.png` | 64x64       | RGBA | 1.0KB  |
| 66  | `Tiles/multi_tiles/tiles/grass_rock/multi_139.png` | 64x64       | RGBA | 1.0KB  |
| 67  | `Tiles/multi_tiles/tiles/grass_rock/multi_140.png` | 64x64       | RGBA | 1.1KB  |
| 68  | `Tiles/multi_tiles/tiles/grass_rock/multi_47.png`  | 64x64       | RGBA | 1.5KB  |
| 69  | `Tiles/multi_tiles/tiles/grass_rock/multi_91.png`  | 64x64       | RGBA | 1.6KB  |
| 70  | `Tiles/multi_tiles/tiles/grass_rock/multi_92.png`  | 64x64       | RGBA | 1.7KB  |
| 71  | `Tiles/multi_tiles/tiles/grass_rock/multi_93.png`  | 64x64       | RGBA | 1.1KB  |
| 72  | `Tiles/multi_tiles/tiles/grass_rock/multi_94.png`  | 64x64       | RGBA | 1.1KB  |
| 73  | `Tiles/multi_tiles/tiles/grass_rock/multi_95.png`  | 64x64       | RGBA | 1.1KB  |
| 74  | `Tiles/multi_tiles/tiles/grass_rock/multi_96.png`  | 64x64       | RGBA | 1.6KB  |
| 75  | `Tiles/multi_tiles/tiles/grass_rock/multi_97.png`  | 64x64       | RGBA | 1.5KB  |
| 76  | `Tiles/multi_tiles/tiles/grass_sand/multi_49.png`  | 64x64       | RGBA | 1.5KB  |
| 77  | `Tiles/multi_tiles/tiles/grass_sand/multi_50.png`  | 64x64       | RGBA | 1.6KB  |
| 78  | `Tiles/multi_tiles/tiles/grass_sand/multi_51.png`  | 64x64       | RGBA | 1.6KB  |
| 79  | `Tiles/multi_tiles/tiles/grass_sand/multi_52.png`  | 64x64       | RGBA | 1.8KB  |
| 80  | `Tiles/multi_tiles/tiles/grass_sand/multi_63.png`  | 64x64       | RGBA | 1.5KB  |
| 81  | `Tiles/multi_tiles/tiles/grass_sand/multi_64.png`  | 64x64       | RGBA | 1.6KB  |
| 82  | `Tiles/multi_tiles/tiles/grass_sand/multi_65.png`  | 64x64       | RGBA | 1.6KB  |
| 83  | `Tiles/multi_tiles/tiles/grass_sand/multi_66.png`  | 64x64       | RGBA | 1.7KB  |
| 84  | `Tiles/multi_tiles/tiles/grass_sand/multi_77.png`  | 64x64       | RGBA | 1.6KB  |
| 85  | `Tiles/multi_tiles/tiles/grass_sand/multi_78.png`  | 64x64       | RGBA | 1.6KB  |
| 86  | `Tiles/multi_tiles/tiles/grass_sand/multi_79.png`  | 64x64       | RGBA | 1.8KB  |
| 87  | `Tiles/multi_tiles/tiles/grass_sand/multi_80.png`  | 64x64       | RGBA | 1.8KB  |
| 88  | `Tiles/multi_tiles/tiles/grass_water/multi_10.png` | 64x64       | RGBA | 1.8KB  |
| 89  | `Tiles/multi_tiles/tiles/grass_water/multi_11.png` | 64x64       | RGBA | 1.5KB  |
| 90  | `Tiles/multi_tiles/tiles/grass_water/multi_12.png` | 64x64       | RGBA | 1.7KB  |
| 91  | `Tiles/multi_tiles/tiles/grass_water/multi_13.png` | 64x64       | RGBA | 1.5KB  |
| 92  | `Tiles/multi_tiles/tiles/grass_water/multi_14.png` | 64x64       | RGBA | 1.7KB  |
| 93  | `Tiles/multi_tiles/tiles/grass_water/multi_21.png` | 64x64       | RGBA | 1.4KB  |
| 94  | `Tiles/multi_tiles/tiles/grass_water/multi_22.png` | 64x64       | RGBA | 1.6KB  |
| 95  | `Tiles/multi_tiles/tiles/grass_water/multi_23.png` | 64x64       | RGBA | 1.5KB  |
| 96  | `Tiles/multi_tiles/tiles/grass_water/multi_24.png` | 64x64       | RGBA | 1.6KB  |
| 97  | `Tiles/multi_tiles/tiles/grass_water/multi_25.png` | 64x64       | RGBA | 1.5KB  |
| 98  | `Tiles/multi_tiles/tiles/grass_water/multi_26.png` | 64x64       | RGBA | 1.6KB  |
| 99  | `Tiles/multi_tiles/tiles/grass_water/multi_27.png` | 64x64       | RGBA | 1.5KB  |
| 100 | `Tiles/multi_tiles/tiles/grass_water/multi_28.png` | 64x64       | RGBA | 1.5KB  |
| 101 | `Tiles/multi_tiles/tiles/grass_water/multi_35.png` | 64x64       | RGBA | 1.6KB  |
| 102 | `Tiles/multi_tiles/tiles/grass_water/multi_36.png` | 64x64       | RGBA | 1.6KB  |
| 103 | `Tiles/multi_tiles/tiles/grass_water/multi_37.png` | 64x64       | RGBA | 1.7KB  |
| 104 | `Tiles/multi_tiles/tiles/grass_water/multi_38.png` | 64x64       | RGBA | 1.6KB  |
| 105 | `Tiles/multi_tiles/tiles/grass_water/multi_39.png` | 64x64       | RGBA | 1.6KB  |
| 106 | `Tiles/multi_tiles/tiles/grass_water/multi_40.png` | 64x64       | RGBA | 1.7KB  |
| 107 | `Tiles/multi_tiles/tiles/grass_water/multi_41.png` | 64x64       | RGBA | 1.6KB  |
| 108 | `Tiles/multi_tiles/tiles/grass_water/multi_42.png` | 64x64       | RGBA | 1.6KB  |
| 109 | `Tiles/multi_tiles/tiles/grass_water/multi_7.png`  | 64x64       | RGBA | 1.5KB  |
| 110 | `Tiles/multi_tiles/tiles/grass_water/multi_8.png`  | 64x64       | RGBA | 1.7KB  |
| 111 | `Tiles/multi_tiles/tiles/grass_water/multi_9.png`  | 64x64       | RGBA | 1.5KB  |
| 112 | `Tiles/multi_tiles/tiles/rock/multi_107.png`       | 64x64       | RGBA | 0.9KB  |
| 113 | `Tiles/multi_tiles/tiles/rock/multi_108.png`       | 64x64       | RGBA | 0.9KB  |
| 114 | `Tiles/multi_tiles/tiles/rock/multi_109.png`       | 64x64       | RGBA | 0.9KB  |
| 115 | `Tiles/multi_tiles/tiles/rock/multi_121.png`       | 64x64       | RGBA | 0.9KB  |
| 116 | `Tiles/multi_tiles/tiles/rock/multi_122.png`       | 64x64       | RGBA | 0.9KB  |
| 117 | `Tiles/multi_tiles/tiles/rock/multi_123.png`       | 64x64       | RGBA | 0.9KB  |
| 118 | `Tiles/multi_tiles/tiles/rock/multi_124.png`       | 64x64       | RGBA | 0.9KB  |
| 119 | `Tiles/multi_tiles/tiles/rock/multi_125.png`       | 64x64       | RGBA | 1.0KB  |
| 120 | `Tiles/multi_tiles/tiles/rock/multi_126.png`       | 64x64       | RGBA | 0.9KB  |
| 121 | `Tiles/multi_tiles/tiles/rock/multi_15.png`        | 64x64       | RGBA | 0.9KB  |
| 122 | `Tiles/multi_tiles/tiles/rock/multi_17.png`        | 64x64       | RGBA | 1.6KB  |
| 123 | `Tiles/multi_tiles/tiles/rock/multi_61.png`        | 64x64       | RGBA | 0.9KB  |
| 124 | `Tiles/multi_tiles/tiles/rock/multi_75.png`        | 64x64       | RGBA | 0.9KB  |
| 125 | `Tiles/multi_tiles/tiles/sand/multi_18.png`        | 64x64       | RGBA | 1.3KB  |
| 126 | `Tiles/multi_tiles/tiles/sand/multi_19.png`        | 64x64       | RGBA | 1.6KB  |
| 127 | `Tiles/multi_tiles/tiles/sand/multi_4.png`         | 64x64       | RGBA | 1.3KB  |
| 128 | `Tiles/multi_tiles/tiles/sand/multi_5.png`         | 64x64       | RGBA | 1.6KB  |
| 129 | `Tiles/multi_tiles/tiles/sand/multi_53.png`        | 64x64       | RGBA | 1.7KB  |
| 130 | `Tiles/multi_tiles/tiles/sand/multi_54.png`        | 64x64       | RGBA | 1.7KB  |
| 131 | `Tiles/multi_tiles/tiles/sand/multi_55.png`        | 64x64       | RGBA | 1.9KB  |
| 132 | `Tiles/multi_tiles/tiles/sand/multi_56.png`        | 64x64       | RGBA | 1.9KB  |
| 133 | `Tiles/multi_tiles/tiles/sand/multi_57.png`        | 64x64       | RGBA | 1.2KB  |
| 134 | `Tiles/multi_tiles/tiles/sand/multi_58.png`        | 64x64       | RGBA | 1.3KB  |
| 135 | `Tiles/multi_tiles/tiles/sand/multi_67.png`        | 64x64       | RGBA | 1.7KB  |
| 136 | `Tiles/multi_tiles/tiles/sand/multi_68.png`        | 64x64       | RGBA | 1.7KB  |
| 137 | `Tiles/multi_tiles/tiles/sand/multi_69.png`        | 64x64       | RGBA | 1.9KB  |
| 138 | `Tiles/multi_tiles/tiles/sand/multi_70.png`        | 64x64       | RGBA | 1.9KB  |
| 139 | `Tiles/multi_tiles/tiles/sand/multi_81.png`        | 64x64       | RGBA | 1.7KB  |
| 140 | `Tiles/multi_tiles/tiles/sand/multi_82.png`        | 64x64       | RGBA | 1.6KB  |
| 141 | `Tiles/multi_tiles/tiles/sand/multi_83.png`        | 64x64       | RGBA | 1.8KB  |
| 142 | `Tiles/multi_tiles/tiles/sand/multi_84.png`        | 64x64       | RGBA | 1.8KB  |
| 143 | `Tiles/multi_tiles/tiles/sand_water/multi_43.png`  | 64x64       | RGBA | 1.6KB  |
| 144 | `Tiles/multi_tiles/tiles/sand_water/multi_44.png`  | 64x64       | RGBA | 1.6KB  |
| 145 | `Tiles/multi_tiles/tiles/sand_water/multi_45.png`  | 64x64       | RGBA | 1.6KB  |
| 146 | `Tiles/multi_tiles/tiles/sand_water/multi_46.png`  | 64x64       | RGBA | 1.6KB  |
| 147 | `Tiles/multi_tiles/tiles/trees/multi_135.png`      | 64x64       | RGBA | 0.7KB  |
| 148 | `Tiles/multi_tiles/tiles/trees/multi_136.png`      | 64x64       | RGBA | 0.9KB  |
| 149 | `Tiles/multi_tiles/tiles/trees/multi_137.png`      | 64x64       | RGBA | 0.8KB  |
| 150 | `Tiles/multi_tiles/tiles/trees/multi_149.png`      | 64x64       | RGBA | 0.6KB  |
| 151 | `Tiles/multi_tiles/tiles/trees/multi_150.png`      | 64x64       | RGBA | 0.8KB  |
| 152 | `Tiles/multi_tiles/tiles/trees/multi_151.png`      | 64x64       | RGBA | 0.6KB  |
| 153 | `Tiles/multi_tiles/tiles/trees/multi_163.png`      | 64x64       | RGBA | 0.5KB  |
| 154 | `Tiles/multi_tiles/tiles/trees/multi_164.png`      | 64x64       | RGBA | 0.6KB  |
| 155 | `Tiles/multi_tiles/tiles/trees/multi_165.png`      | 64x64       | RGBA | 0.4KB  |
| 156 | `Tiles/multi_tiles/tiles/water/multi_29.png`       | 64x64       | RGBA | 1.3KB  |
| 157 | `Tiles/multi_tiles/tiles/water/multi_30.png`       | 64x64       | RGBA | 1.3KB  |
| 158 | `Tiles/multi_tiles/tiles/water/multi_31.png`       | 64x64       | RGBA | 1.3KB  |
| 159 | `Tiles/multi_tiles/tiles/water/multi_32.png`       | 64x64       | RGBA | 1.2KB  |

### TILES_slice_tileset (29 archivos)

| #  | Archivo                                   | Dimensiones | Modo | Tamano   |
| -- | ----------------------------------------- | ----------- | ---- | -------- |
| 1  | `Tiles/dungeon/dungeon_1.png`           | 1024x1024   | RGB  | 1212.7KB |
| 2  | `Tiles/dungeon/dungeon_c_1.png`         | 1024x1024   | RGB  | 1171.2KB |
| 3  | `Tiles/dungeon_1.png`                   | 1024x1024   | RGB  | 1212.7KB |
| 4  | `Tiles/dungeon_2.png`                   | 1024x1024   | RGB  | 1212.7KB |
| 5  | `Tiles/dungeon_3.png`                   | 1024x1024   | RGB  | 1212.7KB |
| 6  | `Tiles/dungeon_c_1.png`                 | 1024x1024   | RGB  | 1171.2KB |
| 7  | `Tiles/dungeon_c_2.png`                 | 1024x1024   | RGB  | 1171.2KB |
| 8  | `Tiles/floor.png`                       | 300x300     | RGBA | 101.7KB  |
| 9  | `Tiles/floor_1.png`                     | 1024x1024   | RGBA | 2658.5KB |
| 10 | `Tiles/floor_2.png`                     | 1024x1024   | RGBA | 2468.2KB |
| 11 | `Tiles/floor_3.png`                     | 1024x1024   | RGBA | 2965.5KB |
| 12 | `Tiles/floor_4.png`                     | 1024x1024   | RGBA | 2536.1KB |
| 13 | `Tiles/floor_5.png`                     | 1024x1024   | RGBA | 2712.5KB |
| 14 | `Tiles/floor_6.png`                     | 1024x1024   | RGBA | 2949.6KB |
| 15 | `Tiles/floor_7.png`                     | 1024x1024   | RGBA | 2579.8KB |
| 16 | `Tiles/multi_tiles/multi.png`           | 896x896     | RGBA | 133.5KB  |
| 17 | `Tiles/multi_tiles/multi_grid.png`      | 896x896     | RGB  | 196.2KB  |
| 18 | `Tiles/ready/grass_dirt/tileset3.png`   | 160x128     | RGBA | 14.0KB   |
| 19 | `Tiles/ready/grass_rock/tileset4.png`   | 160x128     | RGBA | 18.7KB   |
| 20 | `Tiles/ready/grass_rock/tileset5.png`   | 160x128     | RGBA | 17.6KB   |
| 21 | `Tiles/ready/grass_rock/tileset6.png`   | 160x128     | RGBA | 21.3KB   |
| 22 | `Tiles/ready/rock_water/tileset8.png`   | 160x128     | RGBA | 19.4KB   |
| 23 | `Tiles/ready/rock_water/tileset9.png`   | 160x128     | RGBA | 19.4KB   |
| 24 | `Tiles/ready/sand_grass/tileset1.png`   | 160x128     | RGBA | 40.7KB   |
| 25 | `Tiles/ready/sand_grass/tileset2.png`   | 160x128     | RGBA | 16.7KB   |
| 26 | `Tiles/ready/sand_rock/tileset7.png`    | 160x128     | RGBA | 23.7KB   |
| 27 | `Tiles/tileset_1/tileset_test.png`      | 960x384     | RGBA | 70.5KB   |
| 28 | `Tiles/tileset_1/tileset_test_grid.png` | 960x384     | RGB  | 110.6KB  |
| 29 | `Tiles/wall.PNG`                        | 864x880     | RGBA | 842.6KB  |

### TILES_upscale_16_to_32 (20 archivos)

| #  | Archivo                                             | Dimensiones | Modo | Tamano |
| -- | --------------------------------------------------- | ----------- | ---- | ------ |
| 1  | `Tiles/tileset_1/rock_grass/rock_grass_32_1.png`  | 16x16       | RGBA | 0.7KB  |
| 2  | `Tiles/tileset_1/rock_grass/rock_grass_32_10.png` | 16x16       | RGBA | 0.7KB  |
| 3  | `Tiles/tileset_1/rock_grass/rock_grass_32_11.png` | 16x16       | RGBA | 0.7KB  |
| 4  | `Tiles/tileset_1/rock_grass/rock_grass_32_12.png` | 16x16       | RGBA | 0.7KB  |
| 5  | `Tiles/tileset_1/rock_grass/rock_grass_32_13.png` | 16x16       | RGBA | 0.7KB  |
| 6  | `Tiles/tileset_1/rock_grass/rock_grass_32_14.png` | 16x16       | RGBA | 0.7KB  |
| 7  | `Tiles/tileset_1/rock_grass/rock_grass_32_15.png` | 16x16       | RGBA | 0.7KB  |
| 8  | `Tiles/tileset_1/rock_grass/rock_grass_32_16.png` | 16x16       | RGBA | 0.4KB  |
| 9  | `Tiles/tileset_1/rock_grass/rock_grass_32_17.png` | 16x16       | RGBA | 0.7KB  |
| 10 | `Tiles/tileset_1/rock_grass/rock_grass_32_18.png` | 16x16       | RGBA | 0.1KB  |
| 11 | `Tiles/tileset_1/rock_grass/rock_grass_32_19.png` | 16x16       | RGBA | 0.1KB  |
| 12 | `Tiles/tileset_1/rock_grass/rock_grass_32_2.png`  | 16x16       | RGBA | 0.7KB  |
| 13 | `Tiles/tileset_1/rock_grass/rock_grass_32_20.png` | 16x16       | RGBA | 0.1KB  |
| 14 | `Tiles/tileset_1/rock_grass/rock_grass_32_3.png`  | 16x16       | RGBA | 0.7KB  |
| 15 | `Tiles/tileset_1/rock_grass/rock_grass_32_4.png`  | 16x16       | RGBA | 0.6KB  |
| 16 | `Tiles/tileset_1/rock_grass/rock_grass_32_5.png`  | 16x16       | RGBA | 0.6KB  |
| 17 | `Tiles/tileset_1/rock_grass/rock_grass_32_6.png`  | 16x16       | RGBA | 0.7KB  |
| 18 | `Tiles/tileset_1/rock_grass/rock_grass_32_7.png`  | 16x16       | RGBA | 0.4KB  |
| 19 | `Tiles/tileset_1/rock_grass/rock_grass_32_8.png`  | 16x16       | RGBA | 0.7KB  |
| 20 | `Tiles/tileset_1/rock_grass/rock_grass_32_9.png`  | 16x16       | RGBA | 0.6KB  |

</details>

### TILES ya correctos (0 archivos — sin cambios)

Estos tiles ya son 32x32 RGBA y no necesitan modificacion.

---

## 4. CHARACTERS — Sin cambios necesarios

Los 16 sprite sheets de personajes estan correctamente estructurados.
Unity los importa como Sprite Mode=Multiple y los corta con Sprite Editor (grid 128x128).

| #  | Archivo                                        | Dimensiones | Modo |
| -- | ---------------------------------------------- | ----------- | ---- |
| 1  | `Characters/Selection.png/Selection.png`     | 512x128     | RGBA |
| 2  | `Characters/barbarian/barbarian_casting.png` | 5120x128    | RGBA |
| 3  | `Characters/barbarian/barbarian_idle.png`    | 5120x128    | RGBA |
| 4  | `Characters/barbarian/barbarian_walking.png` | 5120x128    | RGBA |
| 5  | `Characters/dwarf/dwarf_casting.png`         | 5120x128    | RGBA |
| 6  | `Characters/dwarf/dwarf_idle.png`            | 5248x128    | RGBA |
| 7  | `Characters/dwarf/dwarf_walking.png`         | 5248x128    | RGBA |
| 8  | `Characters/elven/elven_casting.png`         | 5120x128    | RGBA |
| 9  | `Characters/elven/elven_idle.png`            | 5248x128    | RGBA |
| 10 | `Characters/elven/elven_walking.png`         | 5120x128    | RGBA |
| 11 | `Characters/mague/mague_casting.png`         | 5120x128    | RGBA |
| 12 | `Characters/mague/mague_idle.png`            | 5248x128    | RGBA |
| 13 | `Characters/mague/mague_walking.png`         | 5120x128    | RGBA |
| 14 | `Characters/valkyrie/valkyrie_casting.png`   | 5120x128    | RGBA |
| 15 | `Characters/valkyrie/valkyrie_idle.png`      | 5248x128    | RGBA |
| 16 | `Characters/valkyrie/valkyrie_walking.png`   | 5120x128    | RGBA |

---

## 5. NPC — Archivos a modificar

Los NPCs son sprites generados por IA a 1024x1024 que necesitan:

1. **Auto-crop** (recortar transparencia/fondo)
2. **Resize** a 128x128 (consistente con player frames)
3. **Ensure RGBA** (los RGB necesitan deteccion de fondo + transparencia)

Ruta base: `_Project/Art/NPC/`

**Total archivos a modificar: 90**
**Tamano objetivo: 128x128 RGBA**

### NPC_autocrop_resize (73 archivos)

| #  | Archivo                                                                                           | Dimensiones | Modo | Tamano   |
| -- | ------------------------------------------------------------------------------------------------- | ----------- | ---- | -------- |
| 1  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl1_down.png`                                      | 1024x1024   | RGBA | 1558.8KB |
| 2  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl1_left.png`                                      | 1024x1024   | RGBA | 892.6KB  |
| 3  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl1_right.png`                                     | 1024x1024   | RGBA | 1503.6KB |
| 4  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl1_top.png`                                       | 1024x1024   | RGBA | 1507.3KB |
| 5  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl2_down.png`                                      | 1024x1024   | RGBA | 1625.6KB |
| 6  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl2_left.png`                                      | 1024x1024   | RGBA | 1246.8KB |
| 7  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl2_right.png`                                     | 1024x1024   | RGBA | 1596.5KB |
| 8  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl2_top.png`                                       | 1024x1024   | RGBA | 1590.1KB |
| 9  | `NPC/Monsters/barbol boss/final_boss_barbol_lvl3_down.png`                                      | 1024x1024   | RGBA | 1712.5KB |
| 10 | `NPC/Monsters/barbol boss/final_boss_barbol_lvl3_left.png`                                      | 1024x1024   | RGBA | 1514.9KB |
| 11 | `NPC/Monsters/barbol boss/final_boss_barbol_lvl3_right.png`                                     | 1024x1024   | RGBA | 1594.7KB |
| 12 | `NPC/Monsters/barbol boss/final_boss_barbol_lvl3_top.png`                                       | 1024x1024   | RGBA | 1642.1KB |
| 13 | `NPC/Monsters/barbol/barbol_1_down.png`                                                         | 1024x1024   | RGBA | 1783.6KB |
| 14 | `NPC/Monsters/barbol/barbol_1_down_attack.png`                                                  | 1024x1024   | RGBA | 1675.1KB |
| 15 | `NPC/Monsters/barbol/barbol_1_down_chase.png`                                                   | 1024x1024   | RGBA | 1861.8KB |
| 16 | `NPC/Monsters/barbol/barbol_1_left.png`                                                         | 1024x1024   | RGBA | 1254.9KB |
| 17 | `NPC/Monsters/barbol/barbol_1_left_chase.png`                                                   | 1024x1024   | RGBA | 1936.6KB |
| 18 | `NPC/Monsters/barbol/barbol_1_left_damage.png`                                                  | 1024x1024   | RGBA | 1473.2KB |
| 19 | `NPC/Monsters/barbol/barbol_1_right.png`                                                        | 1024x1024   | RGBA | 1757.3KB |
| 20 | `NPC/Monsters/barbol/barbol_1_right_attack.png`                                                 | 1024x1024   | RGBA | 1568.6KB |
| 21 | `NPC/Monsters/barbol/barbol_1_right_chase.png`                                                  | 1024x1024   | RGBA | 2045.5KB |
| 22 | `NPC/Monsters/barbol/barbol_1_right_damage.png`                                                 | 1024x1024   | RGBA | 1330.7KB |
| 23 | `NPC/Monsters/barbol/barbol_1_top.png`                                                          | 1024x1024   | RGBA | 1670.0KB |
| 24 | `NPC/Monsters/barbol/barbol_1_top_attack.png`                                                   | 1024x1024   | RGBA | 1635.6KB |
| 25 | `NPC/Monsters/barbol/barbol_1_top_chase.png`                                                    | 1024x1024   | RGBA | 1886.0KB |
| 26 | `NPC/Monsters/barbol/barbol_2_down_right_attack.png`                                            | 1024x1024   | RGBA | 1446.5KB |
| 27 | `NPC/Monsters/barbol/barbol_2_left.png`                                                         | 1024x1024   | RGBA | 1291.5KB |
| 28 | `NPC/Monsters/barbol/barbol_2_right.png`                                                        | 1024x1024   | RGBA | 1726.7KB |
| 29 | `NPC/Monsters/barbol/barbol_2_right_attack.png`                                                 | 1024x1024   | RGBA | 1712.9KB |
| 30 | `NPC/Monsters/barbol/barbol_female_death.png`                                                   | 1024x1024   | RGBA | 929.5KB  |
| 31 | `NPC/Monsters/barbol_brother_felipondor/felipondor_brother_final_boss.png`                      | 1024x1536   | RGBA | 2208.3KB |
| 32 | `NPC/Monsters/barbol_druida/druida_1_down.png`                                                  | 1024x1024   | RGBA | 1526.7KB |
| 33 | `NPC/Monsters/barbol_druida/druida_1_right.png`                                                 | 1024x1024   | RGBA | 1361.1KB |
| 34 | `NPC/Monsters/barbol_druida/druida_1_top.png`                                                   | 1024x1024   | RGBA | 1311.6KB |
| 35 | `NPC/Monsters/barbol_elite/elite_barbol_1_down.png`                                             | 510x514     | RGBA | 267.5KB  |
| 36 | `NPC/Monsters/barbol_elite/elite_barbol_1_left.png`                                             | 510x514     | RGBA | 212.0KB  |
| 37 | `NPC/Monsters/barbol_elite/elite_barbol_1_right.png`                                            | 510x514     | RGBA | 210.4KB  |
| 38 | `NPC/Monsters/barbol_elite/elite_barbol_1_top.png`                                              | 510x514     | RGBA | 243.0KB  |
| 39 | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 08_59_54 PM.png`                                | 1024x1024   | RGBA | 1779.8KB |
| 40 | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 09_00_07 PM.png`                                | 1024x1024   | RGBA | 1779.8KB |
| 41 | `NPC/Monsters/dragon/ChatGPT_Image_May_5_2025_09_00_07_PM.png`                                  | 1024x1024   | RGBA | 1779.8KB |
| 42 | `NPC/Monsters/dragon/dragon.png`                                                                | 136x264     | RGBA | 11.8KB   |
| 43 | `NPC/Monsters/fairy/fairy_1_down.png`                                                           | 1024x1024   | RGBA | 1535.8KB |
| 44 | `NPC/Monsters/fairy/fairy_1_right.png`                                                          | 1024x1024   | RGBA | 1564.1KB |
| 45 | `NPC/Monsters/fairy/fairy_1_top.png`                                                            | 1024x1024   | RGBA | 1658.0KB |
| 46 | `NPC/Monsters/jabato/ChatGPT Image 28 may 2025, 09_51_17.png`                                   | 1024x1024   | RGBA | 1332.9KB |
| 47 | `NPC/Monsters/jabato/ChatGPT Image 28 may 2025, 10_11_44.png`                                   | 1024x1536   | RGBA | 2128.7KB |
| 48 | `NPC/Monsters/jabato/ChatGPT Image May 26, 2025, 09_19_32 AM.png`                               | 1536x1024   | RGBA | 2879.2KB |
| 49 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 06_48_28 PM.png`                                  | 1024x1024   | RGBA | 1586.3KB |
| 50 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 07_16_43 PM.png`                                  | 1024x1024   | RGBA | 1546.3KB |
| 51 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 07_23_02 PM.png`                                  | 1024x1024   | RGBA | 1611.1KB |
| 52 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_08_04 AM.png`                                  | 1024x1024   | RGBA | 1529.0KB |
| 53 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_25_30 AM.png`                                  | 1024x1024   | RGBA | 1785.9KB |
| 54 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_49_43 AM.png`                                  | 1024x1024   | RGBA | 1713.8KB |
| 55 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_52_45 AM.png`                                  | 1024x1024   | RGBA | 1738.5KB |
| 56 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_52_48 AM.png`                                  | 1024x1024   | RGBA | 1816.7KB |
| 57 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_54_49 AM.png`                                  | 1024x1024   | RGBA | 1752.4KB |
| 58 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_56_12 AM.png`                                  | 1024x1024   | RGBA | 1853.7KB |
| 59 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_59_26 AM.png`                                  | 1024x1024   | RGBA | 2042.4KB |
| 60 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 12_00_11 PM.png`                                  | 1024x1024   | RGBA | 1891.5KB |
| 61 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 12_07_50 PM.png`                                  | 1024x1024   | RGBA | 1914.9KB |
| 62 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 12_11_52 PM.png`                                  | 1024x1024   | RGBA | 1902.0KB |
| 63 | `NPC/Monsters/new/Nigro/ChatGPT Image May 26, 2025, 11_16_39 AM.png`                            | 1024x1024   | RGBA | 1886.5KB |
| 64 | `NPC/Monsters/new/Nigro/ChatGPT Image May 26, 2025, 11_19_45 AM.png`                            | 1024x1024   | RGBA | 1814.8KB |
| 65 | `NPC/Monsters/new/mini Migro/ChatGPT Image May 26, 2025, 11_08_02 AM.png`                       | 1024x1024   | RGBA | 1517.9KB |
| 66 | `NPC/Monsters/new/mini Migro/ChatGPT Image May 26, 2025, 11_18_54 AM.png`                       | 1024x1024   | RGBA | 1674.9KB |
| 67 | `NPC/Monsters/new/squeleton/ChatGPT Image May 26, 2025, 11_07_57 AM.png`                        | 1024x1024   | RGBA | 1534.1KB |
| 68 | `NPC/Monsters/new/squeleton/ChatGPT Image May 26, 2025, 11_12_08 AM.png`                        | 1024x1024   | RGBA | 1536.4KB |
| 69 | `NPC/Monsters/new/squeleton/ChatGPT Image May 26, 2025, 11_13_41 AM.png`                        | 1024x1024   | RGBA | 1544.0KB |
| 70 | `NPC/Neutral/vendors/cheff/gatita_chanchita/normal/gatitachanchita_left.png`                    | 1024x1536   | RGBA | 2388.3KB |
| 71 | `NPC/Neutral/vendors/cheff/gatita_chanchita/normal/gatitachanchita_right.png`                   | 1024x1536   | RGBA | 2244.8KB |
| 72 | `NPC/Neutral/vendors/cheff/gatita_chanchita/normal/gatitachanchita_top.png`                     | 1024x1536   | RGBA | 2803.0KB |
| 73 | `NPC/Neutral/vendors/cheff/gatita_chanchita/others/ChatGPT Image May 26, 2025, 09_48_51 PM.png` | 1024x1536   | RGBA | 2760.1KB |

### NPC_autocrop_resize_rgba (17 archivos)

| #  | Archivo                                                                     | Dimensiones | Modo | Tamano   |
| -- | --------------------------------------------------------------------------- | ----------- | ---- | -------- |
| 1  | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 08_53_01 PM.png`          | 1024x1024   | RGB  | 2758.8KB |
| 2  | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 08_55_38 PM.png`          | 1024x1024   | RGB  | 2209.0KB |
| 3  | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 09_02_33 PM.png`          | 1024x1024   | RGB  | 1728.7KB |
| 4  | `NPC/Monsters/dragon/ChatGPT Image May 5, 2025, 09_05_42 PM.png`          | 1024x1024   | RGB  | 1856.0KB |
| 5  | `NPC/Monsters/jabato/ChatGPT Image May 26, 2025, 09_21_44 AM.png`         | 1536x1024   | RGB  | 2980.8KB |
| 6  | `NPC/Monsters/jabato/ChatGPT Image May 26, 2025, 09_28_46 AM.png`         | 1536x1024   | RGB  | 2927.1KB |
| 7  | `NPC/Monsters/jabato/ChatGPT Image May 26, 2025, 09_28_48 AM.png`         | 1536x1024   | RGB  | 2868.6KB |
| 8  | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 07_19_32 PM.png`            | 1024x1024   | RGB  | 1328.8KB |
| 9  | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_27_40 AM.png`            | 1024x1024   | RGB  | 2080.1KB |
| 10 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_30_37 AM.png`            | 1024x1024   | RGB  | 2350.9KB |
| 11 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 11_57_42 AM.png`            | 1024x1024   | RGB  | 2059.4KB |
| 12 | `NPC/Monsters/new/ChatGPT Image May 26, 2025, 12_01_32 PM.png`            | 1024x1024   | RGB  | 1854.7KB |
| 13 | `NPC/Monsters/new/Nigro/ChatGPT Image May 26, 2025, 11_08_06 AM.png`      | 1024x1024   | RGB  | 1690.8KB |
| 14 | `NPC/Monsters/new/Nigro/ChatGPT Image May 26, 2025, 11_18_06 AM.png`      | 1024x1024   | RGB  | 2025.4KB |
| 15 | `NPC/Monsters/new/mini Migro/ChatGPT Image May 26, 2025, 11_20_44 AM.png` | 1024x1024   | RGB  | 2247.2KB |
| 16 | `NPC/Monsters/new/mini Migro/ChatGPT Image May 26, 2025, 11_22_31 AM.png` | 1024x1024   | RGB  | 2298.1KB |
| 17 | `NPC/Monsters/new/squeleton/ChatGPT Image May 26, 2025, 11_16_17 AM.png`  | 1024x1024   | RGB  | 1685.9KB |

### NPC ya correctos (7 archivos — sin cambios)

| # | Archivo                                                                        | Dimensiones | Modo |
| - | ------------------------------------------------------------------------------ | ----------- | ---- |
| 1 | `NPC/Neutral/vendors/alchemist/normal/valeria_alchemist.png`                 | 256x256     | RGBA |
| 2 | `NPC/Neutral/vendors/banker/normal/Abigail_banker.png`                       | 256x256     | RGBA |
| 3 | `NPC/Neutral/vendors/blacksmith/normal/smith_blacksmith.png`                 | 256x256     | RGBA |
| 4 | `NPC/Neutral/vendors/cheff/gatita_chanchita/normal/gatitachanchita_down.png` | 256x256     | RGBA |
| 5 | `NPC/Neutral/vendors/cheff/gatita_chanchita/sexy/gatita_chanchita_down.png`  | 256x256     | RGBA |
| 6 | `NPC/Neutral/vendors/lumberjack/normal/pavel_lumberjack.png`                 | 256x256     | RGBA |
| 7 | `NPC/Neutral/vendors/mague/normal/roberto_mague.png`                         | 256x256     | RGBA |

---

## 6. BUILDINGS — Archivos a modificar

Buildings/props generados por IA a 1024x1024. Necesitan:

1. **Auto-crop** (recortar transparencia)
2. **Mantener aspect ratio** (no forzar cuadrado)
3. **Ensure RGBA** donde sea RGB

Ruta base: `_Project/Art/Buildings/`

**Total archivos a modificar: 93**
**Tamano objetivo: Auto-crop + max 256px lado mayor**

### BUILDINGS_autocrop (89 archivos)

| #  | Archivo                                                                     | Dimensiones | Modo | Tamano   |
| -- | --------------------------------------------------------------------------- | ----------- | ---- | -------- |
| 1  | `Buildings/castles/castle_1.png`                                          | 1536x1024   | RGBA | 3114.1KB |
| 2  | `Buildings/castles/castle_2.png`                                          | 1024x1024   | RGBA | 2511.5KB |
| 3  | `Buildings/combat/ChatGPT Image Apr 3, 2025, 05_12_38 AM.png`             | 1536x1024   | RGBA | 3577.9KB |
| 4  | `Buildings/combat/coliseo_2.png`                                          | 1024x1024   | RGBA | 2484.7KB |
| 5  | `Buildings/combat/training.png`                                           | 1536x1024   | RGBA | 3577.9KB |
| 6  | `Buildings/forest_decoration/corrupto/craneo_muzgo.png`                   | 1024x1024   | RGBA | 1436.7KB |
| 7  | `Buildings/forest_decoration/corrupto/flor_carnivora.png`                 | 1024x1024   | RGBA | 1412.7KB |
| 8  | `Buildings/forest_decoration/corrupto/hojas_marchitas.png`                | 1024x1024   | RGBA | 1406.6KB |
| 9  | `Buildings/forest_decoration/corrupto/hongos_liminiscentes_venenosos.png` | 1024x1024   | RGBA | 1434.9KB |
| 10 | `Buildings/forest_decoration/corrupto/monton_ramas_afiladas.png`          | 1024x1024   | RGBA | 1382.5KB |
| 11 | `Buildings/forest_decoration/corrupto/piedra_runica_musgosa.png`          | 1024x1024   | RGBA | 1717.3KB |
| 12 | `Buildings/forest_decoration/corrupto/pozo_savia_corrupta.png`            | 1024x1024   | RGBA | 1351.1KB |
| 13 | `Buildings/forest_decoration/corrupto/raiz_retorcida.png`                 | 1024x1024   | RGBA | 1410.6KB |
| 14 | `Buildings/forest_decoration/corrupto/semilla_reventada.png`              | 1024x1024   | RGBA | 1401.7KB |
| 15 | `Buildings/forest_decoration/corrupto/totem_podrido.png`                  | 1024x1024   | RGBA | 1430.6KB |
| 16 | `Buildings/forest_decoration/natural/Flor_silvestre_azul.png`             | 1024x1024   | RGBA | 1364.5KB |
| 17 | `Buildings/forest_decoration/natural/champinones_agrupados_marrones.png`  | 1024x1024   | RGBA | 1368.3KB |
| 18 | `Buildings/forest_decoration/natural/hojas_frescas.png`                   | 1024x1024   | RGBA | 1526.8KB |
| 19 | `Buildings/forest_decoration/natural/mariposa rama caida.png`             | 1024x1024   | RGBA | 1351.7KB |
| 20 | `Buildings/forest_decoration/natural/nido_huevos.png`                     | 1024x1024   | RGBA | 1493.4KB |
| 21 | `Buildings/forest_decoration/natural/petalos_rosados_1.png`               | 1024x1024   | RGBA | 1352.2KB |
| 22 | `Buildings/forest_decoration/natural/petalos_rosados_2.png`               | 1024x1024   | RGBA | 1346.1KB |
| 23 | `Buildings/forest_decoration/natural/petalos_rosados_3.png`               | 1024x1024   | RGBA | 1340.1KB |
| 24 | `Buildings/forest_decoration/natural/piedras_musgo.png`                   | 1024x1024   | RGBA | 1481.3KB |
| 25 | `Buildings/forest_decoration/natural/raiz_expuesta_suave.png`             | 1024x1024   | RGBA | 846.7KB  |
| 26 | `Buildings/forest_decoration/natural/seta_blanca.png`                     | 1024x1024   | RGBA | 1343.2KB |
| 27 | `Buildings/gardens/ChatGPT Image Sep 17, 2025, 12_27_43 AM.png`           | 1024x1024   | RGBA | 1767.8KB |
| 28 | `Buildings/gardens/flowers_1.PNG`                                         | 412x169     | RGBA | 126.3KB  |
| 29 | `Buildings/gardens/flowers_2.PNG`                                         | 412x412     | RGBA | 226.0KB  |
| 30 | `Buildings/gardens/flowers_3.PNG`                                         | 462x451     | RGBA | 254.8KB  |
| 31 | `Buildings/gardens/flowers_4.PNG`                                         | 174x444     | RGBA | 140.3KB  |
| 32 | `Buildings/gardens/flowers_5.PNG`                                         | 904x904     | RGBA | 777.2KB  |
| 33 | `Buildings/gardens/flowers_6.PNG`                                         | 904x904     | RGBA | 803.1KB  |
| 34 | `Buildings/gardens/flowers_7.PNG`                                         | 904x904     | RGBA | 776.9KB  |
| 35 | `Buildings/gardens/flowers_8.PNG`                                         | 444x174     | RGBA | 138.9KB  |
| 36 | `Buildings/gardens/flowers_9.PNG`                                         | 169x412     | RGBA | 129.0KB  |
| 37 | `Buildings/gardens/garden_1.png`                                          | 1024x1024   | RGBA | 2684.4KB |
| 38 | `Buildings/gardens/garden_2.png`                                          | 1024x1024   | RGBA | 2750.7KB |
| 39 | `Buildings/gardens/garden_3.png`                                          | 1024x1024   | RGBA | 2808.6KB |
| 40 | `Buildings/gardens/garden_4.png`                                          | 1024x1024   | RGBA | 2783.7KB |
| 41 | `Buildings/gardens/garden_5.png`                                          | 1024x1024   | RGBA | 2215.5KB |
| 42 | `Buildings/houses/curse_house_iso.png`                                    | 1024x1024   | RGBA | 1786.5KB |
| 43 | `Buildings/houses/curse_house_topdown.png`                                | 1024x1024   | RGBA | 1710.8KB |
| 44 | `Buildings/houses/orden_house_2.png`                                      | 1024x1024   | RGBA | 1746.7KB |
| 45 | `Buildings/mine.png`                                                      | 1024x1024   | RGBA | 1770.6KB |
| 46 | `Buildings/others/Portal wow.png`                                         | 1024x1024   | RGBA | 1598.8KB |
| 47 | `Buildings/others/fuente.png`                                             | 1024x1024   | RGBA | 1935.2KB |
| 48 | `Buildings/others/guillotina.png`                                         | 1024x1024   | RGBA | 1568.1KB |
| 49 | `Buildings/portals/ChatGPT Image Sep 11, 2025, 10_25_29 PM.png`           | 1024x1024   | RGBA | 1741.8KB |
| 50 | `Buildings/portals/ChatGPT Image Sep 11, 2025, 10_27_21 PM.png`           | 1024x1024   | RGBA | 1668.1KB |
| 51 | `Buildings/portals/ChatGPT Image Sep 11, 2025, 10_29_09 PM.png`           | 1024x1024   | RGBA | 1732.2KB |
| 52 | `Buildings/portals/ChatGPT Image Sep 11, 2025, 10_30_45 PM.png`           | 1024x1024   | RGBA | 1732.2KB |
| 53 | `Buildings/portals/portal.png`                                            | 1024x1536   | RGBA | 2867.5KB |
| 54 | `Buildings/portals/portal_2.png`                                          | 1024x1536   | RGBA | 2238.6KB |
| 55 | `Buildings/portals/portal_dudgeon_1.png`                                  | 1024x1024   | RGBA | 1373.2KB |
| 56 | `Buildings/portals/portal_dudgeon_2.png`                                  | 1024x1024   | RGBA | 1448.2KB |
| 57 | `Buildings/shops/ChatGPT Image Apr 3, 2025, 05_11_07 AM.png`              | 1024x1536   | RGBA | 3586.1KB |
| 58 | `Buildings/shops/ChatGPT Image Apr 3, 2025, 05_11_17 AM.png`              | 1024x1024   | RGBA | 2393.4KB |
| 59 | `Buildings/shops/ChatGPT Image Apr 3, 2025, 05_11_32 AM.png`              | 1024x1024   | RGBA | 2594.5KB |
| 60 | `Buildings/shops/alchemy_tower.png`                                       | 1024x1536   | RGBA | 2779.8KB |
| 61 | `Buildings/shops/banco.png`                                               | 1024x1536   | RGBA | 3604.5KB |
| 62 | `Buildings/shops/blacksmith.png`                                          | 1024x1024   | RGBA | 1599.1KB |
| 63 | `Buildings/shops/healer.png`                                              | 1024x1024   | RGBA | 1720.7KB |
| 64 | `Buildings/shops/healer_1.png`                                            | 1024x1024   | RGBA | 1612.6KB |
| 65 | `Buildings/shops/jewlery_shop.png`                                        | 1024x1024   | RGBA | 2096.4KB |
| 66 | `Buildings/shops/magic_tower.png`                                         | 1024x1536   | RGBA | 3152.3KB |
| 67 | `Buildings/shops/ukranian_super_1.png`                                    | 1024x1024   | RGBA | 2169.3KB |
| 68 | `Buildings/shops/ukranian_super_2.png`                                    | 1024x1536   | RGBA | 2966.4KB |
| 69 | `Buildings/statues/ChatGPT Image May 1, 2025, 11_04_33 AM.png`            | 1024x1024   | RGBA | 1778.6KB |
| 70 | `Buildings/statues/statue_olim_01.png`                                    | 1024x1024   | RGBA | 1765.6KB |
| 71 | `Buildings/temples/ChatGPT Image Apr 3, 2025, 05_11_11 AM.png`            | 1024x1024   | RGBA | 2021.1KB |
| 72 | `Buildings/temples/ChatGPT Image Apr 3, 2025, 05_11_14 AM.png`            | 1024x1024   | RGBA | 2184.7KB |
| 73 | `Buildings/temples/catholic.png`                                          | 1024x1536   | RGBA | 3286.0KB |
| 74 | `Buildings/temples/satanist.png`                                          | 1024x1024   | RGBA | 2449.7KB |
| 75 | `Buildings/totems/totem_cargando_energia.png`                             | 1024x1536   | RGBA | 2399.9KB |
| 76 | `Buildings/totems/totem_destruido.png`                                    | 1024x1536   | RGBA | 2693.7KB |
| 77 | `Buildings/totems/totem_forest.png`                                       | 1024x1536   | RGBA | 2242.9KB |
| 78 | `Buildings/totems/totem_riendo.png`                                       | 1024x1536   | RGBA | 2138.0KB |
| 79 | `Buildings/totems/totem_sufriendo.png`                                    | 1024x1536   | RGBA | 2373.7KB |
| 80 | `Buildings/vegetation/tree_1.PNG`                                         | 456x626     | RGBA | 383.6KB  |
| 81 | `Buildings/vegetation/tree_10.PNG`                                        | 251x324     | RGBA | 62.6KB   |
| 82 | `Buildings/vegetation/tree_11.png`                                        | 233x333     | RGBA | 11.0KB   |
| 83 | `Buildings/vegetation/tree_2.PNG`                                         | 456x626     | RGBA | 366.7KB  |
| 84 | `Buildings/vegetation/tree_3.PNG`                                         | 490x627     | RGBA | 387.7KB  |
| 85 | `Buildings/vegetation/tree_6.png`                                         | 540x540     | RGBA | 19.0KB   |
| 86 | `Buildings/vegetation/tree_7.PNG`                                         | 227x309     | RGBA | 54.2KB   |
| 87 | `Buildings/vegetation/tree_8.PNG`                                         | 251x308     | RGBA | 54.0KB   |
| 88 | `Buildings/vegetation/tree_9.PNG`                                         | 251x324     | RGBA | 59.9KB   |
| 89 | `Buildings/vegetation/tree_azul.png`                                      | 1024x1024   | RGBA | 1695.7KB |

### BUILDINGS_autocrop_rgba (4 archivos)

| # | Archivo                                                          | Dimensiones | Modo | Tamano   |
| - | ---------------------------------------------------------------- | ----------- | ---- | -------- |
| 1 | `Buildings/backgrounds/background lobby.png`                   | 1536x1024   | RGB  | 2443.1KB |
| 2 | `Buildings/combat/coliseo.png`                                 | 1024x1024   | RGB  | 2764.3KB |
| 3 | `Buildings/statues/ChatGPT Image May 1, 2025, 11_04_43 AM.png` | 1024x1024   | RGB  | 1859.8KB |
| 4 | `Buildings/temples/ChatGPT Image May 5, 2025, 09_14_51 PM.png` | 1024x1024   | RGB  | 1788.9KB |

### BUILDINGS ya correctos (3 archivos — sin cambios)

- `Buildings/dummy.png` [1x1 RGBA 0.1KB]
- `Buildings/vegetation/tree_4.png` [96x192 RGBA 3.6KB]
- `Buildings/vegetation/tree_5.png` [256x256 RGBA 15.5KB]

---

## 7. ITEMS — Archivos a modificar

Iconos de inventario a 1024x1024 que se renderizan a ~32-64px.
Resize a **128x128** para atlas eficiente.

Ruta base: `_Project/Art/Items/`

**Total archivos a modificar: 48**
**Tamano objetivo: 128x128 RGBA**

### ITEMS_resize_to_64 (47 archivos)

| #  | Archivo                                       | Dimensiones | Modo | Tamano   |
| -- | --------------------------------------------- | ----------- | ---- | -------- |
| 1  | `Items/Alchemy/energy_potion.png`           | 1024x1024   | RGBA | 1670.8KB |
| 2  | `Items/Alchemy/explosion_potion.png`        | 1024x1024   | RGBA | 1630.6KB |
| 3  | `Items/Alchemy/health_potion.png`           | 1024x1024   | RGBA | 1510.2KB |
| 4  | `Items/Alchemy/mana_potion.png`             | 1024x1024   | RGBA | 1437.3KB |
| 5  | `Items/Alchemy/poison_potion.png`           | 1024x1024   | RGBA | 1765.0KB |
| 6  | `Items/Cook/borsh_01.png`                   | 1024x1024   | RGBA | 1537.7KB |
| 7  | `Items/Cook/completo_chileno_01.png`        | 1024x1024   | RGBA | 1501.5KB |
| 8  | `Items/Cook/food_chicken.png`               | 1024x1024   | RGBA | 1412.5KB |
| 9  | `Items/Cook/hakarl_01.png`                  | 1024x1024   | RGBA | 1587.0KB |
| 10 | `Items/Cook/paella_01.png`                  | 1024x1024   | RGBA | 1498.4KB |
| 11 | `Items/Cook/perogi_01.png`                  | 1024x1024   | RGBA | 1402.5KB |
| 12 | `Items/Cook/tortilla_spain_01.png`          | 1024x1024   | RGBA | 1488.0KB |
| 13 | `Items/Mining/iron_ingot.png`               | 1024x1024   | RGBA | 1195.5KB |
| 14 | `Items/backpack_close.png`                  | 1024x1536   | RGBA | 1910.4KB |
| 15 | `Items/backpack_open.png`                   | 1024x1536   | RGBA | 2057.6KB |
| 16 | `Items/bank/chestbox.png`                   | 1024x1024   | RGBA | 1407.1KB |
| 17 | `Items/bank/gold_coin_stack_1.png`          | 1024x1024   | RGBA | 1446.1KB |
| 18 | `Items/bank/gold_coin_stack_2.png`          | 1024x1024   | RGBA | 1592.2KB |
| 19 | `Items/blacksmith/iron_sword.png`           | 1024x1024   | RGBA | 1364.9KB |
| 20 | `Items/bucket.png`                          | 1024x1024   | RGBA | 1410.9KB |
| 21 | `Items/bucket_water.png`                    | 1024x1024   | RGBA | 1428.7KB |
| 22 | `Items/decorations/lamp_decoration.png`     | 1024x1024   | RGBA | 1462.5KB |
| 23 | `Items/decorations/mushrooms_deocation.png` | 1024x1024   | RGBA | 1394.7KB |
| 24 | `Items/decorations/rock_mod_decoration.png` | 1024x1024   | RGBA | 1385.4KB |
| 25 | `Items/decorations/rocks_decoration.png`    | 1024x1024   | RGBA | 1463.4KB |
| 26 | `Items/decorations/shovel_decoration.png`   | 1024x1024   | RGBA | 1405.7KB |
| 27 | `Items/empty_wooden_box.png`                | 1024x1024   | RGBA | 1494.8KB |
| 28 | `Items/experience/exp_orb_1.png`            | 1024x1024   | RGBA | 1459.4KB |
| 29 | `Items/experience/exp_orb_2.png`            | 1024x1024   | RGBA | 1463.5KB |
| 30 | `Items/experience/exp_orb_3.png`            | 1024x1024   | RGBA | 1340.6KB |
| 31 | `Items/experience/exp_orb_4.png`            | 1024x1024   | RGBA | 1341.8KB |
| 32 | `Items/image_item_not_found.png`            | 1024x1024   | RGBA | 2050.3KB |
| 33 | `Items/keg.png`                             | 1024x1024   | RGBA | 1416.8KB |
| 34 | `Items/lumberjack/arrow_01.png`             | 1024x1024   | RGBA | 1460.6KB |
| 35 | `Items/lumberjack/bow_01.png`               | 1024x1024   | RGBA | 1403.1KB |
| 36 | `Items/lumberjack/wood_log_bundle.png`      | 1024x1024   | RGBA | 1436.7KB |
| 37 | `Items/magic/spellbook_simple.png`          | 1024x1024   | RGBA | 1606.4KB |
| 38 | `Items/magic/wizard_staff_lvl_1.png`        | 1024x1024   | RGBA | 1687.6KB |
| 39 | `Items/magic/wizard_staff_lvl_2.png`        | 1024x1024   | RGBA | 1818.5KB |
| 40 | `Items/magic/wizard_staff_lvl_3.png`        | 1024x1536   | RGBA | 3452.6KB |
| 41 | `Items/metal_bucket.png`                    | 1024x1024   | RGBA | 1505.5KB |
| 42 | `Items/npcs/ancient_relic_mask.png`         | 1024x1024   | RGBA | 1151.8KB |
| 43 | `Items/rock_in_bag.png`                     | 1024x1024   | RGBA | 1522.3KB |
| 44 | `Items/shields/simple_wooden_shield.png`    | 1024x1024   | RGBA | 1731.1KB |
| 45 | `Items/sign.png`                            | 1024x1024   | RGBA | 1512.8KB |
| 46 | `Items/torch.png`                           | 1024x1024   | RGBA | 1391.5KB |
| 47 | `Items/unlit_campfire.png`                  | 1024x1024   | RGBA | 1478.4KB |

### ITEMS_resize64_rgba (1 archivos)

| # | Archivo                          | Dimensiones | Modo | Tamano   |
| - | -------------------------------- | ----------- | ---- | -------- |
| 1 | `Items/coal_in_wooden_box.png` | 1024x1024   | RGB  | 2386.5KB |

---

## 8. UI — Archivos a modificar

Iconos de editor/gameplay a 1024x1024 o 1536x1024.

- **Iconos de herramientas:** resize a 128x128
- **Backgrounds/intros:** mantener tamano (no van en atlas)
- **Todos los RGB:** convertir a RGBA

Ruta base: `_Project/Art/UI/`

**Total archivos a modificar: 122**
**Tamano objetivo: 128x128 RGBA (iconos) / mantener (backgrounds)**

### UI_resize (50 archivos)

| #  | Archivo                                                           | Dimensiones | Modo | Tamano   |
| -- | ----------------------------------------------------------------- | ----------- | ---- | -------- |
| 1  | `UI/add_building.png`                                           | 1024x1024   | RGBA | 1401.6KB |
| 2  | `UI/add_building_on_system.png`                                 | 1024x1024   | RGBA | 1369.4KB |
| 3  | `UI/add_entitie.png`                                            | 1024x1024   | RGBA | 926.4KB  |
| 4  | `UI/add_entity_on_system.png`                                   | 1024x1024   | RGBA | 1160.4KB |
| 5  | `UI/add_item.png`                                               | 1024x1024   | RGBA | 1260.9KB |
| 6  | `UI/add_item_on_system.png`                                     | 1024x1024   | RGBA | 1468.9KB |
| 7  | `UI/add_spell.png`                                              | 1024x1024   | RGBA | 1418.8KB |
| 8  | `UI/add_zone.png`                                               | 1024x1024   | RGBA | 1362.9KB |
| 9  | `UI/arrow_left.png`                                             | 600x403     | RGBA | 18.9KB   |
| 10 | `UI/building_manager_icon.png`                                  | 1024x1024   | RGBA | 1464.7KB |
| 11 | `UI/buildings_colliders.png`                                    | 1024x1024   | RGBA | 1597.7KB |
| 12 | `UI/collision_tool.png`                                         | 1024x1024   | RGBA | 1530.1KB |
| 13 | `UI/default_icon.png`                                           | 1024x1024   | RGBA | 1437.0KB |
| 14 | `UI/delete_icon.png`                                            | 1024x1024   | RGBA | 1424.3KB |
| 15 | `UI/delete_zone.png`                                            | 1024x1024   | RGBA | 1460.8KB |
| 16 | `UI/entities_on_map_icon.png`                                   | 1024x1024   | RGBA | 1560.8KB |
| 17 | `UI/entities_on_system_icon.png`                                | 1024x1024   | RGBA | 1462.8KB |
| 18 | `UI/folder_win.png`                                             | 800x800     | RGBA | 123.2KB  |
| 19 | `UI/fsm_editor/graph_panel/connect_node.png`                    | 1024x1024   | RGBA | 1384.5KB |
| 20 | `UI/fsm_editor/graph_panel/select_node.png`                     | 1024x1024   | RGBA | 1386.3KB |
| 21 | `UI/icon.png`                                                   | 1024x1024   | RGBA | 1371.4KB |
| 22 | `UI/intro/game_name.png`                                        | 1024x453    | RGBA | 627.2KB  |
| 23 | `UI/items_on_map_icon.png`                                      | 1024x1024   | RGBA | 1465.5KB |
| 24 | `UI/particles_editor/add_remove_panel/particles_add.png`        | 1024x1024   | RGBA | 1400.8KB |
| 25 | `UI/particles_editor/add_remove_panel/particles_add_system.png` | 1024x1024   | RGBA | 1489.3KB |
| 26 | `UI/particles_editor/add_remove_panel/particles_remove.png`     | 1024x1024   | RGBA | 1401.3KB |
| 27 | `UI/particles_editor/particles_configuration.png`               | 1024x1024   | RGBA | 1376.4KB |
| 28 | `UI/particles_editor/particles_no_view.png`                     | 1024x1024   | RGBA | 1345.7KB |
| 29 | `UI/particles_editor/particles_view.png`                        | 1024x1024   | RGBA | 1352.7KB |
| 30 | `UI/particles_editor/toolbar/particles_list.png`                | 1024x1024   | RGBA | 1342.6KB |
| 31 | `UI/particles_editor/toolbar/particles_reset.png`               | 1024x1024   | RGBA | 1343.9KB |
| 32 | `UI/pintar_colliders_zone.png`                                  | 1024x1024   | RGBA | 1928.4KB |
| 33 | `UI/pintar_tiles_zone.png`                                      | 1024x1024   | RGBA | 1478.5KB |
| 34 | `UI/redo.png`                                                   | 1024x1024   | RGBA | 1467.7KB |
| 35 | `UI/remove_building.png`                                        | 1024x1024   | RGBA | 1449.4KB |
| 36 | `UI/remove_entitie.png`                                         | 1024x1024   | RGBA | 933.4KB  |
| 37 | `UI/remove_item.png`                                            | 1024x1024   | RGBA | 1368.5KB |
| 38 | `UI/remove_spell.png`                                           | 1024x1024   | RGBA | 1357.3KB |
| 39 | `UI/respawn.png`                                                | 1024x1024   | RGBA | 1549.2KB |
| 40 | `UI/spawner_editor/spawner_add.png`                             | 1024x1024   | RGBA | 1219.0KB |
| 41 | `UI/spawner_editor/spawner_list.png`                            | 1024x1024   | RGBA | 1232.6KB |
| 42 | `UI/spawner_editor/spawner_manager.png`                         | 1024x1024   | RGBA | 1208.6KB |
| 43 | `UI/spawner_editor/spawner_no_view.png`                         | 1024x1024   | RGBA | 1283.7KB |
| 44 | `UI/spawner_editor/spawner_remove.png`                          | 1024x1024   | RGBA | 1248.9KB |
| 45 | `UI/spawner_editor/spawner_view.png`                            | 1024x1024   | RGBA | 1282.2KB |
| 46 | `UI/spells_on_map_icon.png`                                     | 1024x1024   | RGBA | 1559.9KB |
| 47 | `UI/tutorials_button.png`                                       | 1024x1024   | RGBA | 1398.4KB |
| 48 | `UI/undo.png`                                                   | 1024x1024   | RGBA | 1477.4KB |
| 49 | `UI/vaciar_colliders_zone.png`                                  | 1024x1024   | RGBA | 1545.7KB |
| 50 | `UI/vaciar_tiles_zone.png`                                      | 1024x1024   | RGBA | 1604.7KB |

### UI_resize_rgba (72 archivos)

| #  | Archivo                                                     | Dimensiones | Modo | Tamano   |
| -- | ----------------------------------------------------------- | ----------- | ---- | -------- |
| 1  | `UI/ChatGPT Image 2 jun 2025, 12_09_43.png`               | 1024x1024   | RGB  | 1496.9KB |
| 2  | `UI/background_ini.png`                                   | 1536x1024   | RGB  | 2591.1KB |
| 3  | `UI/background_ini_2.png`                                 | 1536x1024   | RGB  | 2324.8KB |
| 4  | `UI/background_ini_old.png`                               | 1536x1024   | RGB  | 2865.0KB |
| 5  | `UI/brush_tool.PNG`                                       | 1024x1024   | RGB  | 1518.2KB |
| 6  | `UI/character_selection/barbarian_vs_dragon.png`          | 1536x1024   | RGB  | 3098.1KB |
| 7  | `UI/character_selection/character_selection_01.png`       | 1536x1024   | RGB  | 2438.1KB |
| 8  | `UI/character_selection/character_selection_barbrian.png` | 1536x1024   | RGB  | 2356.8KB |
| 9  | `UI/character_selection/character_selection_drwaft.png`   | 1536x1024   | RGB  | 2410.4KB |
| 10 | `UI/character_selection/character_selection_elve.png`     | 1536x1024   | RGB  | 2361.0KB |
| 11 | `UI/character_selection/character_selection_mague.png`    | 1536x1024   | RGB  | 2368.7KB |
| 12 | `UI/character_selection/character_selection_valkyrie.png` | 1536x1024   | RGB  | 2387.9KB |
| 13 | `UI/character_selection/characters_dungeon_stop.png`      | 1536x1024   | RGB  | 2332.9KB |
| 14 | `UI/character_selection/characters_tabern.png`            | 1536x1024   | RGB  | 2213.7KB |
| 15 | `UI/character_selection/characters_tabern_2.png`          | 1536x1024   | RGB  | 2054.2KB |
| 16 | `UI/character_selection/characters_tabern_3.png`          | 1536x1024   | RGB  | 2604.2KB |
| 17 | `UI/character_selection/characters_tabern_4.png`          | 1536x1024   | RGB  | 2362.8KB |
| 18 | `UI/character_selection/characters_tabern_5.png`          | 1536x1024   | RGB  | 2586.8KB |
| 19 | `UI/character_selection/characters_tabern_6.png`          | 1536x1024   | RGB  | 2401.6KB |
| 20 | `UI/character_selection/characters_tabern_7.png`          | 1536x1024   | RGB  | 2343.7KB |
| 21 | `UI/character_selection/characters_tabern_stop.png`       | 1536x1024   | RGB  | 2201.1KB |
| 22 | `UI/character_selection/dwraft_vs_dragon.png`             | 1536x1024   | RGB  | 2708.5KB |
| 23 | `UI/character_selection/elven_play_guitar.png`            | 1536x1024   | RGB  | 2401.9KB |
| 24 | `UI/character_selection/elven_play_guitar_concert.png`    | 1024x1536   | RGB  | 2460.9KB |
| 25 | `UI/character_selection/elven_vs_dragon.png`              | 1536x1024   | RGB  | 2563.4KB |
| 26 | `UI/character_selection/elven_vs_dragon_2.png`            | 1536x1024   | RGB  | 2633.6KB |
| 27 | `UI/character_selection/intro_castle_afternoon.png`       | 1536x1024   | RGB  | 2606.5KB |
| 28 | `UI/character_selection/intro_castle_morning.png`         | 1536x1024   | RGB  | 2759.0KB |
| 29 | `UI/character_selection/intro_castle_morning_2.png`       | 1536x1024   | RGB  | 3134.1KB |
| 30 | `UI/character_selection/intro_castle_night.png`           | 1536x1024   | RGB  | 2377.9KB |
| 31 | `UI/character_selection/mague_orden_chaos.png`            | 1024x1024   | RGB  | 1850.2KB |
| 32 | `UI/character_selection/mague_orden_chaos_2.png`          | 1536x1024   | RGB  | 2512.4KB |
| 33 | `UI/character_selection/mague_vs_dragon.png`              | 1536x1024   | RGB  | 3174.4KB |
| 34 | `UI/character_selection/mague_vs_dragon_2.png`            | 1536x1024   | RGB  | 3119.4KB |
| 35 | `UI/character_selection/mague_vs_dragon_3.png`            | 1536x1024   | RGB  | 3158.0KB |
| 36 | `UI/character_selection/taberna.png`                      | 1536x1024   | RGB  | 2259.4KB |
| 37 | `UI/character_selection/valkyrie_dragon.png`              | 1536x1024   | RGB  | 2540.6KB |
| 38 | `UI/character_selection/valkyrie_dragon_2.png`            | 1536x1024   | RGB  | 2853.9KB |
| 39 | `UI/character_selection/valkyrie_dragon_3.png`            | 1536x1024   | RGB  | 2608.9KB |
| 40 | `UI/character_selection/valkyrie_vs_dragon.png`           | 1536x1024   | RGB  | 2661.8KB |
| 41 | `UI/dash_icon.png`                                        | 1024x1024   | RGB  | 960.8KB  |
| 42 | `UI/eyedropper_tool.PNG`                                  | 1024x1024   | RGB  | 1431.3KB |
| 43 | `UI/firework_icon.png`                                    | 1024x1024   | RGB  | 941.7KB  |
| 44 | `UI/fsm_editor/graph_panel/add_node.png`                  | 1024x1024   | RGB  | 963.7KB  |
| 45 | `UI/fsm_editor/graph_panel/clone_node.png`                | 1024x1024   | RGB  | 821.5KB  |
| 46 | `UI/fsm_editor/graph_panel/delete_node.png`               | 1024x1024   | RGB  | 963.1KB  |
| 47 | `UI/fsm_editor/graph_panel/disconnect_node.png`           | 1024x1024   | RGB  | 743.9KB  |
| 48 | `UI/fsm_editor/graph_panel/end_node.png`                  | 1024x1024   | RGB  | 862.5KB  |
| 49 | `UI/fsm_editor/graph_panel/start_node.png`                | 1024x1024   | RGB  | 824.7KB  |
| 50 | `UI/fsm_editor/graph_panel/zoom_in.png`                   | 1024x1024   | RGB  | 1081.3KB |
| 51 | `UI/fsm_editor/graph_panel/zoom_out.png`                  | 1024x1024   | RGB  | 889.1KB  |
| 52 | `UI/fsm_editor/tool_panel/set_assigment_animations.png`   | 1024x1024   | RGB  | 779.6KB  |
| 53 | `UI/fsm_editor/tool_panel/set_assigment_entities.png`     | 1024x1024   | RGB  | 771.8KB  |
| 54 | `UI/fsm_editor/tool_panel/set_properties.png`             | 1024x1024   | RGB  | 811.5KB  |
| 55 | `UI/fsm_editor/tool_panel/sets_list.png`                  | 1024x1024   | RGB  | 736.0KB  |
| 56 | `UI/generic_icon.png`                                     | 1024x1024   | RGB  | 1175.1KB |
| 57 | `UI/intro/Intro_barbarian.png`                            | 1024x1024   | RGB  | 1876.3KB |
| 58 | `UI/intro/Intro_drwaft.png`                               | 1536x1024   | RGB  | 2884.1KB |
| 59 | `UI/intro/Intro_elven.png`                                | 1536x1024   | RGB  | 2516.3KB |
| 60 | `UI/intro/Intro_valkyrie.png`                             | 1536x1024   | RGB  | 2727.4KB |
| 61 | `UI/intro/intro_mague.png`                                | 1536x1024   | RGB  | 2479.4KB |
| 62 | `UI/layers_view_tool.png`                                 | 1024x1024   | RGB  | 1419.7KB |
| 63 | `UI/lightning_icon.png`                                   | 1024x1024   | RGB  | 986.0KB  |
| 64 | `UI/loading_create_dungeon.png`                           | 1536x1024   | RGB  | 2268.1KB |
| 65 | `UI/pixel_fire_icon.png`                                  | 1024x1024   | RGB  | 1011.5KB |
| 66 | `UI/restore_icon.png`                                     | 1024x1024   | RGB  | 1702.0KB |
| 67 | `UI/select_tool.PNG`                                      | 1024x1024   | RGB  | 1552.9KB |
| 68 | `UI/shield_icon.png`                                      | 1024x1024   | RGB  | 993.5KB  |
| 69 | `UI/slash_icon.png`                                       | 1024x1024   | RGB  | 975.8KB  |
| 70 | `UI/smoke_icon.png`                                       | 1024x1024   | RGB  | 978.9KB  |
| 71 | `UI/teleport_icon.png`                                    | 1024x1024   | RGB  | 1122.1KB |
| 72 | `UI/view_tool.png`                                        | 1024x1024   | RGB  | 1057.4KB |

---

## 9. SPELLS — Archivos a modificar

Sprites de hechizos a 1024x1024. Resize a **128x128**.

Ruta base: `_Project/Art/Spells/`

**Total archivos a modificar: 18**
**Tamano objetivo: 128x128 RGBA**

### SPELLS_resize_to_128 (17 archivos)

| #  | Archivo                                       | Dimensiones | Modo | Tamano   |
| -- | --------------------------------------------- | ----------- | ---- | -------- |
| 1  | `Spells/Projectiles/darkball.png`           | 1024x1024   | RGBA | 1344.8KB |
| 2  | `Spells/Projectiles/fireball.png`           | 1024x1024   | RGBA | 1357.2KB |
| 3  | `Spells/Projectiles/iceball.png`            | 1024x1024   | RGBA | 1397.6KB |
| 4  | `Spells/Projectiles/lightball.png`          | 1024x1024   | RGBA | 1368.2KB |
| 5  | `Spells/Projectiles/rockball.png`           | 1024x1024   | RGBA | 1593.8KB |
| 6  | `Spells/boomerang/iron_axe.png`             | 1024x1024   | RGBA | 1411.5KB |
| 7  | `Spells/boomerang/iron_boomerang.png`       | 1024x1024   | RGBA | 1384.6KB |
| 8  | `Spells/meteor/meteor.png`                  | 1024x1024   | RGBA | 1457.1KB |
| 9  | `Spells/meteor/meteor_impact.png`           | 1024x1024   | RGBA | 1461.6KB |
| 10 | `Spells/root_whip/root_whip_1.png`          | 1024x1024   | RGBA | 1431.0KB |
| 11 | `Spells/root_whip/root_whip_2.png`          | 1024x1024   | RGBA | 1425.3KB |
| 12 | `Spells/root_whip/root_whip_3.png`          | 1024x1024   | RGBA | 1435.2KB |
| 13 | `Spells/shield/shield.png`                  | 1024x1024   | RGBA | 1511.0KB |
| 14 | `Spells/traps/regular_trap.png`             | 1024x1024   | RGBA | 1402.5KB |
| 15 | `Spells/walls/ice_wall/large_ice_wall.png`  | 1536x1024   | RGBA | 2184.1KB |
| 16 | `Spells/walls/ice_wall/medium_ice_wall.png` | 1024x1024   | RGBA | 1457.3KB |
| 17 | `Spells/walls/ice_wall/small_ice_wall.png`  | 1536x1024   | RGBA | 2192.5KB |

### SPELLS_resize128_rgba (1 archivos)

| # | Archivo                              | Dimensiones | Modo | Tamano   |
| - | ------------------------------------ | ----------- | ---- | -------- |
| 1 | `Spells/Projectiles/waterball.png` | 1024x1024   | RGB  | 2165.6KB |

---

## 10. VFX — Archivos a modificar

Explosiones sobredimensionadas o con extension mixta (.PNG).

Ruta base: `_Project/Art/VFX/`

**Total archivos a modificar: 5**
**Tamano objetivo: 256x256 RGBA (o 128x128)**

### VFX_resize (5 archivos)

| # | Archivo                            | Dimensiones | Modo | Tamano   |
| - | ---------------------------------- | ----------- | ---- | -------- |
| 1 | `VFX/Explosions/explosion.png`   | 1024x1024   | RGBA | 1717.6KB |
| 2 | `VFX/Explosions/explosion_0.PNG` | 475x475     | RGBA | 47.5KB   |
| 3 | `VFX/Explosions/explosion_1.PNG` | 475x475     | RGBA | 338.2KB  |
| 4 | `VFX/Explosions/explosion_2.PNG` | 475x459     | RGBA | 338.0KB  |
| 5 | `VFX/Explosions/explosion_3.PNG` | 475x475     | RGBA | 285.6KB  |

### VFX ya correctos (224 archivos — particulas 256x256 RGBA, sin cambios)

---

## 11. MISC — Revision manual

| # | Archivo                                          | Dimensiones | Modo | Tamano   |
| - | ------------------------------------------------ | ----------- | ---- | -------- |
| 1 | `Misc/objects/arrow_left.png`                  | 600x403     | RGBA | 18.9KB   |
| 2 | `Misc/objects/folder_win.png`                  | 800x800     | RGBA | 123.2KB  |
| 3 | `Misc/objects/rock.png`                        | 64x64       | RGBA | 3.1KB    |
| 4 | `Misc/views/Top_Landscape.png`                 | 1024x128    | RGBA | 14.3KB   |
| 5 | `Misc/views/horizonte_1.png`                   | 1536x1024   | RGB  | 2241.2KB |
| 6 | `Sprites/Placeholders/monster_placeholder.png` | 32x32       | RGBA | 0.1KB    |
| 7 | `Sprites/Placeholders/player_placeholder.png`  | 32x32       | RGBA | 0.1KB    |

---

## 12. Resumen de Acciones por Tipo

| Accion                             | Archivos      | Descripcion                     |
| ---------------------------------- | ------------- | ------------------------------- |
| **BUILDINGS_autocrop**       | **89**  | **Requiere modificacion** |
| **BUILDINGS_autocrop_rgba**  | **4**   | **Requiere modificacion** |
| BUILDINGS_ok                       | 3             | Sin cambios                     |
| CHARACTERS_ok                      | 16            | Sin cambios                     |
| **ITEMS_resize64_rgba**      | **1**   | **Requiere modificacion** |
| **ITEMS_resize_to_64**       | **47**  | **Requiere modificacion** |
| **MISC_review**              | **7**   | **Requiere modificacion** |
| **NPC_autocrop_resize**      | **73**  | **Requiere modificacion** |
| **NPC_autocrop_resize_rgba** | **17**  | **Requiere modificacion** |
| NPC_ok                             | 7             | Sin cambios                     |
| **SPELLS_resize128_rgba**    | **1**   | **Requiere modificacion** |
| **SPELLS_resize_to_128**     | **17**  | **Requiere modificacion** |
| ~~TILES_resize_48_to_32~~         | 216           | ✅ Completado                   |
| ~~TILES_resize_64_to_32~~         | 159           | ✅ Completado                   |
| ~~TILES_slice_tileset~~           | 29            | ✅ Completado                   |
| ~~TILES_upscale_16_to_32~~        | 20            | ✅ Completado                   |
| **UI_resize**                | **50**  | **Requiere modificacion** |
| **UI_resize_rgba**           | **72**  | **Requiere modificacion** |
| VFX_ok                             | 224           | Sin cambios                     |
| **VFX_resize**               | **5**   | **Requiere modificacion** |
| **TOTAL CAMBIOS**            | **807** |                                 |
| TOTAL OK                           | 250           |                                 |

---

## 13. Pipeline de Normalizacion

> **REGLA FUNDAMENTAL:** Solo se modifican archivos en `unity/Valkur/Assets/_Project/Art/`.
> Los archivos de `python/assets/` NO se tocan.

### Herramienta: Script Python con Pillow

```bash
pip install Pillow
python scripts/normalize_unity_assets.py --dry-run    # Ver plan sin ejecutar
python scripts/normalize_unity_assets.py --execute     # Ejecutar normalizacion
python scripts/normalize_unity_assets.py --validate    # Validar resultados
```

### Operaciones por tipo:

| Operacion         | Descripcion                                        | Categorias afectadas          |
| ----------------- | -------------------------------------------------- | ----------------------------- |
| `resize`        | Escalar a tamano objetivo (NEAREST para pixel art) | Tiles, Items, UI, Spells, VFX |
| `auto_crop`     | Recortar transparencia con `getbbox()`           | NPC, Buildings                |
| `ensure_rgba`   | Convertir RGB->RGBA (detectar fondo por esquinas)  | NPC, Buildings, UI, Items     |
| `slice_tileset` | Cortar en grid 32x32, descartar vacios             | Tiles (tilesets)              |
| `upscale`       | Escalar 16x16->32x32 con NEAREST                   | Tiles (rock_grass)            |

### Orden recomendado (por fases):

1. **Fase 2a — Tiles** (424 archivos): Critico para Tilemap
2. **Fase 2b — NPC + Characters** (90 archivos): Critico para gameplay
3. **Fase 2c — Items** (48 archivos): Critico para inventario
4. **Fase 2d — UI + Spells + VFX + Buildings** (~215 archivos): Complementario

---

## 14. Grupos de SpriteAtlas para Unity

| Atlas                   | Contenido                        | Sprites | Max Texture | Formato                 |
| ----------------------- | -------------------------------- | ------- | ----------- | ----------------------- |
| `Atlas_Tiles_Ground`  | floor, grass, sand, dirt (32x32) | ~300    | 2048x2048   | RGBA32, Point, Pad=2    |
| `Atlas_Tiles_Dungeon` | dungeon, wall (32x32)            | ~200    | 2048x2048   | RGBA32, Point, Pad=2    |
| `Atlas_Characters`    | Player + NPC frames (128x128)    | ~100    | 4096x4096   | RGBA32, Point, Pad=2    |
| `Atlas_Items`         | Item icons (64x64)               | ~50     | 512x512     | RGBA32, Point, Pad=2    |
| `Atlas_UI`            | HUD/toolbar icons (128x128)      | ~80     | 2048x2048   | RGBA32, Bilinear, Pad=2 |
| `Atlas_VFX`           | Particles + explosions (256x256) | ~230    | 4096x4096   | RGBA32, Bilinear, Pad=0 |
| `Atlas_Spells`        | Projectiles + spells (128x128)   | ~20     | 1024x1024   | RGBA32, Point, Pad=2    |

---

## 15. Resultados de Normalizacion — TILES

> Ejecutado el 2025-02-22 con `python/scripts/normalize_tiles.py`

### Resumen

| Metrica               | Antes                                                                     | Despues                             |
| --------------------- | ------------------------------------------------------------------------- | ----------------------------------- |
| Archivos              | 564 (originales)                                                          | **12,401** tiles individuales |
| Tamano total          | 28.8 MB                                                                   | **24.3 MB** (-33.8%)          |
| Dimensiones           | 16x16, 32x32, 48x48, 64x64, 300x300, 864x880, 896x896, 960x384, 1024x1024 | **32x32 (100%)**              |
| Modo color            | RGB(9) + RGBA(555)                                                        | **RGBA (100%)**               |
| Duplicados eliminados | —                                                                        | 5,534                               |

### Operaciones realizadas

| Operacion      | Archivos                           | Descripcion                        |
| -------------- | ---------------------------------- | ---------------------------------- |
| resize 48→32  | 216                                | Nearest-neighbor downscale         |
| resize 64→32  | 159                                | Nearest-neighbor downscale         |
| upscale 16→32 | 20                                 | Nearest-neighbor upscale           |
| slice tilesets | 29 → 17,400 slices                | Grid 32x32, descartando vacios     |
| dedup          | -5,534                             | SHA-256 hash, eliminando identicos |
| ensure RGBA    | 0 (ya convertidos en resize/slice) | —                                 |

### Distribucion final por carpeta

| Carpeta                                  | Tiles            |
| ---------------------------------------- | ---------------- |
| `Tiles/` (root: dungeon, floor slices) | 10,026           |
| `Tiles/multi_tiles/`                   | 1,337            |
| `Tiles/ready/`                         | 312              |
| `Tiles/tileset_1/`                     | 726              |
| **TOTAL**                          | **12,401** |

### Backups

Todos los archivos originales estan respaldados en:
`unity/Valkur/Assets/_Project/Art/Tiles/_backups/`

### Validacion

```
ALL TILES PASS VALIDATION 
OK: 12,401 tiles are 32x32 RGBA
```

### Siguiente paso: Crear SpriteAtlas en Unity

Con todos los tiles normalizados a 32x32 RGBA, se pueden crear los SpriteAtlas:

1. Abrir Unity → Window → 2D → Sprite Atlas
2. Crear `Atlas_Tiles_Ground.spriteatlas` para tiles de terreno
3. Crear `Atlas_Tiles_Dungeon.spriteatlas` para tiles de dungeon
4. Configurar: Max Texture Size=4096, Format=RGBA32, Filter=Point, Padding=2
5. Arrastrar carpetas de tiles correspondientes al atlas

---

*Documento generado automaticamente por `python/scripts/generate_atlas_doc.py`
a partir de `unity_asset_audit.json` (1197 archivos, 644.8 MB).
Actualizado con resultados de normalizacion de tiles (12,401 tiles, 24.3 MB).*
