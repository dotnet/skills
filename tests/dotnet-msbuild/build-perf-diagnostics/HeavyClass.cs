namespace AnalyzerHeavy;

/// <summary>
/// A class with enough code to make analyzers do meaningful work.
/// </summary>
public class HeavyClass
{
    private readonly Dictionary<string, List<int>> _data = new();

    public void AddValues(string key, params int[] values)
    {
        if (!_data.ContainsKey(key))
            _data[key] = new List<int>();
        _data[key].AddRange(values);
    }

    public double GetAverage(string key)
    {
        if (!_data.TryGetValue(key, out var values) || values.Count == 0)
            return 0.0;
        return values.Average();
    }

    public IReadOnlyDictionary<string, int> GetCounts()
    {
        return _data.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (key, values) in _data)
        {
            sb.AppendLine($"{key}: count={values.Count}, avg={values.Average():F2}, min={values.Min()}, max={values.Max()}");
        }
        return sb.ToString();
    }
}
