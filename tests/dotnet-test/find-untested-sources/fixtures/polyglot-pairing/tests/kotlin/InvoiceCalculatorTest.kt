package billing

class InvoiceCalculatorTest {
    fun total_is_sum() = InvoiceCalculator().total(2, 3) == 5
}
