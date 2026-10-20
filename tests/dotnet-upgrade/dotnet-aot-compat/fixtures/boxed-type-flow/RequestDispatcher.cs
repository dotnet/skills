public interface IRequestHandler
{
    string Handle(string value);
}

public sealed class EchoHandler : IRequestHandler
{
    public string Handle(string value) => value.ToUpperInvariant();
}

public static class RequestDispatcher
{
    public static string Dispatch(object[] arguments)
    {
        var handlerType = (Type)arguments[0];
        var value = (string)arguments[1];
        var handler = (IRequestHandler)Activator.CreateInstance(handlerType)!;
        return handler.Handle(value);
    }
}
