using System.Diagnostics.CodeAnalysis;

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
    public static string Dispatch(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type handlerType,
        object[] arguments)
    {
        var value = (string)arguments[0];
        var handler = (IRequestHandler)Activator.CreateInstance(handlerType)!;
        return handler.Handle(value);
    }
}
