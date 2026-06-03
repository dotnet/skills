"""Tasks API package."""

from .app import create_app
from .models import Tag, Task, TaskPriority, TaskStatus
from .queries import TaskPage, TaskQuery, TaskQueryError, apply_query
from .repository import InMemoryTaskRepository, TaskRepository, normalize_tags
from .repository_sqlite import SqliteTaskRepository
from .service import TaskNotFoundError, TaskService

__all__ = [
    "create_app",
    "InMemoryTaskRepository",
    "SqliteTaskRepository",
    "TaskRepository",
    "TaskService",
    "TaskNotFoundError",
    "Task",
    "TaskStatus",
    "TaskPriority",
    "Tag",
    "TaskQuery",
    "TaskPage",
    "TaskQueryError",
    "apply_query",
    "normalize_tags",
]
