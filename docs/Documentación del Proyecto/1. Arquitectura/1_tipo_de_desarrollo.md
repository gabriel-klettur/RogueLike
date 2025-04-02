# 🛠️ Tipo de Desarrollo – RogueLike Top-Down

## ✅ Enfoque General

Este proyecto adopta un modelo **Iterativo-Incremental libre**, altamente adaptable para equipos pequeños, juegos experimentales y desarrollo sin presión de fechas.

> ⚠️ No se utiliza un enfoque con fechas fijas o Scrum clásico.
> Se prioriza el avance por funcionalidad, en un ambiente creativo y flexible.


---

## 🔁 Metodología: Iterativo-Incremental + Kanban libre

| Elemento              | Descripción                                                                 |
|-----------------------|------------------------------------------------------------------------------|
| **Base metodológica** | Iterativo-Incremental                                                        |
| **Adaptación**        | Sin sprints, sin deadlines.                                                  |
| **Herramienta**       | GitHub Projects (Kanban)                                                    |
| **Ciclo por Feature** | Investigación → Diseño → Desarrollo → Pruebas → Documentación → Integración |


---

## 🎯 Cómo trabajamos

### 🎨 Creatividad libre, con estructura clara
- Cada desarrollador puede elegir qué funcionalidad desarrollar.
- Se trabaja en ramas independientes (`feature/`, `bugfix/`, etc.).
- Se documenta cada avance para facilitar la colaboración.

### 📋 Tablero Kanban (GitHub Projects)

Columnas recomendadas:
- `💡 Ideas`
- `🛠️ To Do`
- `🚧 In Progress`
- `✅ Review`
- `🏁 Done`

Cada tarjeta (issue) representa una funcionalidad completa.

---

## 🔂 Ciclo de Desarrollo de Funcionalidades

Cada funcionalidad debe pasar por las siguientes etapas:

1. **Investigación** (solo si hay dudas o falta de inspiración)
2. **Especificación funcional**
3. **Diseño técnico**
4. **Diseño visual (si aplica)**
5. **Desarrollo**
6. **Pruebas (manuales o automatizadas)**
7. **Documentación**
8. **Pull request + revisión + merge a `develop`**

> 📌 Ver documento `docs/Guias internas de Equipo/CONTRIBUTING.md` para el flujo exacto de colaboración.


---

## 🧪 Testing sin presión
- Se prioriza testing manual al principio.
- Se agregan tests automatizados a medida que la arquitectura madura.
- Toda lógica importante debe tener una forma de ser testeada.


---

## 📂 Estructura Modular
El proyecto está organizado por carpetas separadas por dominios (`core/`, `map/`, `entities/`, `network/`, etc.) para favorecer la independencia y facilidad de testeo o refactor.

> 📌 Ver `4_estructura_proyecto.md` para entender la arquitectura física del código.


---

## 🤝 Rol del Equipo

| Dev         | Funciones                                                                 |
|-------------|---------------------------------------------------------------------------|
| Gaby        | Arquitectura, desarrollo, diseño, documentación.                         |
| Mistico     | Desarrollo, testing, diseño técnico, puto amo de la IA, documentacion.           |

Ambos tienen capacidad para:
- Crear funcionalidades desde cero.
- Revisar código del otro.
- Mejorar documentación.


---
