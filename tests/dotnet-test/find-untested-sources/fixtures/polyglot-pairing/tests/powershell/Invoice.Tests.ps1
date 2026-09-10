Describe "Invoice" {
    It "calculates the total" {
        Get-InvoiceTotal -Subtotal 2 -Tax 3 | Should -Be 5
    }
}
