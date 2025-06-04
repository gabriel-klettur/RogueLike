{{...}}
## Eventos de Entrada/Salida

- **OnEnter**: Invocado al entrar en un estado. Inicializa animaciones y variables necesarias.
- **OnExit**: Invocado al salir de un estado. Limpia efectos o finaliza animaciones.
- **OnPlayerDetected**: Cuando `DistanciaJugador <= detectionRange`.
- **OnOutOfRange**: Cuando `DistanciaJugador > detectionRange`.
- **OnLowHealth**: Cuando `VidaActual/MaxVida <= fleeThreshold`.
- **OnDeathTimerExpired**: Cuando expira el temporizador de muerte (`DeathTimer`).