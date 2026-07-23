package pricing

import "testing"

func TestTotal(t *testing.T) {
	if got := Total([]int{200, 300}); got != 500 {
		t.Fatalf("Total() = %d, want 500", got)
	}
}
