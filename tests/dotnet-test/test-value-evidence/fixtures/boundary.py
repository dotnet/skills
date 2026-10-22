def shipping_cost(amount):
    """Orders of at least 100 ship free; other orders cost 10."""
    return 0 if amount > 100 else 10
