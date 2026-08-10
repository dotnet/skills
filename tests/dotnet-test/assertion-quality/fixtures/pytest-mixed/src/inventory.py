class Inventory:
    def __init__(self, notifier=None):
        self._items = {}
        self._notifier = notifier

    def add(self, sku, quantity, price):
        if quantity <= 0:
            raise ValueError("quantity must be positive")
        self._items[sku] = {"quantity": quantity, "price": price}
        if self._notifier:
            self._notifier.stock_added(sku, quantity)

    def find(self, sku):
        return self._items.get(sku)

    def total(self):
        return sum(item["quantity"] * item["price"] for item in self._items.values())
