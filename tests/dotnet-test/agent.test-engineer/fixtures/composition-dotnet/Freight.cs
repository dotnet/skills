namespace Dispatch;

public static class Freight
{
    public static int Quote(bool remote)
    {
        return remote ? 13 : 5;
    }
}
