# Python Test Frameworks Reference (pytest, unittest)

Reference data for analyzing Python test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — convention-driven (`test_*.py`, `*_test.py`, `Test*` classes) |
| Assertion detection | Strong — bare `assert`, `unittest` methods, `pytest.raises` |
| Sleep/delay detection | Strong — `time.sleep`, `asyncio.sleep` |
| Skip/ignore detection | Strong — `@pytest.mark.skip`, `unittest.skip` |
| Setup/teardown detection | Strong — fixtures and methods |
| Tag support | **auto-edit** — `@pytest.mark.<tag>` (pytest), no canonical syntax in unittest |

## Test File Identification

| Framework | Test file convention | Test method markers |
|-----------|---------------------|---------------------|
| pytest | `test_*.py` or `*_test.py` | functions starting with `test_`; classes starting with `Test` (no `__init__`) and methods starting with `test_` |
| unittest | any module (often `test_*.py`) | classes inheriting `unittest.TestCase` with methods starting with `test` |

## Assertion APIs

| Category | pytest | unittest |
|----------|--------|----------|
| Equality | `assert x == y` | `self.assertEqual(x, y)` |
| Inequality | `assert x != y` | `self.assertNotEqual(x, y)` |
| Boolean | `assert flag` / `assert not flag` | `self.assertTrue(flag)` / `self.assertFalse(flag)` |
| None | `assert x is None` | `self.assertIsNone(x)` / `self.assertIsNotNone(x)` |
| Exception | `with pytest.raises(SomeError) as exc_info: ...` | `with self.assertRaises(SomeError): ...` |
| Type | `assert isinstance(x, T)` | `self.assertIsInstance(x, T)` |
| Identity | `assert x is y` | `self.assertIs(x, y)` |
| Membership | `assert item in collection` | `self.assertIn(item, collection)` |
| Approximate | `assert x == pytest.approx(y, rel=0.01)` | `self.assertAlmostEqual(x, y, places=2)` |
| String | `assert sub in s` / `assert s.startswith(...)` | `self.assertIn(sub, s)` |
| Skip | `pytest.skip("reason")` | `self.skipTest("reason")` |
| Fail | `pytest.fail("reason")` | `self.fail("reason")` |

**Important:** Bare `assert` is the canonical pytest assertion and produces rich failure diffs via pytest's assertion rewriting. Do NOT flag bare `assert` as a missing-framework-API smell.

Third-party assertion libraries: `assertpy`, `hamcrest` (`assert_that`), `expects`.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Sync sleep | `time.sleep(2)` |
| Async sleep | `await asyncio.sleep(1)` |
| Loop wait | `while not condition: time.sleep(0.1)` |
| Trio/anyio | `await trio.sleep(...)`, `await anyio.sleep(...)` |

## Skip/Ignore Annotations

| Framework | Annotation |
|-----------|------------|
| pytest | `@pytest.mark.skip(reason="...")`, `@pytest.mark.skipif(cond, reason="...")`, `@pytest.mark.xfail(reason="...")`, `pytest.skip("...")` inline |
| unittest | `@unittest.skip("reason")`, `@unittest.skipIf(cond, "reason")`, `@unittest.skipUnless(cond, "reason")`, `@unittest.expectedFailure` |

## Exception Handling — Idiomatic Alternatives

```python
# pytest (preferred):
with pytest.raises(ValueError, match=r"must be positive"):
    parse_amount(-5)

# unittest:
with self.assertRaises(ValueError):
    parse_amount(-5)

# To inspect the exception:
with pytest.raises(ValueError) as exc_info:
    parse_amount(-5)
assert "must be positive" in str(exc_info.value)
```

Flag bare `try/except` in tests as Exception Handling smell only when no assertion follows or the exception is silently swallowed.

## Mystery Guest — Common Python Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `open()`, `pathlib.Path(...).read_text()`, `os.path.exists`, hard-coded absolute paths |
| Database | direct `psycopg2`/`mysql.connector`/`sqlite3.connect` to a file path, `SQLAlchemy` engine pointing at a real DB URL |
| Network | `requests.get/post`, `httpx.get/post`, `urllib.request.urlopen`, raw `socket` |
| Environment | `os.getenv("X")` (especially without default), `os.environ["X"]` |
| Acceptable | `io.StringIO` / `io.BytesIO`, `tmp_path` / `tmp_path_factory` pytest fixtures, `monkeypatch.setenv`, `responses` / `httpx.MockTransport`, `pytest-mock`, sqlite `:memory:` |

## Integration Test Markers

- Folder names: `tests/integration/`, `tests/e2e/`, `tests/acceptance/`
- Module/class/function names containing `Integration`, `E2E`, `EndToEnd`, `Acceptance`
- `@pytest.mark.integration` / `@pytest.mark.e2e` (project-specific markers registered in `pytest.ini` / `pyproject.toml`)
- Conftest fixtures that spin up containers / databases (`testcontainers`, `docker-compose` fixtures)

## Setup/Teardown

| Framework | Setup | Teardown |
|-----------|-------|----------|
| pytest | `@pytest.fixture` (any scope), `autouse=True` fixtures | yield-based teardown inside fixture or `request.addfinalizer` |
| pytest (class) | `setup_method` / `setup_class` | `teardown_method` / `teardown_class` |
| unittest | `setUp` / `setUpClass` / `setUpModule` | `tearDown` / `tearDownClass` / `tearDownModule` |

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| pytest | `@pytest.mark.<name>` (project-registered) | `@pytest.mark.positive`, `@pytest.mark.boundary` |
| unittest | none built-in — use class organization, attributes, or `unittest.skipIf` toggles | *(report-only; recommend pytest markers or a project convention)* |

For pytest, ensure the markers are registered in `pyproject.toml` / `pytest.ini` to avoid `PytestUnknownMarkWarning`:

```toml
[tool.pytest.ini_options]
markers = [
    "positive: verifies expected behavior under normal conditions",
    "negative: verifies handling of invalid input or error paths",
    "boundary: tests limits, thresholds, empty/null inputs",
]
```

## Language-specific calibration notes

- **Bare `assert`** is the pytest idiom — do not flag it as assertion-free.
- **Snapshot tests** (`syrupy`, `pytest-snapshot`) replace the `assert` call with an implicit snapshot compare; treat as a legitimate assertion.
- **Property-based tests** (`hypothesis`): a `@given(...)`-decorated function is a real test even if it appears to have no body — the assertions live in the generated input cycles.
- **Async tests** (`pytest-asyncio`, `anyio`): missing `await` on a coroutine call inside the test produces a `RuntimeWarning` and an effectively assertion-free test. Flag as a critical anti-pattern.
- **Doctests** invoked via `--doctest-modules` are tests too; treat `>>>` blocks as test methods if the user includes them in scope.
- **Parametrized tests** (`@pytest.mark.parametrize`) are *not* duplicates of the underlying function — treat them as the consolidated form.
- **Fixtures used by only one test** are not General Fixture smells; pytest fixtures are pay-as-you-go (a fixture only runs when a test requests it).
