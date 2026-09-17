var counter = new AsyncCounter();
var increments = Enumerable.Range(0, 1_000)
    .Select(_ => counter.IncrementAsync())
    .ToArray();

await Task.WhenAll(increments);

if (counter.Value != increments.Length)
{
    throw new InvalidOperationException($"Expected {increments.Length}, actual {counter.Value}.");
}

Console.WriteLine("concurrency-ok");

internal sealed class AsyncCounter
{
    private int _value;

    public int Value => _value;

    public async Task IncrementAsync()
    {
        var current = _value;
        await Task.Yield();
        _value = current + 1;
    }
}
