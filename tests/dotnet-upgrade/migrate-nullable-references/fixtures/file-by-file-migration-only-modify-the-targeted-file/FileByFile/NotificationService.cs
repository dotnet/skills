#nullable disable

namespace FileByFile;

public class NotificationService
{
    private string _endpoint;

    public NotificationService(string endpoint)
    {
        _endpoint = endpoint;
    }

    public void Send(string message, string recipient)
    {
    }

    public string GetLastError()
    {
        return null;
    }
}
