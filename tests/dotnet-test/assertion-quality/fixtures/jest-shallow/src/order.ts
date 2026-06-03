export interface Order {
  id: string;
  customer: string;
  itemCount: number;
  totalCents: number;
  status: 'pending' | 'confirmed' | 'cancelled';
}

export class OrderService {
  private readonly orders = new Map<string, Order>();

  create(customer: string, itemCount: number, totalCents: number): Order {
    if (!customer) throw new Error('customer is required');
    if (itemCount <= 0) throw new Error('itemCount must be positive');
    if (totalCents < 0) throw new Error('totalCents must be non-negative');
    const order: Order = {
      id: `o-${this.orders.size + 1}`,
      customer,
      itemCount,
      totalCents,
      status: 'pending',
    };
    this.orders.set(order.id, order);
    return order;
  }

  confirm(id: string): Order {
    const order = this.orders.get(id);
    if (!order) throw new Error('order not found');
    order.status = 'confirmed';
    return order;
  }

  list(): Order[] {
    return Array.from(this.orders.values());
  }
}
