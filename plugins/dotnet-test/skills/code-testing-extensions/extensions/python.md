# Python Extension

Language-specific guidance for Python test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, discover what the repo already does:

1. **Find ALL existing test files** — search broadly: `test_*.py`, `*_test.py`, `*.uts`, `test/*.sh`, or any other test format. Do not assume pytest.
2. **Identify the test framework** — look for:
   - Custom test runners (e.g. `UTscapy` for scapy, project-specific harnesses)
   - Standard frameworks (`pytest`, `unittest`, `nose2`)
   - Test runner scripts in `Makefile`, `tox.ini`, `nox`, `scripts/`
   - Config entries in `pyproject.toml`, `setup.cfg`, `pytest.ini`, `conftest.py`
3. **Read existing tests thoroughly** — copy their exact style: file format, imports, fixtures, assertion patterns, helper utilities, setup/teardown conventions
4. **Package layout** — determine import paths from existing code, not guesswork

**Use whatever framework and conventions the repo already uses.** If the repo uses a custom test framework (custom file formats, custom runners, domain-specific test utilities), adopt it fully — do not layer pytest on top. Only introduce pytest if the repo has no tests at all.

## Environment Detection

Detect the runner from lockfiles/config and prefix all commands accordingly:

| Indicator | Prefix |
|-----------|--------|
| `poetry.lock` / `[tool.poetry]` in `pyproject.toml` | `poetry run` |
| `pdm.lock` / `[tool.pdm]` in `pyproject.toml` | `pdm run` |
| `uv.lock` / `[tool.uv]` in `pyproject.toml` | `uv run` |
| `Pipfile.lock` | `pipenv run` |
| `hatch.toml` / `[tool.hatch]` in `pyproject.toml` | `hatch run` |
| None of the above | `python -m` |

If `Makefile`, `tox.ini`, or `nox` config exists, prefer those scripts over raw commands.

## Build Commands

Python has no separate build step. Validate with the type checker if one is configured:

| Scope | Command |
|-------|---------|
| Syntax check | `<prefix> py_compile path/to/file.py` |
| Type check | `<prefix> mypy path/to/file.py` or `<prefix> pyright path/to/file.py` |

## Test Commands

If the repo uses a **custom test framework** (custom file formats, custom runner), use its native commands — do not wrap them in pytest. Examples:

| Framework | Command |
|-----------|---------|
| UTscapy (`.uts` files) | `<prefix> scapy.tools.UTscapy -f test/test_file.uts` |
| Custom runner script | `make test`, `./run_tests.sh`, `tox` |
| Repo-defined script | Whatever `scripts.test` in Makefile/tox/nox specifies |

For **pytest** projects (the most common case), use the detected `<prefix>`:

| Scope | Command |
|-------|---------|
| All tests | `<prefix> pytest` |
| Specific file | `<prefix> pytest tests/test_module.py` |
| Specific test | `<prefix> pytest tests/test_module.py::TestClass::test_method` |
| Keyword filter | `<prefix> pytest -k "keyword"` |
| Stop on first failure | `<prefix> pytest -x --tb=short` |

- Prefer `python -m pytest` over bare `pytest` to ensure the correct interpreter
- If the project uses `unittest` only (no pytest in deps), use `python -m unittest discover`

## Lint Command

Use the repo's existing lint script first (`make lint`, `tox -e lint`). Otherwise detect tools from config:

- `ruff.toml` or `[tool.ruff]` → `<prefix> ruff check --fix && <prefix> ruff format`
- `[tool.black]` → `<prefix> black`
- `.flake8` → `<prefix> flake8`

## Project Layout and Imports

| Layout | Import Style |
|--------|-------------|
| `src/package/module.py` | `from package.module import X` |
| `package/module.py` at root | `from package.module import X` |
| `module.py` at root | `from module import X` |

- **Match existing test imports exactly** — do not invent `src.` prefixes unless existing tests use them
- Check `pyproject.toml` `[tool.setuptools.package-dir]` for layout hints
- Default test placement: `tests/` mirroring source structure (`src/billing/service.py` → `tests/billing/test_service.py`)

## Test File Naming

Match the repo's existing conventions. Common patterns:

- **pytest**: Files `test_*.py` or `*_test.py`, functions `test_` prefix, classes `Test` prefix
- **Custom frameworks**: Use whatever format existing tests use (e.g. `.uts` for UTscapy, custom extensions)

If writing new tests in a repo with no tests, default to pytest conventions.

## Common Errors

| Error | Fix |
|-------|-----|
| `ModuleNotFoundError: No module named 'src'` | Import from the package name used by the repo, not from `src` |
| `ModuleNotFoundError: No module named 'X'` | Check existing imports for the correct package name; if editable install needed: `<prefix> pip install -e .` |
| `ImportError: attempted relative import` | Convert to absolute imports matching existing test patterns |
| `fixture 'X' not found` | Check `conftest.py` for existing fixtures; reuse them instead of creating new ones |
| `TypeError: missing required argument` | Read the full `__init__`/function signature; pass all required parameters |
| `async def functions are not natively supported` | Use `@pytest.mark.asyncio` only if `pytest-asyncio` is already in deps; check for `asyncio_mode = "auto"` in config |
| `SyntaxError` | Fix syntax at the indicated line |

## Mocking Rules

- Use `unittest.mock` (stdlib) — no extra dependency needed
- **Patch where the name is looked up**, not where it is defined: `@patch("mypackage.module.datetime")` not `@patch("datetime.datetime")`
- Use `Mock(spec=RealClass)` to catch attribute errors
- Use `AsyncMock` for async functions
- Prefer dependency injection over `@patch`
- If a test needs more than 3 mocks, flag it as a design smell

## Dependency Installation (Last Resort)

Only install packages after investigation confirms they are missing. Use the detected prefix:

| Manager | Install command |
|---------|----------------|
| Poetry | `poetry add --group dev pytest` |
| PDM | `pdm add -dG test pytest` |
| uv | `uv add --dev pytest` |
| pip | `python -m pip install -e ".[dev]"` |

Never run bare `pip install` in a Poetry/PDM/uv project — it bypasses the lockfile.

## Skip Coverage Tools

Do not configure or run coverage tools (coverage.py, pytest-cov). Coverage is measured separately by the evaluation harness.
