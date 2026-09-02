package calc

import "errors"

// Add returns the sum of a and b.
func Add(a, b int) int {
	return a + b
}

// Divide returns a/b, or an error when b is zero.
func Divide(a, b int) (int, error) {
	if b == 0 {
		return 0, errors.New("division by zero")
	}
	return a / b, nil
}

// Parse converts s to an int. It is intentionally simple for the fixture.
func Parse(s string) (int, error) {
	n := 0
	for _, r := range s {
		if r < '0' || r > '9' {
			return 0, errors.New("not a number")
		}
		n = n*10 + int(r-'0')
	}
	return n, nil
}

// Reset clears the accumulator (no return value).
func Reset() {}
