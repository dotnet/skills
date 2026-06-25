package calc

import "testing"

// ============================================================
// STRONG TEST: idiomatic table-driven test with subtests.
// The `for` loop and the `if got != tt.want` comparison are
// the canonical Go assertion pattern, NOT branching/conditional
// logic in the test under grade.
// Expected grade: A (90–100)
// ============================================================
func TestAdd_TableDriven(t *testing.T) {
	tests := []struct {
		name string
		a, b int
		want int
	}{
		{"positives", 2, 3, 5},
		{"with zero", 0, 7, 7},
		{"negatives", -4, -6, -10},
		{"mixed sign", -2, 5, 3},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := Add(tt.a, tt.b)
			if got != tt.want {
				t.Errorf("Add(%d, %d) = %d, want %d", tt.a, tt.b, got, tt.want)
			}
		})
	}
}

// ============================================================
// STRONG TEST: error path verified by checking the returned error.
// Expected grade: A (90–100)
// ============================================================
func TestDivide_ByZero(t *testing.T) {
	_, err := Divide(10, 0)
	if err == nil {
		t.Fatal("expected an error dividing by zero, got nil")
	}
}

// ============================================================
// WEAK TEST: only checks that no error came back — does not
// verify the parsed value. Trivial assertion.
// Expected grade: C (70–79)
// ============================================================
func TestParse_NoError(t *testing.T) {
	_, err := Parse("123")
	if err != nil {
		t.Errorf("unexpected error: %v", err)
	}
}

// ============================================================
// BAD TEST: calls the function but never asserts anything.
// Expected grade: F (0–59)
// ============================================================
func TestReset_NoAssertions(t *testing.T) {
	Reset()
}
