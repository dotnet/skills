package calculator

import "testing"

func TestAdd_ValidInputs_ReturnsSum(t *testing.T) {
	if got := Add(2, 3); got != 5 {
		t.Fatalf("expected 5, got %d", got)
	}
}

func TestDivide_ValidInputs_ReturnsQuotient(t *testing.T) {
	got, err := Divide(10, 2)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if got != 5 {
		t.Fatalf("expected 5, got %d", got)
	}
}

func TestDivide_ByZero_ReturnsError(t *testing.T) {
	if _, err := Divide(1, 0); err == nil {
		t.Fatal("expected an error when dividing by zero")
	}
}

func TestAdd_ZeroOperands_ReturnsZero(t *testing.T) {
	if got := Add(0, 0); got != 0 {
		t.Fatalf("expected 0, got %d", got)
	}
}
