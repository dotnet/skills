"""Running balance calculations."""

from decimal import Decimal
from typing import Iterable, Tuple


def running_balance(opening: Decimal, entries: Iterable[Tuple[str, Decimal]]) -> Decimal:
    """Apply credit/debit ``entries`` to ``opening`` and return the balance."""
    balance = opening
    for kind, amount in entries:
        if kind == "credit":
            balance += amount
        elif kind == "debit":
            balance -= amount
        else:
            raise ValueError(f"unknown entry kind: {kind}")
    return balance


def is_overdrawn(balance: Decimal, overdraft_limit: Decimal = Decimal("0")) -> bool:
    """Return True when ``balance`` is below the allowed overdraft."""
    return balance < -overdraft_limit
