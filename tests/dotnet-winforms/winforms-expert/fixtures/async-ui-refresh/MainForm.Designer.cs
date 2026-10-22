namespace AsyncCustomerEditor;

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
        _refreshButton = new Button();
        _statusLabel = new Label();
        SuspendLayout();
        _refreshButton.Location = new Point(12, 12);
        _refreshButton.Name = "_refreshButton";
        _refreshButton.Size = new Size(90, 23);
        _refreshButton.Text = "Refresh";
        _refreshButton.UseVisualStyleBackColor = true;
        _refreshButton.Click += RefreshButton_Click;
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(12, 52);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Text = "Ready";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 92);
        Controls.Add(_statusLabel);
        Controls.Add(_refreshButton);
        Name = "MainForm";
        Text = "Customer Refresh";
        ResumeLayout(false);
        PerformLayout();
    }

    private Button _refreshButton;
    private Label _statusLabel;
}
