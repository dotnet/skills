namespace Dashboard;

partial class MainForm
{
    private System.ComponentModel.IContainer components;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (components != null)
            {
                components.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _statusBadge = new StatusBadge();
        SuspendLayout();
        _statusBadge.Location = new Point(12, 12);
        _statusBadge.Name = "_statusBadge";
        _statusBadge.Size = new Size(180, 40);
        _statusBadge.StatusText = "Online";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(204, 64);
        Controls.Add(_statusBadge);
        Name = "MainForm";
        Text = "Dashboard";
        ResumeLayout(false);
    }

    private StatusBadge _statusBadge;
}
