namespace ShippingEditor;

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
        _contentLayout = new FlowLayoutPanel();
        _addressGroup = new GroupBox();
        _addressLayout = new TableLayoutPanel();
        _streetLabel = new Label();
        _streetTextBox = new TextBox();
        _cityLabel = new Label();
        _cityTextBox = new TextBox();
        _postalCodeLabel = new Label();
        _postalCodeTextBox = new TextBox();
        _saveButton = new Button();
        _contentLayout.SuspendLayout();
        _addressGroup.SuspendLayout();
        _addressLayout.SuspendLayout();
        SuspendLayout();
        _contentLayout.Controls.Add(_addressGroup);
        _contentLayout.Dock = DockStyle.Fill;
        _contentLayout.FlowDirection = FlowDirection.TopDown;
        _contentLayout.Location = new Point(0, 0);
        _contentLayout.Name = "_contentLayout";
        _contentLayout.Padding = new Padding(12);
        _contentLayout.Size = new Size(420, 176);
        _contentLayout.WrapContents = false;
        _addressGroup.Controls.Add(_addressLayout);
        _addressGroup.Name = "_addressGroup";
        _addressGroup.Size = new Size(376, 105);
        _addressGroup.Text = "Shipping address";
        _addressLayout.ColumnCount = 2;
        _addressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _addressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _addressLayout.Controls.Add(_streetLabel, 0, 0);
        _addressLayout.Controls.Add(_streetTextBox, 1, 0);
        _addressLayout.Controls.Add(_cityLabel, 0, 1);
        _addressLayout.Controls.Add(_cityTextBox, 1, 1);
        _addressLayout.Controls.Add(_postalCodeLabel, 0, 2);
        _addressLayout.Controls.Add(_postalCodeTextBox, 1, 2);
        _addressLayout.Dock = DockStyle.Fill;
        _addressLayout.Location = new Point(3, 19);
        _addressLayout.Name = "_addressLayout";
        _addressLayout.Padding = new Padding(8);
        _addressLayout.RowCount = 3;
        _addressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _addressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _addressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _addressLayout.Size = new Size(370, 83);
        _streetLabel.AutoSize = true;
        _streetLabel.Text = "Street";
        _streetTextBox.Dock = DockStyle.Fill;
        _streetTextBox.Name = "_streetTextBox";
        _cityLabel.AutoSize = true;
        _cityLabel.Text = "City";
        _cityTextBox.Dock = DockStyle.Fill;
        _cityTextBox.Name = "_cityTextBox";
        _postalCodeLabel.AutoSize = true;
        _postalCodeLabel.Text = "Postal code";
        _postalCodeTextBox.Dock = DockStyle.Fill;
        _postalCodeTextBox.Name = "_postalCodeTextBox";
        _saveButton.Location = new Point(333, 141);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(75, 23);
        _saveButton.Text = "Save";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(420, 176);
        Controls.Add(_saveButton);
        Controls.Add(_contentLayout);
        Name = "MainForm";
        Text = "Shipping Editor";
        _contentLayout.ResumeLayout(false);
        _addressGroup.ResumeLayout(false);
        _addressLayout.ResumeLayout(false);
        _addressLayout.PerformLayout();
        ResumeLayout(false);
    }

    private FlowLayoutPanel _contentLayout;
    private GroupBox _addressGroup;
    private TableLayoutPanel _addressLayout;
    private Label _streetLabel;
    private TextBox _streetTextBox;
    private Label _cityLabel;
    private TextBox _cityTextBox;
    private Label _postalCodeLabel;
    private TextBox _postalCodeTextBox;
    private Button _saveButton;
}
