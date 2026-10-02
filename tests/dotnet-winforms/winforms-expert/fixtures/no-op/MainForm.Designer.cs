namespace SafeDesigner;

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
        _refreshButton = new Button();
        SuspendLayout();
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(12, 17);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(39, 15);
        _statusLabel.TabIndex = 0;
        _statusLabel.Text = "Ready";
        _refreshButton.Location = new Point(117, 12);
        _refreshButton.Name = "_refreshButton";
        _refreshButton.Size = new Size(75, 23);
        _refreshButton.TabIndex = 1;
        _refreshButton.Text = "Refresh";
        _refreshButton.UseVisualStyleBackColor = true;
        _refreshButton.Click += RefreshButton_Click;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(204, 47);
        Controls.Add(_refreshButton);
        Controls.Add(_statusLabel);
        Name = "MainForm";
        Text = "Status";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _statusLabel;
    private Button _refreshButton;
}
