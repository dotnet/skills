namespace ComponentOwnership;

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
        _statusLabel = new Label();
        _refreshTimer = new System.Windows.Forms.Timer();
        SuspendLayout();
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(12, 16);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Text = "Waiting";
        _refreshTimer.Interval = 1000;
        _refreshTimer.Tick += RefreshTimer_Tick;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(220, 48);
        Controls.Add(_statusLabel);
        Name = "MainForm";
        Text = "Timer";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _statusLabel;
    private System.Windows.Forms.Timer _refreshTimer;
}
