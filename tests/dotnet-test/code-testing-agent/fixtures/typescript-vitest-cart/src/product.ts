export interface Product {
  id: string;
  name: string;
  unitPriceCents: number;
}

export interface CartLine {
  product: Product;
  quantity: number;
}
