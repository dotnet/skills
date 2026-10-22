package money

import "fmt"

func Discount(subtotalCents int, percent int) (int, error) {
	if subtotalCents < 0 {
		return 0, fmt.Errorf("subtotal must not be negative")
	}
	if percent < 0 || percent > 100 {
		return 0, fmt.Errorf("percent must be between 0 and 100")
	}

	return subtotalCents * percent / 100, nil
}
