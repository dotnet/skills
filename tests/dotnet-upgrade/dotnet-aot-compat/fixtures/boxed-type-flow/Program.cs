var result = RequestDispatcher.Dispatch(new object[] { typeof(EchoHandler), "hello" });

if (result != "HELLO")
{
    throw new InvalidOperationException("Dispatch behavior changed.");
}

Console.WriteLine("PASS");
