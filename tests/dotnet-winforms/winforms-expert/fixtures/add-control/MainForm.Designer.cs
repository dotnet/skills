namespace CustomerEditor;

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
        _customerNameTextBox = new TextBox();
        _saveButton = new Button();
        SuspendLayout();
        _customerNameTextBox.Location = new Point(12, 12);
        _customerNameTextBox.Name = "_customerNameTextBox";
        _customerNameTextBox.Size = new Size(260, 23);
        _customerNameTextBox.TabIndex = 0;
        _saveButton.Location = new Point(197, 48);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(75, 23);
        _saveButton.TabIndex = 1;
        _saveButton.Text = "Save";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(284, 83);
        Controls.Add(_saveButton);
        Controls.Add(_customerNameTextBox);
        Name = "MainForm";
        Text = "Customer Editor";
        ResumeLayout(false);
        PerformLayout();
    }

    private TextBox _customerNameTextBox;
    private Button _saveButton;
}
