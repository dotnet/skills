module OrderService.Services

open OrderService.Domain

let calculateTotal (order: Order) =
    order.Items
    |> List.sumBy (fun item -> item.Price * decimal item.Quantity)

let processOrder (order: Order) =
    let total = calculateTotal order
    printfn "Processing order for %s: $%M" order.CustomerName total
    total
