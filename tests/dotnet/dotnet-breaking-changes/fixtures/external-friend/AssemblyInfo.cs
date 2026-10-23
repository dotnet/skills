using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Billing.Tests")]
// Billing.Plugins is built and released from the separate billing-plugins repository against the
// published Billing package; it is not part of this solution.
[assembly: InternalsVisibleTo("Billing.Plugins")]
