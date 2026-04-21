namespace Notifications;

public interface ITemplateEngine
{
    string Render(string templateName, Dictionary<string, string> variables);
}

public interface ISmsSender
{
    bool Send(string phoneNumber, string message);
}

public interface IPushNotifier
{
    void SendPush(string deviceToken, string title, string body);
}

public record NotificationRequest(string UserId, string Channel, string TemplateName, Dictionary<string, string> Variables);

public class NotificationDispatcher
{
    private readonly ITemplateEngine _templateEngine;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotifier _pushNotifier;

    public NotificationDispatcher(ITemplateEngine templateEngine, ISmsSender smsSender, IPushNotifier pushNotifier)
    {
        _templateEngine = templateEngine;
        _smsSender = smsSender;
        _pushNotifier = pushNotifier;
    }

    public bool Dispatch(NotificationRequest request, string contactInfo)
    {
        var content = _templateEngine.Render(request.TemplateName, request.Variables);

        return request.Channel switch
        {
            "sms" => _smsSender.Send(contactInfo, content),
            "push" => SendPush(contactInfo, content),
            _ => throw new ArgumentException($"Unknown channel: {request.Channel}")
        };
    }

    private bool SendPush(string deviceToken, string content)
    {
        _pushNotifier.SendPush(deviceToken, "Notification", content);
        return true;
    }
}
