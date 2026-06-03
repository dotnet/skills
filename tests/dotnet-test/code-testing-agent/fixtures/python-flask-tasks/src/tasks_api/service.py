"""Business-logic service for tasks."""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Callable, Iterable, List, Optional

from .models import Tag, Task, TaskPriority, TaskStatus
from .queries import TaskPage, TaskQuery, apply_query
from .repository import TaskRepository, normalize_tags


class TaskNotFoundError(Exception):
    """Raised when an operation targets a task id that does not exist."""


class TaskService:
    """Coordinates task lifecycle operations on top of a :class:`TaskRepository`."""

    def __init__(
        self,
        repository: TaskRepository,
        now: Optional[Callable[[], datetime]] = None,
        max_title_length: int = 200,
    ) -> None:
        if max_title_length <= 0:
            raise ValueError("max_title_length must be positive")
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))
        self._max_title_length = max_title_length

    # ------------------------------------------------------------------ create
    def create(
        self,
        title: str,
        *,
        priority: TaskPriority = TaskPriority.MEDIUM,
        due_at: Optional[datetime] = None,
        tags: Optional[Iterable[Tag | str]] = None,
    ) -> Task:
        title = self._validate_title(title)
        if not isinstance(priority, TaskPriority):
            raise ValueError("priority must be a TaskPriority value")
        due_at = self._validate_due_at(due_at)
        normalized_tags = normalize_tags(tags or [])

        task_id = self._repository.next_id()
        task = Task(
            id=task_id,
            title=title,
            status=TaskStatus.PENDING,
            priority=priority,
            due_at=due_at,
            created_at=self._now(),
            tags=normalized_tags,
        )
        self._repository.add(task)
        return task

    # --------------------------------------------------------------------- get
    def get(self, task_id: int) -> Task:
        task = self._repository.get(task_id)
        if task is None:
            raise TaskNotFoundError(f"Task {task_id} not found")
        return task

    # -------------------------------------------------------------------- list
    def list_all(self) -> List[Task]:
        return list(self._repository.list())

    def query(self, query: TaskQuery) -> TaskPage:
        return apply_query(self._repository.list(), query, self._now())

    # ----------------------------------------------------------------- mutate
    def complete(self, task_id: int) -> Task:
        task = self.get(task_id)
        if task.status == TaskStatus.DONE:
            raise ValueError(f"Task {task_id} is already done")
        task.status = TaskStatus.DONE
        task.completed_at = self._now()
        self._repository.update(task)
        return task

    def reopen(self, task_id: int) -> Task:
        task = self.get(task_id)
        if task.status == TaskStatus.PENDING:
            raise ValueError(f"Task {task_id} is already pending")
        task.status = TaskStatus.PENDING
        task.completed_at = None
        self._repository.update(task)
        return task

    def delete(self, task_id: int) -> None:
        if not self._repository.delete(task_id):
            raise TaskNotFoundError(f"Task {task_id} not found")

    def set_priority(self, task_id: int, priority: TaskPriority) -> Task:
        if not isinstance(priority, TaskPriority):
            raise ValueError("priority must be a TaskPriority value")
        task = self.get(task_id)
        task.priority = priority
        self._repository.update(task)
        return task

    def set_due_at(self, task_id: int, due_at: Optional[datetime]) -> Task:
        due_at = self._validate_due_at(due_at)
        task = self.get(task_id)
        task.due_at = due_at
        self._repository.update(task)
        return task

    # ------------------------------------------------------------------- tags
    def add_tags(self, task_id: int, tags: Iterable[Tag | str]) -> Task:
        task = self.get(task_id)
        task.tags = normalize_tags(list(task.tags) + list(tags))
        self._repository.update(task)
        return task

    def remove_tag(self, task_id: int, name: str) -> Task:
        target = Tag(name).name
        task = self.get(task_id)
        before = len(task.tags)
        task.tags = [t for t in task.tags if t.name != target]
        if len(task.tags) == before:
            raise ValueError(f"Tag {target!r} is not attached to task {task_id}")
        self._repository.update(task)
        return task

    # ---------------------------------------------------------------- helpers
    def _validate_title(self, title: str) -> str:
        if not isinstance(title, str):
            raise ValueError("title must be a string")
        stripped = title.strip()
        if not stripped:
            raise ValueError("title must not be empty")
        if len(stripped) > self._max_title_length:
            raise ValueError(
                f"title must be {self._max_title_length} characters or fewer"
            )
        return stripped

    def _validate_due_at(self, due_at: Optional[datetime]) -> Optional[datetime]:
        if due_at is None:
            return None
        if not isinstance(due_at, datetime):
            raise ValueError("due_at must be a datetime")
        if due_at.tzinfo is None:
            raise ValueError("due_at must be timezone-aware")
        return due_at.astimezone(timezone.utc)
