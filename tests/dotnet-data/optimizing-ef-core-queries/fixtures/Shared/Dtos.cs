namespace OptimizingEfCoreQueries.Shared;

// Small result shapes returned by the scenario solutions. Keeping them here
// (rather than in a solution file the agent edits) means the benchmark and the
// agent's rewrite always agree on the contract.

public record CustomerOrderSummary(string Name, int OrderCount, decimal Total);

public record ProductListItem(int Id, string Name, decimal Price);

public record InvoiceRow(int InvoiceId, string CustomerName, decimal Amount);

public record BlogWithChildren(string Name, int PostCount, int ContributorCount);

public record OrderRow(int Id, DateTime CreatedAt, decimal Total);
