﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using BPS;
using BpOmniaBridge.CommandList;
using BpOmniaBridge.Utility;
using System.Runtime.InteropServices;
using BpOmniaBridge.CommandUtility;

namespace BpOmniaBridge
{
    public partial class BpOmniaForm : Form
    {
        public BPS.BPDevice app;
        private IPatient patient;
        private ISpiro spiro;
        private ITest currentTest;
        private string subjectID;
        private string visitID;
        private Archive archive;
        // List of results: Diasgnosis/PEF/FEV1/FVC/PEFPOST/FEV1POST/FVCPOST/testID
        private List<string> results = new List<string> { };
        private bool pdfCreated = false;


        public BpOmniaForm()
        {
            InitializeComponent();
            Utility.Utility.CreateUtilityFolders();
            Utility.Utility.CreateLogFile();
            bool omnia = Utility.Utility.RunOmnia();
            if (omnia)
                try
                {
                    Type type = Type.GetTypeFromProgID("BPS.BPDevice");
                    app = (BPS.BPDevice)Activator.CreateInstance(type);
                }
                catch (COMException)
                {
                    try
                    {
                        Type type = Type.GetTypeFromProgID("BPS.BPDevice");
                        app = new BPS.BPDevice();// (BPDevice.Device.BPDevice)Activator.CreateInstance(type);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message + Environment.NewLine + ex.InnerException.Message + " BPDeviceStart");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message + ex.StackTrace, "Bp Issue", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }

            if (app != null)
            {
                Utility.Utility.Log("BP instance created");
                app.eOnNewTest += new BPS.BPDevice.DeviceEventHandler(app_eOnNewTest);
                StatusBar("After performing the tests in OMNIA, press Save Tests button");
            }
            else
                closeApp();
        }

        private void BpOmniaForm_Load(object sender, EventArgs e)
        {

        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            StatusBar("Exporting data...please wait");
            archive.ExportTests(visitID);
            StatusBar("Elaborating data...please wait");
            results = archive.ReadExportDataFile();
            StatusBar("Generating PDF file...please wait");
            pdfCreated = archive.GeneratePDF(patient.Name.FullName.ToString(), patient.DOB.ToString("dd/MM/yyyy"), results.ElementAt(7));
            StatusBar("Saving data in BP...please wait");
            SaveTest();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            app.NewTest(1, 1, 4);
        }

        #region Method
        public void closeApp()
        {
            this.Close();
            Utility.Utility.Log("Close bridge");
        }

        private void app_eOnNewTest()
        {
            Utility.Utility.Log("BP => new_test");
            currentTest = app.CurrentTest;
            patient = currentTest.Patient;
            CreateSelectSubjectAndVistCard();
        }

        private bool CreateSelectSubjectAndVistCard()
        {
            string[] prmNames = { "ID", "FirstName", "MiddleName", "LastName", "DayOfBirth", "Gender", "EthnicGroup", "Height", "Weight" };
            var id = patient.InternalId.ToString();
            var name = patient.Name.First;
            var middlename = patient.Name.Middle;
            var lastname = patient.Name.Last;
            var dob = patient.DOB.ToString("yyyyMMdd");
            
            // handle different in gender lists
            var bpGender = patient.Gender.ToString();
            if (bpGender == "Unknown") { bpGender = "Other"; };
            var gender = bpGender;

            // handle different in ethnicity lists
            var bpEthnicity = patient.Ethnicity.ToString();
            // ASK: how to match the ethnicity list in BP with OMNIA's
            var ethnicity = "Caucasian";
            var height = patient.Height.ToString();
            var weight = patient.Weight.ToString();
            //string[] prmValues = {id, name, lastname, dob, gender, ethnicity, height, weight };
            string[] prmValues = { "", "SUBJECT", "", "DEMO", "19670304", "Male", ethnicity, "180", "80"};

            //check if user is preset in DB
            archive = new Archive(prmNames, prmValues);
            subjectID = archive.CreateSubject();
            bool done = false;
            if (subjectID != "NAK")
            {
                archive.SetRecordID(subjectID);
                visitID = archive.TodayVisitCard();

                if (visitID != "NAK")
                {
                    //Select visit card
                    string[] key = new string[] { "RecordID" };
                    string[] value = new string[] { visitID };
                    done = new CommandList.CommandList().SelectVisit(key, value);
                }
            }

            if (done)
            {
                PopulateSubjectAndVisitCard(prmValues);
            }

            return done;
        }

        public void PopulateSubjectAndVisitCard(string[] array)
        {
            firstname.Text = array[1];
            middlename.Text = array[2];
            lastname.Text = array[3];
            var dobDateTime = DateTime.ParseExact(array[4], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            dob.Text = dobDateTime.ToString("dd-MM-yyyy");
            gender.Text = array[5];
            ethnicity.Text = array[6];
            height.Text = array[7];
            weight.Text = array[8];
        }

        public void StatusBar(string status)
        {
            statusBar.Text = status;
        }

        private void SaveTest()
        {
            bool success;
            var cmnDocPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
            var filename = DateTime.Today.ToString("dd-MM-yyy") + " - " + patient.Name.FullName + " (" + patient.DOB.ToString("dd/MM/yyyy") + ")" + ".pdf";
            var filePath = Path.Combine(cmnDocPath, "BpOmniaBridge", "pdf_files", filename);
            spiro = new Spiro();
            spiro.Patient = patient;
            if (pdfCreated) { spiro.ImageFile = filePath; }
            if(results.Count < 6)
            {
                success = false;
                goto noResults;
            }
            spiro.PEFR = results.ElementAt(1);
            spiro.PEFRPost = results.ElementAt(4);
            spiro.FEV1 = results.ElementAt(2);
            spiro.FEV1Post = results.ElementAt(5);
            spiro.FVC = results.ElementAt(3);
            spiro.FVCPost = results.ElementAt(6);
            spiro.Device = "COSMED Spiro";
            spiro.Comment = results.ElementAt(0);
            spiro.DateTime = DateTime.Now;

            success = spiro.SaveTest();
        noResults:;
            MessageBox.Show(success ? "Data saved successfully." : "Failed saving data.");

            if (success)
                app.OnTestComplete();
        }

        #endregion
    }
}
