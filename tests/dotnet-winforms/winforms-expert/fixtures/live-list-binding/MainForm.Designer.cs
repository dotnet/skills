namespace LiveListBinding;

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
        _customerListBox = new ListBox();
        _addButton = new Button();
        SuspendLayout();
        _customerListBox.FormattingEnabled = true;
        _customerListBox.Location = new Point(12, 12);
        _customerListBox.Name = "_customerListBox";
        _customerListBox.Size = new Size(240, 124);
        _customerListBox.TabIndex = 0;
        _addButton.Location = new Point(177, 142);
        _addButton.Name = "_addButton";
        _addButton.Size = new Size(75, 23);
        _addButton.TabIndex = 1;
        _addButton.Text = "Add";
        _addButton.UseVisualStyleBackColor = true;
        _addButton.Click += AddButton_Click;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(264, 177);
        Controls.Add(_addButton);
        Controls.Add(_customerListBox);
        Name = "MainForm";
        Text = "Customers";
        ResumeLayout(false);
    }

    private ListBox _customerListBox;
    private Button _addButton;
}
