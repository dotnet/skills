using System.Text;

namespace DockDispatch;

public static class ScalarLexeme
{
    public static string Quote(string value)
    {
        var text = new StringBuilder();
        text.Append('"');
        text.Append(value);
        text.Append('"');
        return text.ToString();
    }
}

public static class CalibrationExport
{
    public static Task Emit(DeliveryChannel channel, string reading)
    {
        return channel.SendAsync(Encoding.UTF8.GetBytes(ScalarLexeme.Quote(reading)));
    }
}
