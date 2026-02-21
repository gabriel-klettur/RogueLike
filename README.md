# RogueLike Monorepo

Este repositorio ahora esta dividido en:

- `python/`: proyecto actual en Python/Pygame (codigo, assets, data, tests).
- `unity/`: espacio para la nueva version en Unity.

## Flujo de trabajo

Para trabajar en la version Python, usa `python/` como raiz del proyecto:

```powershell
cd python
python -m venv .venv
.\.venv\Scripts\Activate
pip install -e .
pip install -r requirements.txt
python launcher.py
```

Para tests:

```powershell
cd python
pytest -q
```

La carpeta `unity/` queda preparada para iniciar la migracion del juego.
