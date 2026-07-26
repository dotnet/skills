using System.Diagnostics.Metrics;

namespace SkillCatalog.Api.Services;

public sealed class CatalogTelemetry
{
    private readonly Meter _meter = new("SkillCatalog.Api");
    private readonly Counter<long> _downloads;
    private readonly Counter<long> _failures;
    private readonly Histogram<double> _searchDuration;
    private readonly Histogram<double> _submissionDuration;
    private readonly Counter<long> _submissionFindings;
    public CatalogTelemetry() { _downloads=_meter.CreateCounter<long>("catalog.downloads"); _failures=_meter.CreateCounter<long>("catalog.failures"); _searchDuration=_meter.CreateHistogram<double>("catalog.search.duration.ms"); _submissionDuration=_meter.CreateHistogram<double>("submission.duration.ms"); _submissionFindings=_meter.CreateCounter<long>("submission.findings"); }
    public void Download(string plugin) => _downloads.Add(1,new KeyValuePair<string,object?>("plugin",plugin));
    public void Failure(string operation) => _failures.Add(1,new KeyValuePair<string,object?>("operation",operation));
    public void Search(double milliseconds) => _searchDuration.Record(milliseconds);
    public void Submission(string operation, double milliseconds, int resources, int scenarios, IEnumerable<string> findingCodes) { _submissionDuration.Record(milliseconds, new("operation", operation), new("resources", resources), new("scenarios", scenarios)); foreach (var code in findingCodes) _submissionFindings.Add(1, new KeyValuePair<string, object?>("code", code)); }
}
