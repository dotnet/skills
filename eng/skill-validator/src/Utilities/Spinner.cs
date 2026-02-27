namespace SkillValidator.Utilities;

/// <summary>
/// Simple interactive spinner for terminal output.
/// </summary>
public sealed class Spinner : IDisposable
{
    private static readonly bool IsInteractive =
        Console.IsOutputRedirected is false &&
        Environment.GetEnvironmentVariable("CI") is null;

    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly Lock _lock = new();
    private Timer? _timer;
    private int _frame;
    private string _message = "";
    private bool _active;

    public void Start(string message)
    {
        lock (_lock)
        {
            _message = message;
            _active = true;
        }
        if (!IsInteractive)
        {
            Console.Error.WriteLine(message);
            return;
        }
        _frame = 0;
        Render();
        _timer = new Timer(_ =>
        {
            Interlocked.Increment(ref _frame);
            Render();
        }, null, 80, 80);
    }

    public void Update(string message)
    {
        lock (_lock) { _message = message; }
        if (!IsInteractive)
            Console.Error.WriteLine(message);
    }

    /// <summary>Write a log line without clobbering the spinner.</summary>
    public void Log(string text)
    {
        lock (_lock)
        {
            if (_active && IsInteractive)
            {
                Console.Error.Write($"\r\x1b[K{text}\n");
                Render();
            }
            else
            {
                Console.Error.WriteLine(text);
            }
        }
    }

    public void Stop(string? finalMessage = null)
    {
        lock (_lock) { _active = false; }
        _timer?.Dispose();
        _timer = null;
        if (IsInteractive)
            Console.Error.Write("\r\x1b[K");
        if (finalMessage is not null)
            Console.Error.WriteLine(finalMessage);
    }

    public void Dispose() => Stop();

    private void Render()
    {
        if (!IsInteractive) return;
        string msg;
        lock (_lock) { msg = _message; }
        var f = Frames[Interlocked.CompareExchange(ref _frame, 0, 0) % Frames.Length];
        Console.Error.Write($"\r\x1b[K\x1b[36m{f}\x1b[0m {msg}");
    }
}
