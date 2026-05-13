using System.Net;
class ApiClient
{
    void LogAgent(HttpListenerRequest request)
    {
        Console.WriteLine("UA length: " + request.UserAgent.Length);
    }
}
