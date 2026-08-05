from decimal import Decimal

from ledger.balance import is_overdrawn, running_balance


def test_running_balance_applies_credits_and_debits():
    result = running_balance(Decimal("100"), [("credit", Decimal("50")), ("debit", Decimal("20"))])
    assert result == Decimal("120")


def test_running_balance_rejects_unknown_kind():
    try:
        running_balance(Decimal("0"), [("transfer", Decimal("5"))])
    except ValueError:
        return
    raise AssertionError("expected ValueError")


def test_is_overdrawn_respects_overdraft_limit():
    assert is_overdrawn(Decimal("-50"), Decimal("100")) is True


def test_is_overdrawn_without_limit():
    assert is_overdrawn(Decimal("-1")) is True
