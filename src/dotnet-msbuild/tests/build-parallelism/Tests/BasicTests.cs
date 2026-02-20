namespace Tests;
public class BasicTests
{
    public bool TestProcess()
    {
        var app = new Web.WebApp();
        return app.Serve("hello") == "HELLO";
    }
}
