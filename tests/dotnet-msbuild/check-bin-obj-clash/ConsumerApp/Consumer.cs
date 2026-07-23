namespace ConsumerApp;

public class Consumer
{
    public string Describe() => new ToolLib.Tool().GetName();
}
