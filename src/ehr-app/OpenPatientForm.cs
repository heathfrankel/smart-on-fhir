using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using System;
using System.Linq;
using System.Windows.Forms;

namespace EHRApp
{
    public partial class OpenPatientForm : Form
    {
        private FhirClient _fhirClient = null;
        public OpenPatientForm()
        {
            InitializeComponent();
        }

        protected FhirClient FhirClient
        {
            get
            {
                return (_fhirClient = _fhirClient ?? new FhirClient(Program._baseAddress ?? Globals.ApplicationSettings.FhirBaseUrl));
            }
        }
        
        public void PopulateListView(Bundle bundle)
        {
            patientListView.Items.Clear();
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                Patient patient = entry.Resource as Patient;
                if (patient == null) continue; // Should never happen, but I do not want to deal with cast or null reference exceptions

                ListViewItem item = new ListViewItem
                {
                    Text = patient.Id,
                    Tag = $"Patient/{patient.Id}"
                    //Tag = patient.Id
                };
                HumanName name = patient.Name.FirstOrDefault();
                if (name != null)
                    item.SubItems.Add($"{name.Given.FirstOrDefault()} {name.Family}");
                else
                    item.SubItems.Add($"(no name)");
                item.SubItems.Add(patient.BirthDate);
                if (!string.IsNullOrEmpty(patient.BirthDate))
                    item.SubItems.Add(CalculateAge(patient.BirthDateElement?.ToDateTime()));
                patientListView.Items.Add(item);
            }
        }

        private string CalculateAge(DateTime? birthdate)
        {
            if (!birthdate.HasValue) return null;
            int age = DateTime.Now.Year - birthdate.Value.Year;
            return age.ToString();
        }

        private void CancelButtonOnClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void OpenPatientOnClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        public Patient OpenPatient()
        {
            if (string.IsNullOrEmpty(SelectedPatientId)) return null;

            return FhirClient.Read<Patient>(SelectedPatientId);
        }

        private void FindPatient(object sender, EventArgs e)
        {
            Bundle patientBundle = FindPatientByName(findWhoTextBox.Text);
            if (patientBundle != null)
                PopulateListView(patientBundle);
        }

        private Bundle FindPatientByName(string name)
        {
            try
            {
                if (!string.IsNullOrEmpty(name))
                    return FhirClient.Search<Patient>(new string[] { $"name={name}" });
                return FhirClient.Search<Patient>();
            }
            catch (FhirOperationException ex)
            {
                MessageBox.Show(ex.Message);
                return null;
            }
            catch (System.Net.WebException ex)
            {
                MessageBox.Show(ex.Message);
                return null;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message);
                return null;
            }
        }

        public string SelectedPatientId { get; private set; }

        private void PatientListViewOnSelectedIndexChanged(object sender, EventArgs e)
        {
            if(patientListView.SelectedItems.Count > 0)
            {
                SelectedPatientId = patientListView.SelectedItems[0].Tag as string;
            }
            else
            {
                SelectedPatientId = null;
            }
        }

        private void FindWhoTextBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyData == Keys.Enter)
            {
                e.Handled = true;
                findWhoButton.PerformClick();
            }
        }

        private void patientListView_DoubleClick(object sender, EventArgs e)
        {
            if (patientListView.SelectedItems.Count > 0)
            {
                SelectedPatientId = patientListView.SelectedItems[0].Tag as string;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
