namespace ContactEditor;

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
        _firstNameLabel = new Label();
        _firstNameTextBox = new TextBox();
        _lastNameLabel = new Label();
        _lastNameTextBox = new TextBox();
        _saveButton = new Button();
        SuspendLayout();
        _firstNameLabel.AutoSize = true;
        _firstNameLabel.Location = new Point(12, 15);
        _firstNameLabel.Name = "_firstNameLabel";
        _firstNameLabel.Size = new Size(67, 15);
        _firstNameLabel.Text = "First name";
        _firstNameTextBox.Location = new Point(92, 12);
        _firstNameTextBox.Name = "_firstNameTextBox";
        _firstNameTextBox.Size = new Size(240, 23);
        _lastNameLabel.AutoSize = true;
        _lastNameLabel.Location = new Point(12, 47);
        _lastNameLabel.Name = "_lastNameLabel";
        _lastNameLabel.Size = new Size(66, 15);
        _lastNameLabel.Text = "Last name";
        _lastNameTextBox.Location = new Point(92, 44);
        _lastNameTextBox.Name = "_lastNameTextBox";
        _lastNameTextBox.Size = new Size(240, 23);
        _saveButton.Location = new Point(257, 82);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(75, 23);
        _saveButton.Text = "Save";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(344, 117);
        Controls.Add(_saveButton);
        Controls.Add(_lastNameTextBox);
        Controls.Add(_lastNameLabel);
        Controls.Add(_firstNameTextBox);
        Controls.Add(_firstNameLabel);
        MinimumSize = new Size(360, 156);
        Name = "MainForm";
        Text = "Contact Editor";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _firstNameLabel;
    private TextBox _firstNameTextBox;
    private Label _lastNameLabel;
    private TextBox _lastNameTextBox;
    private Button _saveButton;
}
