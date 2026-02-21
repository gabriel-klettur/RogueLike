"""
Parser de consola basado en shlex.
- Soporta comillas y espacios dentro de argumentos
- Provee utilidades para autocompletado básico (detección de contexto)
"""
from __future__ import annotations
import shlex
from dataclasses import dataclass
from typing import List


@dataclass
class ParseContext:
    tokens: List[str]
    ends_with_space: bool


class ConsoleParser:
    def tokenize(self, line: str) -> List[str]:
        try:
            return shlex.split(line, posix=True)
        except ValueError:
            # Línea con comillas sin cerrar: degradar a split simple
            return line.strip().split()

    def split_command(self, line: str) -> tuple[str | None, List[str]]:
        tokens = self.tokenize(line)
        if not tokens:
            return None, []
        return tokens[0], tokens[1:]

    def analyze(self, line: str) -> ParseContext:
        tokens = self.tokenize(line)
        ends_with_space = len(line) > 0 and line[-1].isspace()
        return ParseContext(tokens=tokens, ends_with_space=ends_with_space)
