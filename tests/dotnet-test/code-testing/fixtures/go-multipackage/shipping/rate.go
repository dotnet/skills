package shipping

import "fmt"

type Bracket struct {
	UpToGrams int
	Cents     int
}

func Rate(weightGrams int, brackets []Bracket) (int, error) {
	if weightGrams < 0 {
		return 0, fmt.Errorf("weight must not be negative")
	}
	if len(brackets) == 0 {
		return 0, fmt.Errorf("at least one bracket is required")
	}

	for _, bracket := range brackets {
		if weightGrams <= bracket.UpToGrams {
			return bracket.Cents, nil
		}
	}

	return brackets[len(brackets)-1].Cents, nil
}
