# Mouse Input System - Solución Completa

## 🎯 Problema Resuelto

El problema del mouse se debía a:
1. **Falta de centralización**: El mouse se accedía directamente en múltiples scripts sin verificar si existía
2. **EventSystem no inicializado**: Sin EventSystem, las interacciones UI del mouse fallaban
3. **Conflicto de Input Systems**: Legacy InputManager y New Input System sin sincronización
4. **Falta de diagnostics**: No había forma de identificar qué estaba mal rápidamente

## ✅ Solución Implementada

### 1. InputDiagnostics.cs
Sistema automático que detecta y reporta problemas del input:
- Verifica si el Mouse device está disponible
- Valida que el Keyboard existe
- Comprueba que EventSystem esté configurado
- Carga ValkurInputActions asset
- Valida posición del mouse

**Uso:**
```csharp
InputDiagnostics.RunDiagnostics(); // Se ejecuta automáticamente en primer acceso
InputDiagnostics.EnsureEventSystem(); // Crea EventSystem si falta
```

### 2. MouseInputManager.cs
Centralizador de todo input del mouse con API segura:

```csharp
// Posición del mouse
Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
Vector2 worldPos = MouseInputManager.GetWorldMousePosition();

// Botones del mouse
bool leftPressed = MouseInputManager.IsLeftMouseButtonPressed();
bool wasLeftPressedThisFrame = MouseInputManager.WasLeftMouseButtonPressedThisFrame();
bool rightPressed = MouseInputManager.IsRightMouseButtonPressed();
bool midPressed = MouseInputManager.IsMiddleMouseButtonPressed();

// Rueda del mouse
float scrollDelta = MouseInputManager.GetMouseWheelDelta();

// UI
bool overUI = MouseInputManager.IsPointerOverUI();

// Raycast y colisiones
Collider2D[] under = MouseInputManager.GetCollidersUnderMouse(layerMask);
Collider2D topmost = MouseInputManager.GetTopmostColliderUnderMouse(layerMask);
RaycastHit2D hit = MouseInputManager.Raycast(direction, distance, layerMask);

// Rectángulos
bool inScreenRect = MouseInputManager.IsMouseInScreenRect(rect);
bool inWorldRect = MouseInputManager.IsMouseInWorldRect(bounds2d);

// Eventos
manager.OnMousePositionChanged += (pos) => { /* ... */ };
manager.OnLeftMouseDown += (pos) => { /* ... */ };
manager.OnLeftMouseUp += (pos) => { /* ... */ };
manager.OnRightMouseDown += (pos) => { /* ... */ };
manager.OnRightMouseUp += (pos) => { /* ... */ };
manager.OnMouseWheelScroll += (delta) => { /* ... */ };
```

**Características:**
- ✓ Null safety automática (sin crashes si no hay mouse)
- ✓ Singleton pattern (una sola instancia)
- ✓ Inicialización automática
- ✓ EventSystem asegurado
- ✓ Conversión automática screen→world coordinates
- ✓ Eventos para cambios de estado

### 3. MouseTargetDetector.cs - Mejorado
Ahora usa MouseInputManager para evitar problemas:

```csharp
// Antes (problemático):
var mouse = Mouse.current; // ¿Qué pasa si es null?
Vector2 mouseWorld = _mainCamera.ScreenToWorldPoint(mouse.position.ReadValue());

// Ahora (seguro):
Vector2 mouseWorld = MouseInputManager.GetWorldMousePosition();
```

### 4. Tests Exhaustivos

**InputDiagnosticsTests.cs** (10 tests)
- Verificar que no lance excepciones
- Validar que el mouse existe
- Verificar EventSystem creation
- Validar posiciones válidas

**MouseInputManagerTests.cs** (26 tests)
- Lectura de posición (screen y world)
- Detección de botones
- Detección de rueda
- Raycast y colisiones
- Rectángulos (screen y world)
- Eventos (subscripción)
- Singleton pattern
- Bounds2D logic

**MouseTargetDetectorTests.cs** (19 tests)
- Detección básica de targets
- Targets muertos no se detectan
- Entidades sin Health no se detectan
- Multiple collider types (Box, Circle, Polygon)
- Cambios de layers
- Entidades deshabilitadas
- Consistencia de multiple updates

**Total: 55 tests nuevos**

## 🚀 Cómo Configurar

### Setup en Editor

1. **Crear MouseInputManager en escena:**
   ```
   Botón derecho en Hierarchy → Create Empty
   Nombre: [MouseInputManager]
   Component: MouseInputManager
   ```

2. **Verificar EventSystem:**
   ```
   Asegurarse de que existe en la escena o que se crea automáticamente
   ```

3. **Usar en tus scripts:**
   ```csharp
   using Valkur.Core.Input;

   public class MyScript : MonoBehaviour {
       void Update() {
           if (MouseInputManager.IsLeftMouseButtonPressed()) {
               Vector2 pos = MouseInputManager.GetWorldMousePosition();
               // Hacer algo...
           }
       }
   }
   ```

