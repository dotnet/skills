pub fn shipping_cost(subtotal: i32) -> i32 {
    if subtotal >= 50 { 0 } else { 10 }
}

#[cfg(test)]
mod tests {
    use super::shipping_cost;

    #[test]
    fn free_shipping_at_threshold() {
        assert_eq!(shipping_cost(50), 10);
    }
}
