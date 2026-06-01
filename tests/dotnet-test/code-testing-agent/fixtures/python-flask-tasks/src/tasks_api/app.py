"""Flask application factory."""

from __future__ import annotations

from typing import Optional

from flask import Flask

from .repository import InMemoryTaskRepository
from .routes import bp as tasks_bp
from .service import TaskService


def create_app(service: Optional[TaskService] = None) -> Flask:
    app = Flask(__name__)
    if service is None:
        service = TaskService(InMemoryTaskRepository())
    app.config["TASK_SERVICE"] = service
    app.register_blueprint(tasks_bp)
    return app
