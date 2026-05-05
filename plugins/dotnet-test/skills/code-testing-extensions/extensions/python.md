# Python Extension

Language-specific guidance for Python test generation.

## Environment and Runner Detection

Before running any commands, detect the project's package manager/runner from lockfiles and config:

| Indicator | Runner | Command prefix |
|-----------|--------|---------------|
| `poetry.lock` or `[tool.poetry]` in `pyproject.toml` | Poetry | `poetry run` |
| `pdm.lock` or `[tool.pdm]` in `pyproject.toml` | PDM | `pdm run` |
| `uv.lock` or `[tool.uv]` in `pyproject.toml` | uv | `uv run` |
| `Pipfile.lock` | Pipenv | `pipenv run` |
| `hatch.toml` or `[tool.hatch]` in `pyproject.toml` | Hatch | `hatch run` |
| None of the above | pip/venv | Use `python -m` prefix |

- **Always prefer `python -m pytest` over bare `pytest`** — this ensures the correct interpreter and avoids PATH issues
- **Always prefer `python -m pip` over bare `pip`** for the same reason
- If a `Makefile`, `tox.ini`, or `nox` config exists, prefer the project's existing build/test commands
- **Do not add a new test framework if one already exists** — follow the repo's established choices

## Build Commands

Python is interpreted — there is no separate build step. Validate syntax and imports by running the tests or using a type checker if configured.

| Scope | Command |
|-------|---------|
| Syntax check | `python -m py_compile path/to/file.py` |
| Type check (if configured) | `mypy path/to/file.py` or `pyright path/to/file.py` |

- Check for `pyproject.toml`, `setup.py`, `setup.cfg`, or `requirements.txt` to understand the project layout
- If the project uses editable installs, run `python -m pip install -e .` or `python -m pip install -e ".[dev]"` before testing

## Test Commands

| Scope | Command |
|-------|---------|
| All tests | `pytest` |
| Specific file | `pytest tests/test_module.py` |
| Specific test | `pytest tests/test_module.py::TestClass::test_method` |
| Keyword filter | `pytest -k "keyword"` |
| Verbose output | `pytest -v` |
| Stop on first failure | `pytest -x` |

- Prefer `pytest` over `unittest` — most Python projects use pytest even when test classes inherit `unittest.TestCase`
- **Prefer the project's existing test script** (`make test`, `tox`, `nox`) over raw `pytest` commands
- If `pytest` is not installed, check `pyproject.toml` `[project.optional-dependencies]` or `requirements-dev.txt`
- Use `pytest --tb=short` to reduce traceback noise during fix cycles
- If the project uses `unittest` exclusively (no pytest in dependencies), use `python -m unittest discover`

## Lint Command

Prefer the project's existing lint script (e.g., `make lint`, `tox -e lint`) over running tools directly. If no script exists, detect which tools the project uses from `pyproject.toml`, `.flake8`, `setup.cfg`, or `ruff.toml`:

```bash
# Prefer ruff when available (fast, covers linting + formatting + import sorting)
ruff check --fix path/to/test_file.py
ruff format path/to/test_file.py

# Fallback alternatives
black path/to/test_file.py       # formatting
flake8 path/to/test_file.py      # linting
isort path/to/test_file.py       # import sorting
```

## Dependency Validation

Before writing test code, verify the test dependencies are available:

1. **pytest**: Check `pyproject.toml` or `requirements*.txt` for `pytest`
2. **Source package**: If the source code is in a `src/` layout, verify the package is importable (editable install)
3. **Mocking**: `unittest.mock` is in the stdlib — no extra dependency needed
4. **pytest plugins**: Check for `pytest-asyncio` (async tests), `pytest-mock` (mocker fixture)

If imports fail with `ModuleNotFoundError`:

```bash
python -m pip install -e .                    # Install source package in editable mode
python -m pip install -e ".[dev]"             # Install with dev extras
python -m pip install pytest pytest-asyncio   # Install test dependencies directly
```

## Common Errors

| Error | Meaning | Fix |
|-------|---------|-----|
| `ModuleNotFoundError` | Package not installed or wrong import path | `pip install -e .` or fix the import statement |
| `ImportError` | Symbol not found in module | Verify the function/class name matches the source exactly |
| `AttributeError` | Wrong attribute on object | Check spelling and that the attribute exists in the source |
| `TypeError: __init__() missing required argument` | Constructor needs more args | Read the `__init__` signature and pass all required parameters |
| `TypeError: takes N positional arguments but M were given` | Wrong number of args | Match the function signature exactly |
| `fixture 'X' not found` | pytest fixture not defined or not imported | Define the fixture or add the correct import/conftest.py |
| `SyntaxError` | Invalid Python syntax in test file | Fix the syntax error at the indicated line |

