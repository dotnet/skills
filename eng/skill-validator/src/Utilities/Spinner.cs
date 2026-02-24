namespace SkillValidator.Utilities;

/// <summary>
/// Simple interactive spinner for terminal output.
/// </summary>
public sealed class Spinner
{
    private static readonly bool IsInteractive =
        Console.IsOutputRedirected is false &&
        Environment.GetEnvironmentVariable("CI") is null;

    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private Timer? _timer;
    private int _frame;
    private string _message = "";
    private bool _active;

    public void Start(string message)
    {
        _message = message;
        _active = true;
        if (!IsInteractive)
        {
            Console.Error.WriteLine(message);
            return;
        }
        _frame = 0;
        Render();
        _timer = new Timer(_ =>
        {
            _frame++;
            Render();
        }, null, 80, 80);
    }

    public void Update(string message)
    {
        _message = message;
        if (!IsInteractive)
            Console.Error.WriteLine(message);
    }

    /// <summary>Write a log line without clobbering the spinner.</summary>
    public void Log(string text)
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

    public void Stop(string? finalMessage = null)
    {
        _active = false;
        _timer?.Dispose();
        _timer = null;
        if (IsInteractive)
            Console.Error.Write("\r\x1b[K");
        if (finalMessage is not null)
            Console.Error.WriteLine(finalMessage);
    }

    private void Render()
    {
        if (!IsInteractive) return;
        var f = Frames[_frame % Frames.Length];
        Console.Error.Write($"\r\x1b[K\x1b[36m{f}\x1b[0m {_message}");
    }
}
