namespace LocalizedEditor;

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
        _saveButton = new Button();
        SuspendLayout();
        _saveButton.Location = new Point(97, 46);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(75, 23);
        _saveButton.TabIndex = 0;
        _saveButton.Text = "Save";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        AcceptButton = _saveButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(184, 81);
        Controls.Add(_saveButton);
        Name = "MainForm";
        Text = "Customer Editor";
        ResumeLayout(false);
    }

    private Button _saveButton;
}
