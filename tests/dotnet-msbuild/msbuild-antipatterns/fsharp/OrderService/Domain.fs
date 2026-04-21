module OrderService.Domain

type OrderId = OrderId of int

type OrderItem = {
    Name: string
    Quantity: int
    Price: decimal
}

type Order = {
    Id: OrderId
    CustomerName: string
    Items: OrderItem list
}