### En GameplaySceneSetup.cs

Añadir en `EnsureServices()`:

```csharp
private MouseInputManager _mouseInputManager;

public void Start()
{
    // Otros ensures...
    EnsureMouseInputManager();
    // Otros ensures...
}

private void EnsureMouseInputManager()
{
    _mouseInputManager = FindObjectOfType<MouseInputManager>();
    if (_mouseInputManager == null)
    {
        var go = new GameObject("[MouseInputManager]");
        _mouseInputManager = go.AddComponent<MouseInputManager>();
    }
}
```

## 📋 Archivos Modificados/Creados

### Nuevos:
- `Scripts/Core/Input/InputDiagnostics.cs` - Diagnostics system
- `Scripts/Core/Input/MouseInputManager.cs` - Centralized input manager
- `Tests/EditMode/Game/Core/InputDiagnosticsTests.cs` - 10 tests
- `Tests/EditMode/Game/Core/MouseInputManagerTests.cs` - 26 tests
- `Tests/EditMode/Game/Combat/MouseTargetDetectorTests.cs` - 19 tests

### Modificados:
- `Scripts/Gameplay/Combat/MouseTargetDetector.cs` - Ahora usa MouseInputManager

## 🧪 Ejecutar Tests

```bash
# En Terminal - Full test suite
cd unity/Valkur
"C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" -runTests -testPlatform EditMode -projectPath . -logFile -

# O en Unity Editor:
# Window → TextTest Framework → Run All → Select EditMode
```

**Resultado esperado:** 55 tests nuevos + todos los tests existentes deben PASAR.

## 🐛 Debugging

Si algo no funciona, ejecuta:

```csharp
InputDiagnostics.RunDiagnostics();
```

Revisa la Console para mensajes como:
- `✓ Mouse device found` → OK
- `❌ Mouse device NOT FOUND` → Problema
- `✓ EventSystem found` → OK
- `❌ EventSystem NOT FOUND` → Necesita crear

## 📝 Best Practices

1. **Siempre usa MouseInputManager, nunca Mouse.current directamente:**
   ```csharp
   // ❌ MAL
   var mouse = Mouse.current;
   if (mouse == null) return; // Tedioso y fácil de olvidar

   // ✅ BIEN
   Vector2 pos = MouseInputManager.GetWorldMousePosition(); // Safe
   ```

2. **Subscribe a eventos en lugar de polling:**
   ```csharp
   // ❌ Menos eficiente
   if (MouseInputManager.WasLeftMouseButtonPressedThisFrame()) { }

   // ✅ Mejor
   MouseInputManager.Instance.OnLeftMouseDown += HandleClick;
   ```

3. **Usa Bounds2D para colisiones en world space:**
   ```csharp
   var bounds = new MouseInputManager.Bounds2D(-5f, 5f, -5f, 5f);
   if (MouseInputManager.IsMouseInWorldRect(bounds)) { }
   ```

4. **Raycast con layer mask correcto:**
   ```csharp
   LayerMask enemyLayers = LayerMask.GetMask("NPC") | LayerMask.GetMask("Monster");
   var colliders = MouseInputManager.GetCollidersUnderMouse(enemyLayers);
   ```

## 🔍 Troubleshooting

| Problema | Solución |
|----------|----------|
| Mouse no funciona | Ejecutar `InputDiagnostics.RunDiagnostics()` |
| UI buttons no responden | Verificar EventSystem existe (`InputDiagnostics.EnsureEventSystem()`) |
| Clicks en el vacío detectan targets | Usar `IsPointerOverUI()` para bloquear |
| Raycasts no encuentran nada | Verificar LayerMask está correcto |
| Posición del mouse es (0,0) | Asegurar que existe Camera.main |

## ✨ Beneficios

✓ **Null Safety**: Nunca vuelve a frenarse por Mouse.current null  
✓ **Centralizado**: Un solo lugar para cambiar input  
✓ **Testeable**: 55 tests nuevos + framework  
✓ **Documentado**: API clara y bien comentada  
✓ **Escalable**: Fácil de extender (gamepad, touch, etc.)  
✓ **Observable**: Eventos en lugar de polling  
✓ **Debuggable**: Sistema de diagnostics automático  

## 🎓 Arquitectura

```
MouseInputManager (Singleton)
├── GetScreenMousePosition()
├── GetWorldMousePosition()
├── Button checks (Left, Right, Middle)
├── Wheel detection
├── UI pointer check
├── Raycast/Collider queries
├── Bounds checking
└── Events (OnLeftMouseDown, OnMouseWheelScroll, etc.)
    ↓
    Input Consumers:
    ├── PlayerController
    ├── MouseTargetDetector
    ├── UI Interactions
    ├── Buildings Editor
    ├── Tile Editor
    └── ... (todos los sistemas que usen mouse)
```

## 📚 Referencias

- Input System Docs: https://docs.unity3d.com/Packages/com.unity.inputsystem@latest/
- EventSystem: https://docs.unity3d.com/Manual/EventSystem.html
