using App1;
using App2;
using App3;
using App4;

namespace Aggregator;

public class AggregatorReport
{
    public App1Feature First { get; } = new();
    public App2Feature Second { get; } = new();
    public App3Feature Third { get; } = new();
    public App4Feature Fourth { get; } = new();
}
