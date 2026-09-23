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
