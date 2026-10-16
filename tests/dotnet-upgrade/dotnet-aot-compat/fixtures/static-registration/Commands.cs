public interface ICommand
{
    string Name { get; }
    int Execute();
}

public sealed class AddCommand : ICommand
{
    public string Name => "add";
    public int Execute() => 7;
}

public sealed class MultiplyCommand : ICommand
{
    public string Name => "multiply";
    public int Execute() => 12;
}
