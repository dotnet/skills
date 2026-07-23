package pricing

func ApplyDiscount(total int, percentage int) int {
	return total - total*percentage/100
}
