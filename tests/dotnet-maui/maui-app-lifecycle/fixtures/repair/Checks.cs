public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var state = new DraftState { Text = "draft" };
        state.Save(); state.Text = ""; state.Restore();
        Require(state.Text == "draft" && state.Saved == "draft", "resume overwrote persisted draft");
        state.Text = "latest"; state.Save(); state.Text = ""; state.Restore();
        Require(state.Text == "latest", "latest edit was lost");
        Console.WriteLine("PASS: behavior-contract");
    }
}
