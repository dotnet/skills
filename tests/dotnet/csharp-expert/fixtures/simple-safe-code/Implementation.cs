internal static class GreetingStore
{
    public static Task<string> GetGreetingAsync(string name)
    {
        return Task.FromResult($"Hello, {name}");
    }
}
