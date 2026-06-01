"""Flask blueprint exposing the tasks API."""

from __future__ import annotations

from flask import Blueprint, current_app, jsonify, request

from .models import Task, TaskStatus
from .service import TaskNotFoundError, TaskService


def _task_to_json(task: Task) -> dict:
    return {
        "id": task.id,
        "title": task.title,
        "status": task.status.value,
        "completed_at": task.completed_at.isoformat() if task.completed_at else None,
        "created_at": task.created_at.isoformat(),
    }


def _service() -> TaskService:
    service = current_app.config.get("TASK_SERVICE")
    if service is None:
        raise RuntimeError("TASK_SERVICE is not configured on the Flask app")
    return service


bp = Blueprint("tasks", __name__, url_prefix="/tasks")


@bp.post("")
def create_task():
    payload = request.get_json(silent=True) or {}
    title = payload.get("title", "")
    try:
        task = _service().create(title)
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 400
    return jsonify(_task_to_json(task)), 201


@bp.get("")
def list_tasks():
    status = request.args.get("status")
    service = _service()
    if status == TaskStatus.PENDING.value:
        tasks = service.list_pending()
    elif status in (None, "", "all"):
        tasks = service.list_all()
    elif status == TaskStatus.DONE.value:
        tasks = [t for t in service.list_all() if t.status == TaskStatus.DONE]
    else:
        return jsonify({"error": f"unknown status filter: {status}"}), 400
    return jsonify([_task_to_json(t) for t in tasks]), 200


@bp.get("/<int:task_id>")
def get_task(task_id: int):
    try:
        task = _service().get(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    return jsonify(_task_to_json(task)), 200


@bp.post("/<int:task_id>/complete")
def complete_task(task_id: int):
    try:
        task = _service().complete(task_id)
    except TaskNotFoundError as exc:
        return jsonify({"error": str(exc)}), 404
    except ValueError as exc:
        return jsonify({"error": str(exc)}), 409
    return jsonify(_task_to_json(task)), 200
