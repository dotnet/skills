"""Flask application factory."""

from __future__ import annotations

import os
from typing import Optional

from flask import Flask

from .repository import InMemoryTaskRepository, TaskRepository
from .repository_sqlite import SqliteTaskRepository
from .routes import bp as tasks_bp
from .service import TaskService


def _build_repository(backend: str, database_url: Optional[str]) -> TaskRepository:
    backend = backend.lower()
    if backend == "memory":
        return InMemoryTaskRepository()
    if backend == "sqlite":
        import sqlite3

        path = database_url or ":memory:"
        return SqliteTaskRepository(sqlite3.connect(path))
    raise ValueError(f"unknown TASKS_BACKEND: {backend!r}")


def create_app(
    service: Optional[TaskService] = None,
    *,
    config: Optional[dict] = None,
) -> Flask:
    """Build a Flask app.

    Resolution order for the repository when ``service`` is not provided:
    explicit ``config`` dict > environment variables > defaults.

    Recognized keys / vars:
      - ``TASKS_BACKEND`` (memory | sqlite, default memory)
      - ``TASKS_DATABASE_URL`` (only used when backend=sqlite)
      - ``TASKS_MAX_TITLE_LENGTH`` (int, default 200)
    """
    app = Flask(__name__)
    settings = {**os.environ, **(config or {})}
    if service is None:
        backend = settings.get("TASKS_BACKEND", "memory")
        database_url = settings.get("TASKS_DATABASE_URL")
        max_title_raw = settings.get("TASKS_MAX_TITLE_LENGTH", 200)
        try:
            max_title = int(max_title_raw)
        except (TypeError, ValueError) as exc:
            raise ValueError("TASKS_MAX_TITLE_LENGTH must be an integer") from exc
        repository = _build_repository(backend, database_url)
        service = TaskService(repository, max_title_length=max_title)
    app.config["TASK_SERVICE"] = service
    app.register_blueprint(tasks_bp)
    return app
