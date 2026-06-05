"""Flask blueprint exposing the tasks API."""

from __future__ import annotations

from datetime import datetime
from typing import Any, Optional

from flask import Blueprint, current_app, jsonify, request

from .models import Tag, Task, TaskPriority, TaskStatus
from .queries import TaskPage, TaskQuery, TaskQueryError
from .service import TaskNotFoundError, TaskService


def _task_to_json(task: Task) -> dict:
    return {
        "id": task.id,
        "title": task.title,
        "status": task.status.value,
        "priority": task.priority.value,
        "due_at": task.due_at.isoformat() if task.due_at else None,
        "completed_at": task.completed_at.isoformat() if task.completed_at else None,
        "created_at": task.created_at.isoformat(),
        "tags": [t.name for t in task.tags],
    }


def _page_to_json(page: TaskPage) -> dict:
    return {
        "items": [_task_to_json(t) for t in page.items],
        "total": page.total,
        "limit": page.limit,
        "offset": page.offset,
        "has_more": page.has_more,
    }


def _service() -> TaskService:
    service = current_app.config.get("TASK_SERVICE")
    if service is None:
        raise RuntimeError("TASK_SERVICE is not configured on the Flask app")
    return service


def _parse_due_at(value: Any) -> Optional[datetime]:
    if value is None:
        return None
    if not isinstance(value, str):
        raise ValueError("due_at must be an ISO 8601 string or null")
    try:
        return datetime.fromisoformat(value)
    except ValueError as exc:
        raise ValueError(f"due_at is not a valid ISO 8601 datetime: {value!r}") from exc


def _parse_priority(value: Any) -> TaskPriority:
    if value is None:
        return TaskPriority.MEDIUM
    if not isinstance(value, str):
        raise ValueError("priority must be a string")
    try:
        return TaskPriority(value)
    except ValueError as exc:
        valid = ", ".join(p.value for p in TaskPriority)
        raise ValueError(f"priority must be one of: {valid}") from exc


def _parse_tags(value: Any) -> list[Tag]:
    if value is None:
        return []
    if not isinstance(value, list) or not all(isinstance(t, str) for t in value):
        raise ValueError("tags must be a list of strings")
    return [Tag(t) for t in value]


def _build_query() -> TaskQuery:
    args = request.args
    raw_status = args.get("status")
    status: Optional[TaskStatus] = None
    if raw_status and raw_status != "all":
        try:
            status = TaskStatus(raw_status)
        except ValueError as exc:
            raise TaskQueryError(f"unknown status filter: {raw_status}") from exc

    overdue: Optional[bool] = None
    if "overdue" in args:
        raw_overdue = args.get("overdue", "").lower()
        if raw_overdue in ("true", "1", "yes"):
            overdue = True
        elif raw_overdue in ("false", "0", "no"):
            overdue = False
        else:
            raise TaskQueryError("overdue must be true or false")

    try:
        limit = int(args.get("limit", 20))
        offset = int(args.get("offset", 0))
    except ValueError as exc:
        raise TaskQueryError("limit and offset must be integers") from exc

    return TaskQuery(
        status=status,
        tag=args.get("tag"),
        search=args.get("q"),
        overdue=overdue,
        sort=args.get("sort", "created_at"),
        descending=args.get("order", "asc").lower() == "desc",
        limit=limit,
        offset=offset,
    )


bp = Blueprint("tasks", __name__, url_prefix="/tasks")


@bp.post("")
def create_task():
    payload = request.get_json(silent=True)
    if not isinstance(payload, dict):
        return jsonify({"error": "request body must be a JSON object"}), 400
    title = payload.get("title", "")
    if not isinstance(title, str):
        return jsonify({"error": "title must be a string"}), 400
    try:
        priority = _parse_priority(payload.get("priority"))
        due_at = _parse_due_at(payload.get("due_at"))
        tags = _parse_tags(payload.get("tags"))
        task = _service().create(title, priority=priority, due_at=due_at, tags=tags)
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 400
    return jsonify(_task_to_json(task)), 201


@bp.get("")
def list_tasks():
    try:
        query = _build_query()
    except TaskQueryError as exc:
        return jsonify({"error": str(exc)}), 400
    page = _service().query(query)
    return jsonify(_page_to_json(page)), 200


@bp.get("/<int:task_id>")
def get_task(task_id: int):
    try:
        task = _service().get(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    return jsonify(_task_to_json(task)), 200


@bp.delete("/<int:task_id>")
def delete_task(task_id: int):
    try:
        _service().delete(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    return "", 204


@bp.post("/<int:task_id>/complete")
def complete_task(task_id: int):
    try:
        task = _service().complete(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 409
    return jsonify(_task_to_json(task)), 200


@bp.post("/<int:task_id>/reopen")
def reopen_task(task_id: int):
    try:
        task = _service().reopen(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 409
    return jsonify(_task_to_json(task)), 200


@bp.post("/<int:task_id>/tags")
def add_task_tags(task_id: int):
    payload = request.get_json(silent=True)
    if not isinstance(payload, dict):
        return jsonify({"error": "request body must be a JSON object"}), 400
    try:
        tags = _parse_tags(payload.get("tags"))
        task = _service().add_tags(task_id, tags)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 400
    return jsonify(_task_to_json(task)), 200


@bp.delete("/<int:task_id>/tags/<string:name>")
def remove_task_tag(task_id: int, name: str):
    try:
        task = _service().remove_tag(task_id, name)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 400
    return jsonify(_task_to_json(task)), 200
