namespace InitializationBalance;

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
        _ordersGrid = new DataGridView();
        ((System.ComponentModel.ISupportInitialize)_ordersGrid).BeginInit();
        SuspendLayout();
        _ordersGrid.AllowUserToAddRows = false;
        _ordersGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _ordersGrid.Dock = DockStyle.Fill;
        _ordersGrid.Name = "_ordersGrid";
        _ordersGrid.RowTemplate.Height = 25;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(420, 240);
        Controls.Add(_ordersGrid);
        Name = "MainForm";
        Text = "Orders";
        ResumeLayout(false);
    }

    private DataGridView _ordersGrid;
}
