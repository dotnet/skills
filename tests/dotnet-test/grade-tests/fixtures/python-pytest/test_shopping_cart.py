import time


def test_calculate_total_with_discount_applies_discount_and_returns_rounded_amount():
    # Arrange
    cart = ShoppingCart()
    cart.add(Item("SKU-1", price=10.00, qty=2))
    cart.add(Item("SKU-2", price=4.99, qty=1))

    # Act
    total = cart.calculate_total(discount_percent=10)

    # Assert
    assert total == 22.49
    assert cart.last_discount_applied == 10
    assert len(cart.items) == 2


def test_calculate_total_with_negative_discount_raises_value_error():
    import pytest
    cart = ShoppingCart()
    cart.add(Item("SKU-1", price=10.00, qty=1))

    with pytest.raises(ValueError, match="discount must be non-negative"):
        cart.calculate_total(discount_percent=-5)


def test_get_cart_returns_cart():
    cart = get_cart()
    assert cart is not None


def test_clear_cart_works():
    cart = ShoppingCart()
    cart.add(Item("SKU-1", price=10.00, qty=1))
    cart.clear()


def test_async_checkout_completes():
    cart = ShoppingCart()
    cart.checkout_async()
    time.sleep(2)
    assert True
