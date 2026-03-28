# Evaluations

Evaluations measure how well an LLM uses your MCP server's tools to answer questions.

## Evaluation Format

Use XML with `qa_pair` elements:

```xml
<evaluations>
  <qa_pair>
    <question>What are the top 3 products in the Electronics category by price?</question>
    <answer>The top 3 products are: 1. Ultra HD TV ($1,299), 2. Gaming Laptop ($1,199), 3. Wireless Headphones ($349).</answer>
  </qa_pair>
  <qa_pair>
    <question>How many products cost less than $50 and are currently in stock?</question>
    <answer>There are 12 products under $50 that are currently in stock.</answer>
  </qa_pair>
</evaluations>
```

## Writing Good Evaluation Questions

**Questions must be read-only, non-destructive, and deterministic.** The LLM should be able to verify its answer against a known correct result without modifying any data.

### Principles

| Principle | Good Example | Bad Example |
|-----------|-------------|-------------|
| Read-only | "List all products in category X" | "Add a new product to category X" |
| Deterministic | "What is the price of product #123?" | "What's a good product to buy?" |
| Multi-tool | "Which category has the highest average price?" (requires listing categories + querying each) | "What is product #1?" (single lookup) |
| Verifiable | "How many products have rating > 4.5?" | "Describe the best products" |

### Design Tips

- **Require reasoning across multiple tool calls** — e.g., "Compare the average price of Electronics vs. Books" needs two category queries plus computation.
- **Include edge cases** — empty results, boundary values, special characters in search terms.
- **Vary complexity** — mix simple single-tool questions with multi-step reasoning chains.
- **Answers must be precise** — include exact values, counts, or names that can be verified programmatically.

### Example: Product Catalog Evaluations

```xml
<evaluations>
  <qa_pair>
    <question>What is the most expensive product across all categories?</question>
    <answer>The most expensive product is the Ultra HD TV in Electronics at $1,299.99.</answer>
  </qa_pair>
  <qa_pair>
    <question>Which categories contain products that are out of stock? List the category names and the count of out-of-stock items in each.</question>
    <answer>Electronics has 3 out-of-stock items, Books has 1 out-of-stock item.</answer>
  </qa_pair>
  <qa_pair>
    <question>Find all products with "wireless" in the name and sort them by price ascending.</question>
    <answer>1. Wireless Mouse ($24.99), 2. Wireless Keyboard ($49.99), 3. Wireless Headphones ($349.00).</answer>
  </qa_pair>
</evaluations>
```
