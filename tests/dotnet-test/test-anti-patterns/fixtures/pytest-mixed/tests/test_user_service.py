"""Tests for UserService. Intentionally riddled with anti-patterns."""

import time
import datetime
import random
import os

import pytest

from src.user_service import UserService, UserNotFoundError


_SHARED_SERVICE = UserService()


def test_add_user_runs_without_error():
    svc = UserService()
    svc.add(1, "alice")
    # No assertion! Silently passes even if add() did nothing.


def test_get_missing_swallows_exception():
    svc = UserService()
    try:
        svc.get(99)
    except Exception:
        pass
    # Silently passes whether or not an exception was raised.


def test_delete_compares_count_to_itself():
    svc = UserService()
    svc.add(1, "alice")
    svc.delete(1)
    assert svc.count() == svc.count()  # always true, proves nothing


def test_add_raises_broad_exception():
    svc = UserService()
    svc.add(1, "alice")
    with pytest.raises(Exception):  # broad — should be ValueError
        svc.add(1, "bob")


def test_flaky_sleep_and_now():
    svc = UserService()
    svc.add(1, "alice")
    time.sleep(0.2)  # wall-clock sleep used for synchronization
    timestamp = datetime.datetime.now()  # un-injected real clock
    assert timestamp is not None


def test_ordering_dependency_shared_state_part_one():
    _SHARED_SERVICE.add(42, "shared-alice")
    assert _SHARED_SERVICE.count() >= 1


def test_ordering_dependency_shared_state_part_two():
    # Depends on the previous test having added user 42.
    assert _SHARED_SERVICE.get(42) == "shared-alice"


def test_random_unseeded():
    svc = UserService()
    uid = random.randint(1, 1_000_000)  # unseeded — non-deterministic
    svc.add(uid, "x")
    assert svc.count() == 1


def test_env_dependent_path():
    home = os.environ.get("HOME") or os.environ.get("USERPROFILE", "/tmp")
    assert isinstance(home, str)  # depends on environment


def test_repeated_value_assertion_1():
    svc = UserService()
    svc.add(1, "a")
    assert svc.count() == 1


def test_repeated_value_assertion_2():
    svc = UserService()
    svc.add(1, "a")
    assert svc.count() == 1


def test_repeated_value_assertion_3():
    svc = UserService()
    svc.add(1, "a")
    assert svc.count() == 1


def test_repeated_value_assertion_4():
    svc = UserService()
    svc.add(1, "a")
    assert svc.count() == 1


def test_get_user_returns_correct_name():
    """A legitimately good test, for contrast."""
    svc = UserService()
    svc.add(1, "alice")
    assert svc.get(1) == "alice"
