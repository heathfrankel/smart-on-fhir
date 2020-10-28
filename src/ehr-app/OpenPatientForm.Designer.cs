namespace EHRApp
{
    partial class OpenPatientForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OpenPatientForm));
            this.patientListView = new System.Windows.Forms.ListView();
            this.colPatientId = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colBirthdate = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colAge = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.openPatientButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.findWhoTextBox = new System.Windows.Forms.TextBox();
            this.findWhoLabel = new System.Windows.Forms.Label();
            this.findWhoButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // patientListView
            // 
            this.patientListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.patientListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colPatientId,
            this.colName,
            this.colBirthdate,
            this.colAge});
            this.patientListView.FullRowSelect = true;
            this.patientListView.HideSelection = false;
            this.patientListView.Location = new System.Drawing.Point(0, 48);
            this.patientListView.Name = "patientListView";
            this.patientListView.Size = new System.Drawing.Size(815, 591);
            this.patientListView.TabIndex = 3;
            this.patientListView.UseCompatibleStateImageBehavior = false;
            this.patientListView.View = System.Windows.Forms.View.Details;
            this.patientListView.SelectedIndexChanged += new System.EventHandler(this.PatientListViewOnSelectedIndexChanged);
            this.patientListView.DoubleClick += new System.EventHandler(this.patientListView_DoubleClick);
            // 
            // colPatientId
            // 
            this.colPatientId.Text = "Patient Id";
            this.colPatientId.Width = 167;
            // 
            // colName
            // 
            this.colName.Text = "Name";
            this.colName.Width = 251;
            // 
            // colBirthdate
            // 
            this.colBirthdate.Text = "Birthdate";
            this.colBirthdate.Width = 128;
            // 
            // colAge
            // 
            this.colAge.Text = "Age";
            this.colAge.Width = 106;
            // 
            // openPatientButton
            // 
            this.openPatientButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.openPatientButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.openPatientButton.Location = new System.Drawing.Point(651, 647);
            this.openPatientButton.Name = "openPatientButton";
            this.openPatientButton.Size = new System.Drawing.Size(75, 34);
            this.openPatientButton.TabIndex = 4;
            this.openPatientButton.Text = "&Open";
            this.openPatientButton.UseVisualStyleBackColor = true;
            this.openPatientButton.Click += new System.EventHandler(this.OpenPatientOnClick);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(731, 647);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(75, 34);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "&Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.CancelButtonOnClick);
            // 
            // findWhoTextBox
            // 
            this.findWhoTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.findWhoTextBox.Location = new System.Drawing.Point(94, 11);
            this.findWhoTextBox.Name = "findWhoTextBox";
            this.findWhoTextBox.Size = new System.Drawing.Size(615, 26);
            this.findWhoTextBox.TabIndex = 1;
            this.findWhoTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FindWhoTextBoxOnKeyDown);
            // 
            // findWhoLabel
            // 
            this.findWhoLabel.AutoSize = true;
            this.findWhoLabel.Location = new System.Drawing.Point(12, 14);
            this.findWhoLabel.Name = "findWhoLabel";
            this.findWhoLabel.Size = new System.Drawing.Size(77, 20);
            this.findWhoLabel.TabIndex = 0;
            this.findWhoLabel.Text = "Find who:";
            // 
            // findWhoButton
            // 
            this.findWhoButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.findWhoButton.Location = new System.Drawing.Point(729, 8);
            this.findWhoButton.Name = "findWhoButton";
            this.findWhoButton.Size = new System.Drawing.Size(75, 34);
            this.findWhoButton.TabIndex = 2;
            this.findWhoButton.Text = "&Find";
            this.findWhoButton.UseVisualStyleBackColor = true;
            this.findWhoButton.Click += new System.EventHandler(this.FindPatient);
            // 
            // OpenPatientForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(815, 688);
            this.Controls.Add(this.findWhoButton);
            this.Controls.Add(this.findWhoLabel);
            this.Controls.Add(this.findWhoTextBox);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.openPatientButton);
            this.Controls.Add(this.patientListView);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OpenPatientForm";
            this.Text = "Open Patient";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListView patientListView;
        private System.Windows.Forms.Button openPatientButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.ColumnHeader colPatientId;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colBirthdate;
        private System.Windows.Forms.ColumnHeader colAge;
        private System.Windows.Forms.TextBox findWhoTextBox;
        private System.Windows.Forms.Label findWhoLabel;
        private System.Windows.Forms.Button findWhoButton;
    }
}