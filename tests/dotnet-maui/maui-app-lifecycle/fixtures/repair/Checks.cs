public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var state = new DraftState { Text = "draft" };
        var window = new Microsoft.Maui.Controls.Window();
        state.Attach(window);
        var lifecycle = (Microsoft.Maui.IWindow)window;
        lifecycle.Stopped();
        Require(state.Saved == "draft", "Stopped subscription did not save");
        state.Text = "";
        lifecycle.Resumed();
        Require(state.Text == "draft" && state.Saved == "draft", "resume overwrote persisted draft");
        state.Text = "latest";
        lifecycle.Destroying();
        Require(state.Saved == "latest", "Destroying subscription did not save");
        state.Text = ""; state.Restore();
        Require(state.Text == "latest", "latest edit was lost");
        Console.WriteLine("PASS: behavior-contract");
    }
}
