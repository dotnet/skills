function Get-InvoiceTotal {
    param([int]$Subtotal, [int]$Tax)
    return $Subtotal + $Tax
}