## Project Layout Detection

Python projects use varied layouts. Detect the correct one:

| Layout | Structure | Import Style |
|--------|-----------|-------------|
| `src/` layout | `src/package/module.py` | `from package.module import X` |
| Flat layout | `package/module.py` at repo root | `from package.module import X` |
| Single module | `module.py` at repo root | `from module import X` |

- Check `pyproject.toml` `[tool.setuptools.packages.find]` or `[tool.setuptools.package-dir]` for layout hints
- If `conftest.py` exists at the repo root, pytest typically handles path resolution
- **Follow the existing convention first** — check where existing tests live before placing new ones
- If no convention exists, default to `tests/` mirroring the source structure: `src/billing/service.py` → `tests/billing/test_service.py`

## Test Discovery

Python tests are discovery-based — no registration step is needed (unlike .NET solutions). Pytest finds tests automatically if naming conventions are followed.

## Testing Philosophy

- **Test behavior through the public API** — do not test private functions (prefixed with `_`) directly unless they contain complex algorithms that cannot be adequately exercised through the public surface
- **Prefer output/state assertions over interaction assertions** — verify return values and observable state changes, not internal call counts
- **Do not mock the system under test** — only mock external dependencies (databases, HTTP, filesystem, time)
- **Do not patch private helpers** — if behavior is only reachable through a private function, test it via the public method that calls it
- Fewer focused tests that thoroughly exercise behavior are better than many shallow tests that only check surface behavior

## Test File Naming

- Test files must be named `test_*.py` or `*_test.py` (pytest default discovery)
- Test functions must start with `test_`
- Test classes must start with `Test` (no `__init__` method)

## pytest Template

```python
import pytest
from package.module import ClassName


class TestClassName:
    """Tests for ClassName behavior."""

    def test_method_name_returns_expected_result(self):
        # Given
        sut = ClassName()

        # When
        result = sut.method_name(input_value)

        # Then
        assert result == expected_value

    @pytest.mark.parametrize("a,b,expected", [
        (2, 3, 5),
        (-1, 1, 0),
        (0, 0, 0),
    ])
    def test_add_valid_inputs_returns_sum(self, a, b, expected):
        # Given
        sut = ClassName()

        # When
        result = sut.add(a, b)

        # Then
        assert result == expected

    def test_method_raises_on_invalid_input(self):
        sut = ClassName()
        with pytest.raises(ValueError, match="must be positive"):
            sut.method_name(-1)
```

## Mocking Guidelines

Use `unittest.mock` (stdlib) or the `pytest-mock` plugin's `mocker` fixture:

```python
from unittest.mock import Mock, patch, AsyncMock

# Dependency injection — preferred
def test_service_calls_repository(self):
    # Given
    repo = Mock()
    repo.find.return_value = {"id": 1, "name": "test"}
    sut = Service(repository=repo)

    # When
    result = sut.get_by_id(1)

    # Then
    assert result["name"] == "test"
    repo.find.assert_called_once_with(1)

# Patching module-level dependencies — use sparingly
@patch("package.module.datetime")
def test_uses_current_time(self, mock_dt):
    mock_dt.utcnow.return_value = datetime(2024, 1, 1)
    # ...
```

- Prefer dependency injection over `@patch` — it produces clearer, less brittle tests
- When patching, patch where the name is **looked up**, not where it is defined
- Use `Mock(spec=RealClass)` to catch attribute errors early
- Use `AsyncMock` for async functions
- If a test needs more than 3 mocks, flag it as a design smell

## Async Tests

If the source code uses `async def`, tests need async support:

```python
import pytest

@pytest.mark.asyncio
async def test_async_method_returns_result():
    sut = AsyncService()
    result = await sut.fetch_data(42)
    assert result is not None
```

- Requires `pytest-asyncio` package
- Check for `asyncio_mode = "auto"` in `pyproject.toml` — if set, the `@pytest.mark.asyncio` decorator is not needed

## Skip Coverage Tools

Do not configure or run code coverage measurement tools (coverage.py, pytest-cov). Coverage is measured separately by the evaluation harness.
