# Checklist de tests para FSM

A continuación se detallan los tests a implementar para cubrir la máquina de estados finita (FSM):

- [ ] Transiciones válidas: cada evento produce la transición esperada.
- [ ] Transiciones inválidas: eventos no permitidos no cambian el estado o lanzan una excepción.
- [ ] Guards (condiciones): probar escenarios donde la condición retiene o permite la transición.
- [ ] Estados temporizados: simular time-outs (DeathTimer) y verificar la transición.
- [ ] Flujo NPC: patrol → detect player → chase → attack → return.
- [ ] Flujo Player: idle → walk → run → attack → idle.
- [ ] FSM de magias: fases charge → cast → fly → impact → cooldown.
- [ ] Cancelación de magia: cancelar `cast` a mitad y verificar rollback o estado adecuado.
- [ ] Exportación gráfica: generar Graphviz y comprobar que contiene todos los nodos y aristas.
- [ ] Serialización y persistencia: guardar/recuperar estado de FSM y continuar correctamente.
- [ ] Concurrencia: instanciar 1000 FSM en paralelo y asegurar que avanzan sin errores.
- [ ] Inyección masiva de eventos: enviar un flood de eventos y confirmar que la cola se maneja.
- [ ] Logging: verificar que cada transición registra el mensaje/log esperado.
