namespace DesignerConstructor;

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
        _customerNameLabel = new Label();
        SuspendLayout();
        _customerNameLabel.AutoSize = true;
        _customerNameLabel.Location = new Point(12, 16);
        _customerNameLabel.Name = "_customerNameLabel";
        _customerNameLabel.Text = "Customer";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(240, 48);
        Controls.Add(_customerNameLabel);
        Name = "MainForm";
        Text = "Customer";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label _customerNameLabel;
}
