# Pipeline de vídeos de gameplay generados por IA

> **Estado: IDEA DISEÑADA, NADA CONSTRUIDO.** Discutido el 2026-09-14. Este documento es el
> punto de retoma: cuando se quiera empezar, se arranca por la **Fase 1** de abajo.

## Objetivo

Escribir una petición ("vídeo de 2 minutos sobre el combate contra los Dark") y obtener un
vídeo listo para YouTube **hecho entero por IA**: guion, gameplay jugado, grabación, voz,
montaje, miniatura y metadatos. El único paso humano recomendado es **aprobar antes de
publicar**, porque publicar es irreversible y es el canal del usuario.

## Por qué es viable en Valkur

- `DevConsole.Execute(string)` es público y alcanzable desde `mcp__unity__execute_code`:
  una IA ya puede preparar escenas (`spawn`, `weather`, `learn`, hora del día, mapas).
- Sistemas **deterministas por semilla** (Seed World, `MarketCycle`): una toma se puede
  repetir idéntica cambiando solo lo que falló.
- Cada sistema ya está documentado (`CLAUDE.md`, `.github/*_AUDIT*.md`): es el guion en bruto.
- La cámara ya se mueve por un único proxy (`CameraFeelDirector`), y el input ya pasa por
  una sola capa (`InputBindingResolver`), que es donde un "jugador IA" puede inyectar.

## Decisiones tomadas

- **Todo por IA**, incluido jugar. Descartado grabar al usuario jugando.
- **No usar vídeo generado por IA** (Sora, Veo, Runway): caro, inventa gameplay que no existe
  y es engañoso para un juego. Se graba el juego real.
- **La IA no juega en directo por MCP.** Cada llamada tarda segundos y el combate saldría
  torpe. La IA planifica offline y un componente dentro de Unity ejecuta en tiempo real.
- **La IA revisa su propio vídeo** (fotogramas por toma) y regraba solo las tomas malas,
  con un máximo de 2-3 vueltas. Es lo que hace viable no tener humano en medio.
- Música: la del propio `AudioCatalog`, con ducking bajo la voz.

## Flujo

```text
Petición del usuario
 └─ Claude: lee docs del sistema -> guion + shotlist.json
     └─ Unity: VideoDirector prepara la escena -> VideoPuppet juega -> Unity Recorder graba
         └─ Claude: extrae fotogramas, comprueba cada toma -> regraba las que fallan
             └─ TTS: narración por toma (el vídeo se ajusta al audio, no al revés)
                 └─ ffmpeg / Remotion: cortes, subtítulos, títulos, música con ducking
                     └─ miniatura + título + descripción + capítulos
                         └─ [aprobación humana] -> subida por YouTube Data API
```

## Piezas a construir

| Pieza | Dónde | Qué hace |
|---|---|---|
| `shotlist.json` | `tools/video/` | Formato de tomas: comandos de DevConsole, cámara, acciones del puppet, duración, texto de narración |
| `VideoDirector` | `Scripts/Gameplay/Video/` (nuevo) | Ejecuta una shot list en Play Mode, prepara cada toma, controla la cámara por el proxy |
| `VideoPuppet` | mismo sitio | "Jugador IA" que inyecta input real (movimiento, apuntado, esquiva, hechizos) vía `InputBindingResolver` y rutas con `PathFinder`. Nunca teletransporta en pantalla |
| Grabación | Unity Recorder (paquete) | MP4 1080p60 o 4K; puede grabar más lento que tiempo real y salir fluido |
| Revisión | Claude + ffmpeg | Fotogramas por toma: ¿se ve lo que dice la narración?, ¿UI tapando?, ¿editor abierto?, ¿personaje fuera de cuadro?, ¿el hook engancha? |
| Voz | `tools/video/tts` | TTS local (Kokoro / Piper / XTTS, $0) o ElevenLabs (~$22/mes) |
| Montaje | `tools/video/` | ffmpeg o Remotion |
| Publicación | `tools/video/` | YouTube Data API, detrás de aprobación |

## Fases

1. **Cadena mínima con B-roll escenificado (1-2 semanas).** Formato `shotlist.json`,
   `VideoDirector`, Recorder, TTS, montaje con ffmpeg. Sin combate. **Primer vídeo de
   prueba: Seed World generando un mundo.** Valida la cadena entera antes del puppet.
2. **`VideoPuppet` (2-3 semanas).** Movimiento, apuntado y combate creíbles.
3. **Autorrevisión y regrabación (~1 semana).** El bucle por fotogramas.
4. **Miniaturas, metadatos y subida (~1 semana).**

## Costes estimados (precios de 2026-09, comprobar al retomar)

- **Por vídeo:** ~$3-10 (tokens de guion + revisión por visión + regrabaciones). Voz $0 en
  local. Grabación, montaje y música $0.
- **Construcción:** decenas de dólares en tokens. El coste real es el tiempo de revisar
  resultados.

## Límites y riesgos

- **El primer render no será perfecto.** Realista: 70-80 % a la primera y 1-2 rondas.
  Editar el JSON de tomas y re-renderizar tarda minutos.
- **Combate creíble, no de experto.** Suficiente para explicar sistemas; corto para
  "montajes épicos" hasta pulir el puppet.
- **Unity tiene que estar abierto y en Play Mode** mientras graba.
- **Hook de los primeros 5 segundos:** plantilla fija, primero el resultado espectacular y
  después la explicación.
- **Política de YouTube sobre contenido sintético:** revisar la vigente. Narración con voz
  IA sobre gameplay real normalmente no requiere etiqueta, pero hay que confirmarlo.
- **Una sonda que cambia estado global lo restaura en `finally`** (ver CLAUDE.md): el
  director cambia clima, hora y `timeScale`, y debe devolverlo todo al terminar. También
  debe **devolver al jugador a Pepitoria** y no persistir nada del rodaje (ni saves ni
  JSON de mundo).

## Decisiones pendientes al retomar

- Idioma de la narración: español, inglés o ambos (dos pistas de voz + subtítulos, coste
  casi nulo).
- Voz: sintética genérica o clonada.
- TTS local o de pago.
- Si se quita también la aprobación humana antes de publicar (desaconsejado al principio).
