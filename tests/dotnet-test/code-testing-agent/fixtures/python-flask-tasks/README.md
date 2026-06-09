# Tasks API (Python Flask) — code-testing-agent polyglot eval fixture

A small Flask "tasks" API used as a polyglot eval fixture for the `code-testing-agent` skill. The agent is asked to write a pytest suite for the service and routes; the eval verifies that `pytest` passes against the suite the agent produced.

## Layout

```
pyproject.toml                          # Flask + pytest, src layout
src/tasks_api/
  __init__.py
  models.py                             # Task / Tag / TaskStatus / TaskPriority + is_overdue()
  queries.py                            # TaskQuery / TaskPage + apply_query (filter / sort / paginate)
  repository.py                         # TaskRepository protocol + InMemoryTaskRepository + normalize_tags
  repository_sqlite.py                  # SqliteTaskRepository (second backend, stdlib sqlite3)
  service.py                            # TaskService: create / get / query / complete / reopen / delete
                                        #             priority / due_at / tag mutations; injected clock
  routes.py                             # Flask blueprint: /tasks CRUD, filtering, tag endpoints
  app.py                                # create_app() — selects backend from TASKS_BACKEND env / config
tests/                                  # no test files yet (only a .gitkeep marker) — the agent must create the suite here
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

Coverage (pytest-cov) is enabled by default via `pyproject.toml` and is
enforced as a **hard 80% line + branch floor** on the `tasks_api` package
— `pytest` exits non-zero (and `coverage.xml` is not produced) when the
suite does not cover at least 80% of lines and branches.

## What the agent should produce

A planned, layered test suite covering the multiple seams in this fixture:

- `TaskService` tests against a mocked `TaskRepository` (e.g. `unittest.mock.Mock(spec=TaskRepository)`)
  with an injected `now` callable for deterministic `created_at` / `completed_at` / overdue checks.
  Cover the validation matrix: title (type, empty, length), priority, due_at (timezone-awareness),
  tags (normalization, dedup), state transitions (complete / reopen, already-done, already-pending),
  and tag add/remove edge cases.
- `queries.apply_query` tests against fixed `Task` lists covering filter combinations
  (status, tag, search, overdue), sort fields (created_at, due_at, priority, title),
  ascending vs descending order, and pagination boundaries (limit/offset/has_more).
- `SqliteTaskRepository` integration tests against an in-memory connection
  (`SqliteTaskRepository.in_memory()`) covering add/get/update/delete/list/next_id and tag persistence.
- Flask blueprint tests using `create_app(service=...).test_client()` — no real network ports.
  Exercise the request/response mapping: 200/201/204/400/404/409 paths, query-parameter parsing,
  JSON shape, and tag endpoint round-trips.
- Use the in-memory backend for blueprint tests by default; only use SQLite when the test is
  specifically about persistence behavior.
