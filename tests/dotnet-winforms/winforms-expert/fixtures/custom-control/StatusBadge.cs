namespace Dashboard;

public sealed class StatusBadge : Control
{
    public string StatusText { get; set; } = string.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, StatusText, Font, ClientRectangle, ForeColor);
    }
}
