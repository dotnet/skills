package calculator

import "errors"

// ErrDivideByZero is returned when a division by zero is attempted.
var ErrDivideByZero = errors.New("divide by zero")

// Add returns the sum of two integers.
func Add(a, b int) int {
	return a + b
}

// Divide returns the quotient of a and b, or ErrDivideByZero when b is zero.
func Divide(a, b int) (int, error) {
	if b == 0 {
		return 0, ErrDivideByZero
	}
	return a / b, nil
}
