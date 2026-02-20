namespace Api;
public class ApiHandler
{
    private readonly Core.CoreService _service = new();
    public string Handle(string request) => _service.Process(request);
}
