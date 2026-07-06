/// Parses a "quantity,price" line into a total cost.
///
/// Uses the `?` operator to short-circuit on parse errors. If either field
/// fails to parse, the error is propagated to the caller instead of panicking.
pub fn parse_line_total(line: &str) -> Result<u64, std::num::ParseIntError> {
    let mut parts = line.split(',');
    let quantity: u64 = parts.next().unwrap_or("").trim().parse()?;
    let price: u64 = parts.next().unwrap_or("").trim().parse()?;
    Ok(quantity * price)
}

/// Returns the first stock level at or below the reorder threshold.
pub fn first_below_threshold(levels: &[u32], threshold: u32) -> Option<u32> {
    levels.iter().copied().find(|&l| l <= threshold)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_a_valid_line() {
        assert_eq!(parse_line_total("3, 10").unwrap(), 30);
    }

    // Note: no test exercises the error path of parse_line_total, so the `?`
    // propagation is never observed by the suite.

    #[test]
    fn finds_a_value_below_threshold() {
        assert_eq!(first_below_threshold(&[9, 5, 2], 5), Some(5));
    }
}
