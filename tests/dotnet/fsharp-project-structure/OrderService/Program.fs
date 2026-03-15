open OrderService.Domain
open OrderService.Services

let order = {
    Id = OrderId 1
    CustomerName = "Alice"
    Items = [
        { Name = "Widget"; Quantity = 2; Price = 9.99m }
        { Name = "Gadget"; Quantity = 1; Price = 24.99m }
    ]
}

let total = processOrder order
printfn "Order total: $%M" total
