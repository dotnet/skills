namespace Demo;
public sealed class WorkerA
{
    public void Run(Buffer buffer) => buffer.Flush();
}
