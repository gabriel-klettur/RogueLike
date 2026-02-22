**FSM para NPC de Acción Roguelike**

Este documento propone una Máquina de Estados Finitos (FSM) para controlar el comportamiento de un NPC que:

* Patrulla en el escenario.
* Detecta al jugador y lo persigue.
* Ataca cuando está en rango.
* Puede huir si tiene poca vida (aleatorio).
* Puede ejecutar esquivas aleatorias durante el combate.

---

### 1. Definición de Estados

| Estado     | Descripción                                       |
| ---------- | ------------------------------------------------- |
| **Patrol** | Recorre waypoints de patrulla.                    |
| **Chase**  | Persigue al jugador tras detectarlo.              |
| **Attack** | Ataca si el jugador está en rango de ataque.      |
| **Flee**   | Huye cuando la vida está por debajo de un umbral. |
| **Dodge**  | Realiza una maniobra de esquiva rápida.           |
| **Dead**   | Estado terminal; reproduce animación de muerte.   |

---

### 2. Sensores y Variables Globales

* **DistanciaJugador**: distancia actual al jugador.
* **VidaActual**: vida restante del NPC.
* **UmbralHuida**: porcentaje de vida bajo el cual puede decidir huir.
* **ChanceHuir**: probabilidad (0–1) de decidir huir al estar bajo el umbral.
* **ChanceEsquivar**: probabilidad de ejecutar una esquiva como acción reactiva.
* **RangoDeteccion**: distancia a la que detecta al jugador.
* **RangoAtaque**: distancia a la que puede atacar.

---

### 3. Transiciones Principales

```text
Patrol
  └─(DistanciaJugador <= RangoDeteccion)─────────> Chase

Chase
  ├─(DistanciaJugador <= RangoAtaque)────────────> Attack
  └─(DistanciaJugador > RangoDeteccion)──────────> Patrol

Attack
  ├─(VidaActual/MaxVida <= UmbralHuida && random() < ChanceHuir)──> Flee
  ├─(random() < ChanceEsquivar)─────────────────────────────> Dodge
  ├─(DistanciaJugador > RangoAtaque && DistanciaJugador <= RangoDeteccion)─> Chase
  └─(JugadorMuerto)────────────────────────────────────────> Patrol

Flee
  └─(DistanciaJugador > RangoDeteccion)──────────> Patrol

Dodge
  └─(esquiva completada)──────────────────────────> Attack (o Chase si fuera de rango)

*En cualquier estado:*
  └─(VidaActual <= 0)────────────────────────────> Dead
```

---

### 4. Estructura de Código (Pseudocódigo)

```csharp
public enum State { Patrol, Chase, Attack, Dodge, Flee, Dead }

public class NPCController : MonoBehaviour {
    State currentState = State.Patrol;
    void Update() {
        Sense();
        Decide();
        Act();
    }

    void Sense() {
        DistanciaJugador = Vector3.Distance(transform.position, player.position);
        VidaActual = health.Current;
    }

    void Decide() {
        switch (currentState) {
            case State.Patrol:
                if (DistanciaJugador <= RangoDeteccion) currentState = State.Chase;
                break;
            case State.Chase:
                if (DistanciaJugador <= RangoAtaque) currentState = State.Attack;
                else if (DistanciaJugador > RangoDeteccion) currentState = State.Patrol;
                break;
            case State.Attack:
                if (VidaActual/MaxVida <= UmbralHuida && Random.value < ChanceHuir) currentState = State.Flee;
                else if (Random.value < ChanceEsquivar) currentState = State.Dodge;
                else if (DistanciaJugador > RangoAtaque) currentState = State.Chase;
                break;
            case State.Dodge:
                // espera a completar animación antes de decidir
                break;
            case State.Flee:
                if (DistanciaJugador > RangoDeteccion) currentState = State.Patrol;
                break;
        }
        if (VidaActual <= 0) currentState = State.Dead;
    }

    void Act() {
        switch (currentState) {
            case State.Patrol: PatrolBehavior(); break;
            case State.Chase: ChaseBehavior(); break;
            case State.Attack: AttackBehavior(); break;
            case State.Dodge: DodgeBehavior(); break;
            case State.Flee: FleeBehavior(); break;
            case State.Dead: DeadBehavior(); break;
        }
    }
}
```

---

### 5. Puntos de Personalización

* **Tiempos de permanencia** en cada estado (ej.: cooldown tras huir).
* **Distintos tipos de esquiva**: lateral, hacia atrás, con invulnerabilidad parcial.
* **Modularidad**: dividir `Decide()` y `Act()` en componentes independientes para reutilizarlos.

---

Con esta base, podemos iterar sobre ajustes de probabilidades, añadir subestados (HFSM) o integrar comportamientos más avanzados (BT/Utility) para escalabilidad.
