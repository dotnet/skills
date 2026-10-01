# Shopping cart

This library models a shopping cart with product lines, discounts, tax,
shipping, inventory availability, and checkout.

## Domain behavior

- Products have stable identifiers, prices in cents, and optional weight and
  currency data.
- A cart can add, remove, update, clear, count, and snapshot product lines.
- Re-adding a product increases its quantity. Updating a quantity to zero
  removes the product.
- Totals are calculated in this order: subtotal, discount, tax on the
  discounted subtotal, shipping, and final total.
- Discount policies support no discount, percentages, fixed amounts, and
  composite sum or chain behavior.
- Tax calculation supports fixed regional rates or rates obtained
  asynchronously.
- Shipping supports free, flat-rate, threshold-based, and weight-bracket
  calculations.
- Checkout can refresh product prices, verify inventory, and resolve a tax
  rate before returning an immutable line snapshot and final totals.
- Invalid prices, quantities, rates, discounts, and availability results are
  rejected with descriptive errors.
