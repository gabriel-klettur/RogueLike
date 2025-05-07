from setuptools import setup, find_packages

setup(
    name="Roguelike powa",
    version="0.1.0",
    description="La matrix de los Roguelikes",
    author="Beelzebot",
    package_dir={"": "src"},
    packages=find_packages(where="src"),
    install_requires=[
        "pygame",
        "tcod",
        "pyyaml",
        "miniupnpc>=2.2",
        "websocket-client>=1.5",
        "websockets>=10.4",
        "aiortc>=1.9.0",
    ],
    entry_points={
        "console_scripts": [
            # Lanza tu juego con el comando `roguelike`
            "roguelike=launcher:main",
        ],
    },
)
