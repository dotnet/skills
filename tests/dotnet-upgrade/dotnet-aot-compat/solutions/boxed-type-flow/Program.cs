var result = RequestDispatcher.Dispatch(typeof(EchoHandler), new object[] { "hello" });

if (result != "HELLO")
{
    throw new InvalidOperationException("Dispatch behavior changed.");
}

Console.WriteLine("PASS");
