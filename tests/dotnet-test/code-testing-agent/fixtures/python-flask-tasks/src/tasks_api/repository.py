"""Repository abstraction for tasks."""

from __future__ import annotations

from typing import Iterable, Optional, Protocol

from .models import Task


class TaskRepository(Protocol):
    def add(self, task: Task) -> None: ...
    def get(self, task_id: int) -> Optional[Task]: ...
    def list(self) -> Iterable[Task]: ...
    def update(self, task: Task) -> None: ...
    def next_id(self) -> int: ...


class InMemoryTaskRepository:
    def __init__(self) -> None:
        self._tasks: dict[int, Task] = {}
        self._next_id = 1

    def add(self, task: Task) -> None:
        self._tasks[task.id] = task

    def get(self, task_id: int) -> Optional[Task]:
        return self._tasks.get(task_id)

    def list(self) -> Iterable[Task]:
        return list(self._tasks.values())

    def update(self, task: Task) -> None:
        if task.id not in self._tasks:
            raise KeyError(task.id)
        self._tasks[task.id] = task

    def next_id(self) -> int:
        value = self._next_id
        self._next_id += 1
        return value
