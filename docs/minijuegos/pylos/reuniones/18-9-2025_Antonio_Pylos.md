# Documento de preparación – Reunión inicial sobre desarrollo de *Pylos*

## 1. Objetivo de la reunión (desarrollado)
- **Conocer la visión de Antonio**: entender si busca un producto mínimo viable (MVP) o un juego completo con extras (animaciones, IA avanzada, multilenguaje).  
- **Aclarar reglas, alcance y prioridades**: confirmar si la implementación será fiel a las reglas oficiales de Pylos o simplificada.
- Como determinar los niveles.  
- **Entender expectativas**: qué considera Antonio un “entregable válido”, qué tanto peso tiene la estética vs. jugabilidad.  
- **Generar confianza**: demostrar preparación, mostrar un plan inicial y flexibilidad para adaptarse.  

### 1.1 Plan inicial
- El lunes presentaré a Antonio una **planificación completa del proyecto**, organizada en semanas con hitos concretos y entregables.  
- Este plan incluirá: lógica del juego, IA, interfaz gráfica, animaciones, empaquetado y documentación.  
- Durante esa presentación, le pediré que **detalle o discuta**:  
  - Qué reglas y variantes del juego considera imprescindibles.  
  - El nivel de detalle deseado en la interfaz gráfica y animaciones.  
  - La complejidad de la IA que espera (básica, intermedia, avanzada).  
  - Plataformas y formas de distribución prioritarias.  
- El objetivo es **alinear expectativas y prioridades** antes de empezar el desarrollo técnico.  

---

## 2. Preguntas clave para Antonio (desarrolladas)
- **Juego y reglas**  
  - ¿Quiere la implementación oficial completa de *Pylos* (19 reglas oficiales) o una versión reducida?  
  - ¿Le interesa incluir variantes como un tablero más pequeño (3x3) para partidas rápidas?  
  - ¿Habrá un límite de tiempo por turno o será libre, como en el juego físico?  

- **Interfaz y experiencia de usuario**  
  - ¿Prefiere que el juego tenga resolución fija o que se adapte (pantalla completa y ventanas redimensionables)?  
  - ¿Desea una interfaz básica funcional con los assets de pixel art o prefiere extras visuales (efectos, partículas)?  
  - ¿Le interesa añadir sonido (colocar esferas, victoria, derrota) y animaciones simples para dar dinamismo?  
  - Como quiere o imaginas determinar los niveles de las fichas.

- **Inteligencia Artificial**  
  - ¿Cuántos niveles de dificultad necesita? (por ejemplo, fácil con jugadas aleatorias, medio con heurísticas, difícil con MinMax y poda alfa-beta).  
  - ¿Prefiere que la IA responda rápido aunque menos inteligente, o que sea más fuerte aunque tarde más en decidir?  

- **Plataformas y distribución**  
  - ¿En qué contexto se usará el juego digital de Pylos? ¿Es un encargo personal, educativo o comercial?
  - ¿Solo versión para PC (Windows/Linux/Mac) o también una versión web (Flask/HTML5) para jugar en navegador?  
  - ¿Prefiere que se entregue solo el código fuente documentado, o también ejecutables empaquetados listos para usar (PyInstaller)?  
  - ¿Cómo quiere manejar los derechos/licencia del código (exclusivos para él o compartidos)?  

- **Colaboración y comunicación**  
  - ¿Le parece útil recibir entregas semanales jugables para revisar y dar feedback?  
  - ¿Cómo será el proceso de validación? (él prueba y comenta, o necesita un sistema de testeo más formal).  

- **Idioma del proyecto**  
  - ¿Prefiere que el **código** (nombres de clases, variables, comentarios) esté en inglés (estándar internacional) o en español?  
  - ¿En qué idioma quiere la **documentación y manuales**: español, inglés u otro?  

- **Coste y Pagos**
  - ¿Qué método de pago prefieres y qué condiciones propones?

---

## 3. Plan de trabajo tentativo (4 semanas, desarrollado)
- **Semana 1**: implementar tablero (4 niveles), colocación básica de esferas, validación de movimientos, modo Jugador vs. Jugador.  
- **Semana 2**: completar lógica de reglas (movimiento de esferas, detección de cuadrados, recuperación de esferas, victoria), añadir IA básica.  
- **Semana 3**: IA avanzada con MinMax + poda alfa-beta, niveles de dificultad, primeras animaciones y HUD con información de turno/esferas.  
- **Semana 4**: optimización, corrección de bugs, empaquetado del juego (ejecutable + código), documentación completa.  

*(Plan flexible que se ajustará a lo que Antonio priorice).*  

---

## 4. Rango de precios, facturación y valor estimado (desarrollado)
- La oferta inicial es de **400 – 1.200 €**.  
- Con la planificación de 50-60 horas de trabajo, el precio máximo de 1.200 € da una tarifa efectiva de **20–24 €/h**.  
- Es aceptable como proyecto de portafolio o primer cliente, pero bajo comparado con el mercado (un rango más justo sería 30-45 €/h).  
- Recomendación: negociar para estar lo más cerca posible del tope del rango si el proyecto incluye IA avanzada y extras visuales.  
- **Con respecto a las entregas, propongo que en cada entrega se facture una parte proporcional del proyecto (total: 1.200 €):**  
  - **Semana 1**: 200 € (implementación base).  
  - **Semana 2**: 300 € (motor completo de reglas + IA básica).  
  - **Semana 3**: 400 € (IA avanzada + animaciones).  
  - **Semana 4**: 300 € (optimización, empaquetado y documentación).  

---

## 5. Mensaje final (desarrollado)
> “Mi idea es trabajar de manera ágil, con entregas semanales jugables que me permitan recoger tu feedback e ir ajustando prioridades. Quiero confirmar contigo qué nivel de detalle esperas en las reglas, qué tan fuerte debe ser la IA, en qué plataformas te interesa distribuir el juego y cuál es tu presupuesto máximo realista. De esta forma, nos aseguramos de que ambos estemos alineados desde el inicio y el proyecto avance sin sorpresas.”
