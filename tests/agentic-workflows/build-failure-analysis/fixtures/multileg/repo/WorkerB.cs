namespace Demo;
public sealed class WorkerB
{
    public void Run(Buffer buffer) => buffer.Flush();
}
