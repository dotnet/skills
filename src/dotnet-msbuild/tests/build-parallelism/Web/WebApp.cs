namespace Web;
public class WebApp
{
    private readonly Api.ApiHandler _handler = new();
    public string Serve(string request) => _handler.Handle(request);
}
