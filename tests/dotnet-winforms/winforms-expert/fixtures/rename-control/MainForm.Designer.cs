namespace OrderEntry;

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
        _orderNumberTextBox = new TextBox();
        _saveButton = new Button();
        _statusLabel = new Label();
        SuspendLayout();
        _orderNumberTextBox.Location = new Point(12, 12);
        _orderNumberTextBox.Name = "_orderNumberTextBox";
        _orderNumberTextBox.Size = new Size(220, 23);
        _saveButton.Location = new Point(157, 47);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(75, 23);
        _saveButton.Text = "Commit";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(12, 51);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(39, 15);
        _statusLabel.Text = "Ready";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(244, 82);
        Controls.Add(_statusLabel);
        Controls.Add(_saveButton);
        Controls.Add(_orderNumberTextBox);
        Name = "MainForm";
        Text = "Order Entry";
        ResumeLayout(false);
        PerformLayout();
    }

    private TextBox _orderNumberTextBox;
    private Button _saveButton;
    private Label _statusLabel;
}
