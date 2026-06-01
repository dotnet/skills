# Tasks API (Python Flask) — code-testing-agent polyglot eval fixture

A small Flask "tasks" API used as a polyglot eval fixture for the `code-testing-agent` skill. The agent is asked to write a pytest suite for the service and routes; the eval verifies that `pytest` passes against the suite the agent produced.

## Layout

```
pyproject.toml                          # Flask + pytest, src layout
src/tasks_api/
  __init__.py
  models.py                             # Task dataclass + TaskStatus enum
  repository.py                         # TaskRepository protocol + InMemoryTaskRepository
  service.py                            # TaskService (create / get / list / complete) with injected clock
  routes.py                             # Flask blueprint exposing /tasks endpoints
  app.py                                # create_app() application factory
tests/                                  # intentionally empty — the agent must create this
```

## Running tests locally

Linux / macOS / WSL:

```bash
python -m pip install -e ".[test]"
python -m pytest
```

Windows:

```pwsh
py -m pip install -e ".[test]"
py -m pytest
```

## What the agent should produce

- Tests for `TaskService` mocking the repository (`unittest.mock.Mock(spec=TaskRepository)`) and an injected `now` callable for `complete`.
- Tests for the Flask blueprint using `create_app(service=...).test_client()` — no real network ports.
- At minimum: happy path + validation error (empty title) + not-found + already-done cases.
