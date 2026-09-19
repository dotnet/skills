package billing

class InvoiceCalculator {
    fun total(subtotal: Int, tax: Int): Int = subtotal + tax
}
