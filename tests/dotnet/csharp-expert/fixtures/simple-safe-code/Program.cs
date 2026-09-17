var result = await GreetingStore.GetGreetingAsync("Ada");

if (result != "Hello, Ada")
{
    throw new InvalidOperationException("The greeting changed.");
}

Console.WriteLine("simple-safe-ok");

internal static class GreetingStore
{
    public static Task<string> GetGreetingAsync(string name)
    {
        return Task.FromResult($"Hello, {name}");
    }
}
