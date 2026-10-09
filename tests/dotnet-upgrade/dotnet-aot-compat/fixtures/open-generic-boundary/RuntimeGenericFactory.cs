public static class RuntimeGenericFactory
{
    public static object Create(Type openGenericType, Type argumentType)
    {
        var closedType = openGenericType.MakeGenericType(argumentType);
        return Activator.CreateInstance(closedType)!;
    }
}
