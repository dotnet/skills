using Microsoft.Maui.Controls;
public sealed class DraftState
{
    public string Text { get; set; } = "";
    public string Saved { get; private set; } = "";
    public void Save() => Saved = Text;
    public void Restore() => Text = Saved;
    public void Attach(Window window)
    {
        window.Stopped += (_, _) => Save();
        window.Destroying += (_, _) => Save();
        window.Resumed += (_, _) => Restore();
    }
}
