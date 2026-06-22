# Python Extension

Language-specific guidance for Python test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, discover what the repo already does:

1. **Find ALL existing test files** — search broadly: `test_*.py`, `*_test.py`, `*.uts`, `test/*.sh`, or any other test format. Do not assume pytest.
2. **Identify the test framework and runner** — check, in order:
   - `pyproject.toml` `[tool.pytest.ini_options]` `testpaths`
   - `pytest.ini`
   - `setup.cfg` `[tool:pytest]`
   - `tox.ini` `[testenv]` `commands`
   - `Makefile`, `noxfile.py`, `conftest.py` locations
   - Project-specific runners such as Django `runtests.py`, `manage.py test`, or `DJANGO_SETTINGS_MODULE`
3. **Find the active test layout** — note the directory, working directory, and fixture scope used by existing tests. Some repos use non-standard layouts such as Ansible-style `test/units/`.
4. **Read existing tests thoroughly** — copy their exact style: file format, imports, fixtures, assertion patterns, helper utilities, setup/teardown conventions
5. **Package layout** — determine import paths from existing code, not guesswork

**Use whatever framework and conventions the repo already uses.** If the repo uses a custom test framework (custom file formats, custom runners, domain-specific test utilities), adopt it fully — do not layer pytest on top. Only introduce pytest if the repo has no tests at all.

**Never finish with a failing or erroring test.** Run the full new-test suite before finishing. If a test cannot be made to pass within a reasonable number of attempts, delete it. A smaller suite where every test passes is strictly better than a larger suite with any failure — a suite with one failing test can score zero.

**Start simple to bank coverage.** Write high-certainty tests for pure functions, validation branches, serializers, small helpers, and deterministic error paths before attempting async views, templates, sessions, network paths, or integration-heavy code.

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

`<prefix>` applies to **module execution** only. With the default `python -m` prefix, `<prefix> pytest` expands to `python -m pytest`, but a script entry point or inline probe must not be double-prefixed — `python -m python manage.py …` / `python -m python -c …` is invalid. Run script entry points (`manage.py`, `runtests.py`) and `python -c` probes with `python` directly, wrapping with the env tool when one is detected (e.g. `poetry run python manage.py test …`, `uv run python -c "…"`) instead of `python -m`.

If `Makefile`, `tox.ini`, or `nox` config exists, prefer those scripts over raw commands.

## Build Commands

Python has no separate build step. Validate with the type checker if one is configured:

| Scope | Command |
|-------|---------|
| Syntax check | `<prefix> py_compile path/to/file.py` |
| Type check | `<prefix> mypy path/to/file.py` or `<prefix> pyright path/to/file.py` |

## Test Commands

Run new tests the same way the repo runs existing tests: same working directory, same command wrapper, same `conftest.py` scope, and same settings environment variables.

Before choosing a command, inspect runner configuration with copy-pasteable probes:

```powershell
Get-ChildItem -Recurse -File -Include pyproject.toml,pytest.ini,setup.cfg,tox.ini,Makefile,noxfile.py,conftest.py,runtests.py,manage.py
Select-String -Path pyproject.toml,pytest.ini,setup.cfg,tox.ini -Pattern 'testpaths|\[tool.pytest|\[tool:pytest|commands|DJANGO_SETTINGS_MODULE' -ErrorAction SilentlyContinue
```

If the repo uses a **custom test framework** (custom file formats, custom runner), use its native commands — do not wrap them in pytest. Examples:

| Framework | Command |
|-----------|---------|
| UTscapy (`.uts` files) | `<prefix> scapy.tools.UTscapy -f test/test_file.uts` |
| Django runner script | `python runtests.py app_label.tests.test_module` |
| Django project | `python manage.py test app_label.tests.test_module` |
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
- If tests must run from a subdirectory, `Set-Location` there first and keep that working directory for verification

## Frameworks Beyond Plain Pytest

Mirror the existing tests' import style and invocation exactly.

- **Django**: Prefer the repo's runner (`runtests.py`, `manage.py test`, or tox/make target). If the repo uses `pytest-django`, ensure `DJANGO_SETTINGS_MODULE` is set exactly as existing tests/config require.
  - `python manage.py test app_label.tests.test_module`
  - `$env:DJANGO_SETTINGS_MODULE='project.settings'; python -m pytest tests/app/test_module.py`
- **unittest-style suites**: Use `python -m unittest path.to.test_module` or the repo's discover command; do not force pytest unless existing tests already do.
- **Subdir runners**: Some repos expect commands from `tests/`, `test/units/`, or another subdir so relative imports and fixtures work.

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
- Place new tests where the existing suite lives so the same `conftest.py`, fixtures, helpers, and settings apply
- Check `pyproject.toml` `[tool.setuptools.package-dir]` for layout hints
- Default test placement: `tests/` mirroring source structure (`src/billing/service.py` → `tests/billing/test_service.py`)

## Heavy or Native Dependencies

Before writing tests for a target module, verify it imports cleanly in the same environment and working directory as tests. Run the probe under the **same env wrapper as the test command** (`poetry run`, `pdm run`, `uv run`, `pipenv run`, `hatch run`) so the check reflects the real test interpreter/venv — a bare `python` may resolve to a different environment and report a misleading `ok`:

```powershell
# Wrap with the detected env tool, e.g. `poetry run python -c "..."`
python -c "import package.module; print('ok')"
python -c "from package import module; print('ok')"
```

If a heavy/native dependency such as NumPy, pandas, PyTorch, TensorFlow, cryptography, or a compiled extension cannot be imported or built in the environment:

- Do not write tests that import the failing module
- Do not spend the budget fighting native build/import failures or installing unrelated packages
- Scope down to a pure-Python submodule that imports cleanly, or omit tests for that module rather than shipping ones that cannot run (see *Finalization: Green Suite or Remove*)

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
| `DJANGO_SETTINGS_MODULE is undefined` | Use the repo's Django runner or set the same settings module used by existing tests |
| `ImportError` from `torch`, `numpy`, or compiled extension | Avoid that module; choose a pure-Python target that imports cleanly |
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

## Finalization: Green Suite or Remove

Before finishing, run the complete set of tests you added with the repo's native invocation, under the **same env wrapper as the repo's tests** (`poetry run`, `pdm run`, `uv run`, `pipenv run`, `hatch run`). Running the green-suite check in a different interpreter/venv can pass locally yet still fail under the repo's actual runner.

```powershell
# Examples; choose the repo-native command/wrapper discovered above
poetry run python -m pytest tests/path/to/new_tests.py
uv run python -m unittest path.to.new_test_module
poetry run python manage.py test app_label.tests.test_module
```

If any new test fails or errors after a reasonable fix attempt, delete that test before finishing. Never leave skipped, xfailed, failing, or collection-error tests just to keep more lines. The final submitted suite must be green.

## Skip Coverage Tools

Do not configure or run coverage tools (coverage.py, pytest-cov). Coverage is measured separately by the evaluation harness.
