"""Business-logic service for tasks."""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Callable, List, Optional

from .models import Task, TaskStatus
from .repository import TaskRepository


class TaskNotFoundError(Exception):
    """Raised when an operation targets a task id that does not exist."""


class TaskService:
    """Coordinates task lifecycle operations on top of a TaskRepository."""

    def __init__(
        self,
        repository: TaskRepository,
        now: Optional[Callable[[], datetime]] = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def create(self, title: str) -> Task:
        if not isinstance(title, str):
            raise ValueError("title must be a string")
        if not title or not title.strip():
            raise ValueError("title must not be empty")
        if len(title) > 200:
            raise ValueError("title must be 200 characters or fewer")

        task_id = self._repository.next_id()
        task = Task(id=task_id, title=title.strip(), status=TaskStatus.PENDING, created_at=self._now())
        self._repository.add(task)
        return task

    def get(self, task_id: int) -> Task:
        task = self._repository.get(task_id)
        if task is None:
            raise TaskNotFoundError(f"Task {task_id} not found")
        return task

    def list_all(self) -> List[Task]:
        return list(self._repository.list())

    def list_pending(self) -> List[Task]:
        return [t for t in self._repository.list() if t.status == TaskStatus.PENDING]

    def complete(self, task_id: int) -> Task:
        task = self.get(task_id)
        if task.status == TaskStatus.DONE:
            raise ValueError(f"Task {task_id} is already done")
        task.status = TaskStatus.DONE
        task.completed_at = self._now()
        self._repository.update(task)
        return task
