namespace CSharpWinForms;

partial class Form1
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
        if (disposing && (components != null)) {
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
        btnCheckUpdate = new Button();
        btnDownloadUpdate = new Button();
        btnRestartApply = new Button();
        txtTextLog = new TextBox();
        lblStatus = new Label();
        SuspendLayout();
        // 
        // btnCheckUpdate
        // 
        btnCheckUpdate.Anchor =  AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        btnCheckUpdate.Location = new Point(12, 96);
        btnCheckUpdate.Name = "btnCheckUpdate";
        btnCheckUpdate.Size = new Size(520, 23);
        btnCheckUpdate.TabIndex = 0;
        btnCheckUpdate.Text = "Check for Updates";
        btnCheckUpdate.UseVisualStyleBackColor = true;
        btnCheckUpdate.Click += btnCheckUpdate_Click;
        // 
        // btnDownloadUpdate
        // 
        btnDownloadUpdate.Anchor =  AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        btnDownloadUpdate.Enabled = false;
        btnDownloadUpdate.Location = new Point(12, 125);
        btnDownloadUpdate.Name = "btnDownloadUpdate";
        btnDownloadUpdate.Size = new Size(520, 23);
        btnDownloadUpdate.TabIndex = 1;
        btnDownloadUpdate.Text = "Download";
        btnDownloadUpdate.UseVisualStyleBackColor = true;
        btnDownloadUpdate.Click += btnDownloadUpdate_Click;
        // 
        // btnRestartApply
        // 
        btnRestartApply.Anchor =  AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        btnRestartApply.Enabled = false;
        btnRestartApply.Location = new Point(12, 154);
        btnRestartApply.Name = "btnRestartApply";
        btnRestartApply.Size = new Size(520, 23);
        btnRestartApply.TabIndex = 2;
        btnRestartApply.Text = "Restart && Apply";
        btnRestartApply.UseVisualStyleBackColor = true;
        btnRestartApply.Click += btnRestartApply_Click;
        // 
        // txtTextLog
        // 
        txtTextLog.Anchor =  AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtTextLog.Location = new Point(12, 183);
        txtTextLog.Multiline = true;
        txtTextLog.Name = "txtTextLog";
        txtTextLog.ScrollBars = ScrollBars.Vertical;
        txtTextLog.Size = new Size(520, 167);
        txtTextLog.TabIndex = 3;
        // 
        // lblStatus
        // 
        lblStatus.Anchor =  AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblStatus.AutoSize = true;
        lblStatus.Location = new Point(12, 9);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(0, 15);
        lblStatus.TabIndex = 4;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(544, 368);
        Controls.Add(lblStatus);
        Controls.Add(txtTextLog);
        Controls.Add(btnRestartApply);
        Controls.Add(btnDownloadUpdate);
        Controls.Add(btnCheckUpdate);
        Name = "Form1";
        Text = "Velopack Sample";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Button btnCheckUpdate;
    private Button btnDownloadUpdate;
    private Button btnRestartApply;
    private TextBox txtTextLog;
    private Label lblStatus;
}
