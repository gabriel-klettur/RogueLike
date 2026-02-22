from __future__ import annotations

import threading
import queue
import uuid
from dataclasses import dataclass
from typing import List, Tuple, Optional

from .chat_service import ChatService, ChatJob, ChatResult


@dataclass
class WorkItem:
    job_id: str
    job: ChatJob


class ChatAsyncWorker:
    """Ejecutor asíncrono de ChatService para no bloquear el loop de Pygame.

    - Usa uno o más hilos consumidores que leen de una cola y publican los resultados
      en otra cola.
    - Diseñado como singleton para compartirse entre sistemas.
    """

    _instance: Optional["ChatAsyncWorker"] = None
    _instance_lock = threading.Lock()

    def __init__(self, max_workers: int = 2, max_queue: int = 64) -> None:
        self._in_q: queue.Queue[WorkItem] = queue.Queue(maxsize=max_queue)
        self._out_q: queue.Queue[Tuple[str, ChatResult]] = queue.Queue()
        self._stop = threading.Event()
        self._threads: List[threading.Thread] = []
        for i in range(max_workers):
            t = threading.Thread(target=self._worker_loop, name=f"ChatWorker-{i}", daemon=True)
            t.start()
            self._threads.append(t)

    @classmethod
    def instance(cls) -> "ChatAsyncWorker":
        with cls._instance_lock:
            if cls._instance is None:
                cls._instance = ChatAsyncWorker()
            return cls._instance

    def submit(self, job: ChatJob) -> str:
        """Encola un trabajo y devuelve un job_id."""
        job_id = uuid.uuid4().hex
        item = WorkItem(job_id=job_id, job=job)
        try:
            self._in_q.put_nowait(item)
        except queue.Full:
            # Si está llena, descartamos el trabajo más antiguo y luego encolamos
            try:
                self._in_q.get_nowait()
            except Exception:
                pass
            self._in_q.put_nowait(item)
        return job_id

    def poll_completed(self, max_items: int = 16) -> List[Tuple[str, ChatResult]]:
        """Extrae hasta max_items resultados disponibles sin bloquear."""
        out: List[Tuple[str, ChatResult]] = []
        for _ in range(max_items):
            try:
                out.append(self._out_q.get_nowait())
            except queue.Empty:
                break
        return out

    def _worker_loop(self) -> None:
        while not self._stop.is_set():
            try:
                item = self._in_q.get(timeout=0.2)
            except queue.Empty:
                continue
            try:
                service = ChatService()
                res = service.process(item.job)
            except Exception as e:
                # Fallback duro en caso de error, para no bloquear producción
                res = ChatResult(text=f"Error de chat: {e}", effects={}, tool_calls=[], provider="error", offline=True)
            try:
                self._out_q.put((item.job_id, res))
            except Exception:
                pass
            finally:
                try:
                    self._in_q.task_done()
                except Exception:
                    pass

    def stop(self) -> None:
        self._stop.set()
        for t in self._threads:
            try:
                t.join(timeout=0.5)
            except Exception:
                pass
