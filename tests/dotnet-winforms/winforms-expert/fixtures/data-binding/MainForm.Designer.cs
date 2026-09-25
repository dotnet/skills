namespace CustomerBinding;

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
        _nameLabel = new Label();
        _nameTextBox = new TextBox();
        SuspendLayout();
        _nameLabel.AutoSize = true;
        _nameLabel.Location = new Point(12, 15);
        _nameLabel.Name = "_nameLabel";
        _nameLabel.Size = new Size(39, 15);
        _nameLabel.Text = "Name";
        _nameTextBox.Location = new Point(62, 12);
        _nameTextBox.Name = "_nameTextBox";
        _nameTextBox.Size = new Size(210, 23);
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(284, 51);
        Controls.Add(_nameTextBox);
        Controls.Add(_nameLabel);
        Name = "MainForm";
        Text = "Customer";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _nameLabel;
    private TextBox _nameTextBox;
}
