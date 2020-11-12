using Hl7.Fhir.Model;
using Hl7.Fhir.SmartAppLaunch;
using Hl7.Fhir.Support;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EHRApp
{
    public partial class MDIParent : Form
    {
        public MDIParent()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Add in all the registered SMART applications
            foreach (SmartApplicationDetails settings in Globals.SmartAppSettings.SmartApplications)
            {
                var toolbarItem = new ToolStripMenuItem();
                toolbarItem.Name = $"MenuItem_{settings.Key}";
                toolbarItem.Size = new System.Drawing.Size(334, 26);
                toolbarItem.Tag = settings.Key;
                toolbarItem.Text = settings.Name;
                toolbarItem.Click += new System.EventHandler(smartAppLaunchToolbarItem_Click);
                
                toolsMenu.DropDownItems.Add(toolbarItem);
            }
        }

        private void ShowNewForm(object sender, EventArgs e)
        {
            
        }

        private void ExitToolsStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void CutToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void CopyToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void PasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void ToolBarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toolStrip.Visible = toolBarToolStripMenuItem.Checked;
        }

        private void StatusBarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            statusStrip.Visible = statusBarToolStripMenuItem.Checked;
        }

        private void CascadeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.Cascade);
        }

        private void TileVerticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileVertical);
        }

        private void TileHorizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.TileHorizontal);
        }

        private void ArrangeIconsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LayoutMdi(MdiLayout.ArrangeIcons);
        }

        private void CloseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (Form childForm in MdiChildren)
            {
                childForm.Close();
            }
        }
        
        public void DisplayOutput(string output)
        {
            this.InvokeOnUiThreadIfRequired(() => toolStripStatusLabel.Text = output);
        }
        
        private void OpenPatient(object sender, EventArgs e)
        {
            OpenPatientForm openPatientForm = new OpenPatientForm();
            if(openPatientForm.ShowDialog(this) == DialogResult.OK)
            {
                Patient patient = openPatientForm.OpenPatient();
                if (patient != null)
                {
                    PatientForm patientForm = new PatientForm();
                    patientForm.MdiParent = this;
                    patientForm.WindowState = FormWindowState.Maximized;
                    patientForm.SetPatient(patient);
                    patientForm.Show();
                }
                //else
                    // TODO: Give user an error message
            }
        }

        private void smartAppLaunchToolbarItem_Click(object sender, EventArgs e)
        {
            IPatientData patientData = ActiveMdiChild as IPatientData;
            if (patientData == null)
            {
                MessageBox.Show(this, "No patient has been selected, please select a patient", "No patient selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string applicationKey = ReflectionUtility.GetPropertyValue(sender, "Tag", string.Empty) as string;
            SmartApplicationDetails application = Globals.GetSmartApplicationSettings(applicationKey);

            var context = new SmartAppContext() { LaunchContext = Guid.NewGuid().ToFhirId(), PatientNameForDebug = patientData.Patient.Name.FirstOrDefault().Text };
            context.ContextProperties.Add(new System.Collections.Generic.KeyValuePair<string, string>("patient", patientData.Patient.Id));
            Console.WriteLine($"Opening Smart App {application.Name}: {context.LaunchContext}             Patient/{patientData.Patient.Id} {patientData.Patient.Name.FirstOrDefault().Text}");

            context.ContextProperties.Add(new System.Collections.Generic.KeyValuePair<string, string>("organization", Globals.ApplicationSettings.organization));
            context.ContextProperties.Add(new System.Collections.Generic.KeyValuePair<string, string>("practitioner", Globals.ApplicationSettings.practitioner));
            context.ContextProperties.Add(new System.Collections.Generic.KeyValuePair<string, string>("practitionerrole", Globals.ApplicationSettings.practitionerrole));

            SMARTForm smartForm = new SMARTForm();
            smartForm.MdiParent = this;
            smartForm.WindowState = ActiveMdiChild.WindowState; // FormWindowState.Maximized;

            smartForm.LoadSmartApp(application, context);
            smartForm.Show();
        }
    }
}
