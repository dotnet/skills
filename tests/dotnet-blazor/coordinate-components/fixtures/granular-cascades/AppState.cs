public sealed class AppState
{
    public string Theme { get; set; } = "light";

    public string Locale { get; set; } = "en-US";

    public int CartItems { get; set; }

    public string ConnectionStatus { get; set; } = "Connected";
}
