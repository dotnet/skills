var result = await GreetingStore.GetGreetingAsync("Ada");

if (result != "Hello, Ada")
{
    throw new InvalidOperationException("The greeting changed.");
}

Console.WriteLine("simple-safe-ok");
