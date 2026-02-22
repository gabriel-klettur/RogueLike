"""
Script de diagnóstico para verificar que el Tiles Editor usa el mundo correcto.

Ejecutar después de teleportarse a final_boss_barbol:
    python scripts/test_tiles_editor_world.py
"""
import sys
from pathlib import Path

# Añadir src al path
sys.path.insert(0, str(Path(__file__).parent.parent / 'src'))

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.worlds.service import world_service

def test_world_configuration():
    """Verifica que la configuración del mundo sea correcta."""
    print("\n" + "="*60)
    print("DIAGNÓSTICO: Configuración de Mundo")
    print("="*60)
    
    # 1. Current world
    current_world = getattr(global_map_settings, 'current_world', 'UNKNOWN')
    print(f"✓ current_world = {current_world}")
    
    # 2. Overlays directory
    overlays_dir = getattr(global_map_settings, 'overlays_dir', None)
    print(f"✓ overlays_dir = {overlays_dir}")
    
    if overlays_dir:
        overlays_path = Path(overlays_dir)
        if overlays_path.exists():
            overlay_files = list(overlays_path.glob('*.overlay.json'))
            print(f"  └─ Archivos de overlay: {len(overlay_files)}")
            for f in overlay_files:
                size_kb = f.stat().st_size / 1024
                print(f"     • {f.name} ({size_kb:.1f} KB)")
        else:
            print(f"  └─ ⚠️ Directorio NO EXISTE")
    
    # 3. Zones index
    zones_index = getattr(global_map_settings, 'ZONES_INDEX', None)
    print(f"✓ ZONES_INDEX = {zones_index}")
    
    if zones_index:
        zones_path = Path(zones_index)
        if zones_path.exists():
            with open(zones_path, 'r', encoding='utf-8') as f:
                zones_content = f.read().strip()
                import json
                zones_data = json.loads(zones_content) if zones_content else {}
                print(f"  └─ Zonas definidas: {list(zones_data.keys())}")
        else:
            print(f"  └─ ⚠️ Archivo NO EXISTE")
    
    # 4. Collisions directory
    collisions_dir = getattr(global_map_settings, 'collisions_dir', None)
    print(f"✓ collisions_dir = {collisions_dir}")
    
    # 5. Buildings directory
    buildings_dir = getattr(global_map_settings, 'buildings_dir', None)
    print(f"✓ buildings_dir = {buildings_dir}")
    
    # 6. World service current
    try:
        ws_current = world_service.current
        ws_world_id = getattr(ws_current, 'world_id', 'UNKNOWN')
        print(f"✓ world_service.current.world_id = {ws_world_id}")
    except Exception as e:
        print(f"✗ world_service.current error: {e}")
    
    print("="*60)
    
    # Verificar consistencia
    print("\nVERIFICACIÓN DE CONSISTENCIA:")
    
    if current_world == 'final_boss_barbol':
        print("✅ current_world es final_boss_barbol")
        if overlays_dir and 'final_boss_barbol' in str(overlays_dir):
            print("✅ overlays_dir apunta a final_boss_barbol")
        else:
            print("❌ overlays_dir NO apunta a final_boss_barbol")
            print(f"   Esperado: .../final_boss_barbol/zones/overlays")
            print(f"   Actual:   {overlays_dir}")
    else:
        print(f"⚠️ current_world NO es final_boss_barbol (es '{current_world}')")
    
    print("="*60 + "\n")


if __name__ == '__main__':
    test_world_configuration()
