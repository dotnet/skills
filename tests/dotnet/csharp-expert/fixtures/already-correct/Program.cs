var lines = await LineReader.ReadNonEmptyLinesAsync(
    new StringReader("first\n\nsecond\n"),
    CancellationToken.None);

if (!lines.SequenceEqual(["first", "second"]))
{
    throw new InvalidOperationException("The lines were not returned in order.");
}

using var cancellation = new CancellationTokenSource();
cancellation.Cancel();

try
{
    await LineReader.ReadNonEmptyLinesAsync(
        new StringReader("unread"),
        cancellation.Token);
    throw new InvalidOperationException("Cancellation was not observed.");
}
catch (OperationCanceledException)
{
}

Console.WriteLine("already-correct");
