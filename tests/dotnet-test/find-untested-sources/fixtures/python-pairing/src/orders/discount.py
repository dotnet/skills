def apply_discount(total: int, percent: int) -> int:
    return total - (total * percent // 100)
