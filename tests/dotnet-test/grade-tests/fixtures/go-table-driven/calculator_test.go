package calc

import "testing"

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

func TestDivide_ByZero(t *testing.T) {
	_, err := Divide(10, 0)
	if err == nil {
		t.Fatal("expected an error dividing by zero, got nil")
	}
}

func TestParse_NoError(t *testing.T) {
	_, err := Parse("123")
	if err != nil {
		t.Errorf("unexpected error: %v", err)
	}
}

func TestReset_NoAssertions(t *testing.T) {
	Reset()
}
