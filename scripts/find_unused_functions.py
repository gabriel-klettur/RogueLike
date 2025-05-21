#!/usr/bin/env python3
import ast, os, sys

if len(sys.argv) != 2:
    print("Uso: python find_unused_functions.py <directorio_raiz>")
    sys.exit(1)

root_dir = sys.argv[1]
defs = {}
calls = set()

for dirpath, _, filenames in os.walk(root_dir):
    for filename in filenames:
        if not filename.endswith(".py"):
            continue
        path = os.path.join(dirpath, filename)
        try:
            with open(path, 'r', encoding='utf-8') as f:
                tree = ast.parse(f.read(), filename=path)
        except Exception:
            continue

        for node in ast.walk(tree):
            if isinstance(node, ast.FunctionDef):
                defs.setdefault(node.name, []).append((path, node.lineno))
            elif isinstance(node, ast.Call):
                func = node.func
                if isinstance(func, ast.Name):
                    calls.add(func.id)
                elif isinstance(func, ast.Attribute):
                    calls.add(func.attr)

unused = []
for name, locations in defs.items():
    if name.startswith("_"):
        continue
    if name not in calls:
        for path, lineno in locations:
            unused.append(f"{path}:{lineno} -> {name}")

if unused:
    print("Funciones definidas pero no utilizadas:")
    for line in sorted(unused):
        print(line)
else:
    print("No se encontraron funciones no utilizadas.")
