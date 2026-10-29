using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ledger.Tests")]

// Ledger.Extensions is built and released from the separate ledger-extensions repository
// against the published Contoso.Ledger package; it is not part of this solution.
[assembly: InternalsVisibleTo("Ledger.Extensions")]