class Stock:
    def __init__(self, quantity):
        if quantity < 0:
            raise ValueError("quantity must be nonnegative")
        self.quantity = quantity

    def reserve(self, count):
        """Reject nonpositive requests; shortages return False without changing stock."""
        if count <= 0:
            raise ValueError("count must be positive")
        if count > self.quantity:
            return False
        self.quantity -= count
        return True
