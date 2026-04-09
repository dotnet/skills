namespace LibB;

public class ServiceB
{
    public LibA.ServiceA Dep => new();
}
