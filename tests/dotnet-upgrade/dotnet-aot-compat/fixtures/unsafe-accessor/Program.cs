var counter = new VendorCounter();
counter.Increment();
counter.Increment();

if (CounterInterop.Read(counter) != 2)
{
    throw new InvalidOperationException("Interop behavior changed.");
}

Console.WriteLine("PASS");
