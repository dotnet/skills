from dataclasses import dataclass


@dataclass(frozen=True)
class Quote:
    cost: int
    express: bool


def quote(subtotal: int, express: bool = False) -> Quote:
    if subtotal < 0:
        raise ValueError("Subtotal must not be negative.")
    base_cost = 10 if subtotal < 100 else 0
    return Quote(base_cost + (5 if express else 0), express)


def parse_quantity(text: str) -> int:
    if text == "":
        raise ValueError("Quantity is required.")
    return int(text)
