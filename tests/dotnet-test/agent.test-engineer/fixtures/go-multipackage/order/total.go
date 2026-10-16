package order

import "fmt"

type DiscountPolicy interface {
	Discount(subtotalCents int) (int, error)
}

type ShippingCalculator interface {
	Rate(weightGrams int) (int, error)
}

func Total(
	subtotalCents int,
	weightGrams int,
	discountPolicy DiscountPolicy,
	shippingCalculator ShippingCalculator,
) (int, error) {
	if subtotalCents < 0 {
		return 0, fmt.Errorf("subtotal must not be negative")
	}

	discount, err := discountPolicy.Discount(subtotalCents)
	if err != nil {
		return 0, err
	}
	shipping, err := shippingCalculator.Rate(weightGrams)
	if err != nil {
		return 0, err
	}

	return subtotalCents - discount + shipping, nil
}
