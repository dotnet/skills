"""SQLite-backed implementation of :class:`TaskRepository`.

This implementation persists tasks (and their tags) in a SQLite database. It is
intentionally written using the standard-library ``sqlite3`` module so the
fixture has no extra dependencies. The schema is created on construction.

Use an in-memory connection (``":memory:"``) for fast, isolated tests, or a
file path for integration tests.
"""

from __future__ import annotations

import sqlite3
from datetime import datetime
from typing import Iterable, Optional

from .models import Tag, Task, TaskPriority, TaskStatus

_SCHEMA = """
CREATE TABLE IF NOT EXISTS tasks (
    id           INTEGER PRIMARY KEY,
    title        TEXT NOT NULL,
    status       TEXT NOT NULL,
    priority     TEXT NOT NULL,
    due_at       TEXT,
    completed_at TEXT,
    created_at   TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS task_tags (
    task_id INTEGER NOT NULL,
    name    TEXT NOT NULL,
    PRIMARY KEY (task_id, name),
    FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE
);
"""


def _iso(value: Optional[datetime]) -> Optional[str]:
    return value.isoformat() if value is not None else None


def _parse(value: Optional[str]) -> Optional[datetime]:
    return datetime.fromisoformat(value) if value is not None else None


class SqliteTaskRepository:
    """Persist tasks in SQLite. Suitable for integration tests and production."""

    def __init__(self, connection: sqlite3.Connection) -> None:
        self._conn = connection
        self._conn.row_factory = sqlite3.Row
        self._conn.execute("PRAGMA foreign_keys = ON;")
        self._conn.executescript(_SCHEMA)
        self._conn.commit()

    @classmethod
    def in_memory(cls) -> "SqliteTaskRepository":
        return cls(sqlite3.connect(":memory:"))

    def _row_to_task(self, row: sqlite3.Row) -> Task:
        task = Task(
            id=row["id"],
            title=row["title"],
            status=TaskStatus(row["status"]),
            priority=TaskPriority(row["priority"]),
            due_at=_parse(row["due_at"]),
            completed_at=_parse(row["completed_at"]),
            created_at=_parse(row["created_at"]),  # type: ignore[arg-type]
        )
        tag_rows = self._conn.execute(
            "SELECT name FROM task_tags WHERE task_id = ? ORDER BY name", (task.id,)
        ).fetchall()
        task.tags = [Tag(r["name"]) for r in tag_rows]
        return task

    def add(self, task: Task) -> None:
        try:
            self._conn.execute(
                "INSERT INTO tasks (id, title, status, priority, due_at, completed_at, created_at)"
                " VALUES (?, ?, ?, ?, ?, ?, ?)",
                (
                    task.id,
                    task.title,
                    task.status.value,
                    task.priority.value,
                    _iso(task.due_at),
                    _iso(task.completed_at),
                    _iso(task.created_at),
                ),
            )
        except sqlite3.IntegrityError as exc:
            raise ValueError(f"task id {task.id} already exists") from exc
        self._replace_tags(task)
        self._conn.commit()

    def get(self, task_id: int) -> Optional[Task]:
        row = self._conn.execute(
            "SELECT * FROM tasks WHERE id = ?", (task_id,)
        ).fetchone()
        return self._row_to_task(row) if row else None

    def list(self) -> Iterable[Task]:
        rows = self._conn.execute("SELECT * FROM tasks ORDER BY id").fetchall()
        return [self._row_to_task(r) for r in rows]

    def update(self, task: Task) -> None:
        cursor = self._conn.execute(
            "UPDATE tasks SET title = ?, status = ?, priority = ?, due_at = ?,"
            " completed_at = ? WHERE id = ?",
            (
                task.title,
                task.status.value,
                task.priority.value,
                _iso(task.due_at),
                _iso(task.completed_at),
                task.id,
            ),
        )
        if cursor.rowcount == 0:
            raise KeyError(task.id)
        self._replace_tags(task)
        self._conn.commit()

    def delete(self, task_id: int) -> bool:
        cursor = self._conn.execute("DELETE FROM tasks WHERE id = ?", (task_id,))
        self._conn.commit()
        return cursor.rowcount > 0

    def next_id(self) -> int:
        row = self._conn.execute("SELECT COALESCE(MAX(id), 0) AS max_id FROM tasks").fetchone()
        return int(row["max_id"]) + 1

    def _replace_tags(self, task: Task) -> None:
        self._conn.execute("DELETE FROM task_tags WHERE task_id = ?", (task.id,))
        if task.tags:
            self._conn.executemany(
                "INSERT INTO task_tags (task_id, name) VALUES (?, ?)",
                [(task.id, tag.name) for tag in task.tags],
            )
