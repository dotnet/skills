using System.Diagnostics;

namespace TimeTracking
{
    partial class FrmMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            dayTracking1 = new TimeTracking.Controls.DayTracking();
            SuspendLayout();
            // 
            // dayTracking1
            // 
            dayTracking1.BackColor = Color.SteelBlue;
            dayTracking1.BorderStyle = BorderStyle.FixedSingle;
            dayTracking1.Location = new Point(45, 42);
            dayTracking1.Name = "dayTracking1";
            dayTracking1.Size = new Size(703, 345);
            dayTracking1.TabIndex = 0;
            // 
            // FrmMain
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(dayTracking1);
            Name = "FrmMain";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private Controls.DayTracking dayTracking1;
    }
}
