Imports System.Drawing
Imports System.Windows.Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainForm
    Inherits System.Windows.Forms.Form

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso components IsNot Nothing Then
            components.Dispose()
        End If

        MyBase.Dispose(disposing)
    End Sub

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        _statusLabel = New Label()
        SuspendLayout()
        _statusLabel.AutoSize = True
        _statusLabel.Location = New Point(12, 15)
        _statusLabel.Name = "_statusLabel"
        _statusLabel.Text = "Application ready"
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(320, 72)
        Controls.Add(_statusLabel)
        Name = "MainForm"
        Text = "VB Framework App"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Private _statusLabel As Label
End Class
