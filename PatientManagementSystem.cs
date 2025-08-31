using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Globalization;

namespace PatientManagementSystem
{
    public partial class MainForm : Form
    {
        private SQLiteConnection connection;
        private string connectionString;
        private TabControl mainTabControl;

        public MainForm()
        {
            InitializeComponent();
            InitializeDatabase();
        }

        private void InitializeComponent()
        {
            // Form properties
            this.Text = "Patient Management System";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.FromArgb(240, 248, 255);
            this.Icon = SystemIcons.Application;

            // Create main tab control
            mainTabControl = new TabControl();
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.Font = new Font("Segoe UI", 10F);
            mainTabControl.ItemSize = new Size(150, 30);
            mainTabControl.SizeMode = TabSizeMode.Fixed;

            // Create tabs
            CreatePatientRegistrationTab();
            CreateAppointmentTab();
            CreatePrescriptionTab();
            CreateBillingTab();
            CreateReportsTab();

            this.Controls.Add(mainTabControl);

            // Create menu strip
            CreateMenuStrip();
        }

        private void CreateMenuStrip()
        {
            MenuStrip menuStrip = new MenuStrip();

            // File Menu
            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => Application.Exit());

            // Tools Menu
            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add("Backup Database", null, BackupDatabase);
            toolsMenu.DropDownItems.Add("Settings", null, ShowSettings);

            // Help Menu
            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("About", null, ShowAbout);

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, toolsMenu, helpMenu });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void InitializeDatabase()
        {
            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "PatientManagement.db");
                connectionString = $"Data Source={dbPath};Version=3;";
                connection = new SQLiteConnection(connectionString);

                CreateDatabaseTables();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database initialization error: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateDatabaseTables()
        {
            connection.Open();

            string[] createTables = {
                @"CREATE TABLE IF NOT EXISTS Patients (
                    PatientID INTEGER PRIMARY KEY AUTOINCREMENT,
                    FirstName TEXT NOT NULL,
                    LastName TEXT NOT NULL,
                    DateOfBirth DATE NOT NULL,
                    Gender TEXT NOT NULL,
                    PhoneNumber TEXT NOT NULL,
                    Email TEXT,
                    Address TEXT NOT NULL,
                    EmergencyContact TEXT,
                    BloodGroup TEXT,
                    MedicalHistory TEXT,
                    RegistrationDate DATETIME DEFAULT CURRENT_TIMESTAMP
                )",

                @"CREATE TABLE IF NOT EXISTS Appointments (
                    AppointmentID INTEGER PRIMARY KEY AUTOINCREMENT,
                    PatientID INTEGER NOT NULL,
                    DoctorName TEXT NOT NULL,
                    AppointmentDate DATE NOT NULL,
                    AppointmentTime TEXT NOT NULL,
                    Department TEXT NOT NULL,
                    Status TEXT DEFAULT 'Scheduled',
                    Notes TEXT,
                    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (PatientID) REFERENCES Patients (PatientID)
                )",

                @"CREATE TABLE IF NOT EXISTS Prescriptions (
                    PrescriptionID INTEGER PRIMARY KEY AUTOINCREMENT,
                    PatientID INTEGER NOT NULL,
                    DoctorName TEXT NOT NULL,
                    PrescriptionDate DATE NOT NULL,
                    Diagnosis TEXT NOT NULL,
                    Medicines TEXT NOT NULL,
                    Instructions TEXT,
                    FollowUpDate DATE,
                    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (PatientID) REFERENCES Patients (PatientID)
                )",

                @"CREATE TABLE IF NOT EXISTS Billing (
                    BillID INTEGER PRIMARY KEY AUTOINCREMENT,
                    PatientID INTEGER NOT NULL,
                    AppointmentID INTEGER,
                    ServiceDescription TEXT NOT NULL,
                    Amount DECIMAL(10,2) NOT NULL,
                    PaymentStatus TEXT DEFAULT 'Pending',
                    PaymentMethod TEXT,
                    BillDate DATE NOT NULL,
                    DueDate DATE,
                    CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (PatientID) REFERENCES Patients (PatientID),
                    FOREIGN KEY (AppointmentID) REFERENCES Appointments (AppointmentID)
                )"
            };

            using (SQLiteCommand cmd = new SQLiteCommand(connection))
            {
                foreach (string createTable in createTables)
                {
                    cmd.CommandText = createTable;
                    cmd.ExecuteNonQuery();
                }
            }

            connection.Close();
        }

        // PATIENT REGISTRATION TAB
        private void CreatePatientRegistrationTab()
        {
            TabPage patientTab = new TabPage("Patient Registration");
            patientTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            // Create form controls
            GroupBox personalInfoGroup = new GroupBox("Personal Information");
            personalInfoGroup.Size = new Size(800, 250);
            personalInfoGroup.Location = new Point(10, 10);
            personalInfoGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // First Name
            Label lblFirstName = new Label() { Text = "First Name:", Location = new Point(20, 30), Size = new Size(100, 25) };
            TextBox txtFirstName = new TextBox() { Name = "txtFirstName", Location = new Point(130, 30), Size = new Size(200, 25) };

            // Last Name
            Label lblLastName = new Label() { Text = "Last Name:", Location = new Point(350, 30), Size = new Size(100, 25) };
            TextBox txtLastName = new TextBox() { Name = "txtLastName", Location = new Point(460, 30), Size = new Size(200, 25) };

            // Date of Birth
            Label lblDOB = new Label() { Text = "Date of Birth:", Location = new Point(20, 70), Size = new Size(100, 25) };
            DateTimePicker dtpDOB = new DateTimePicker() { Name = "dtpDOB", Location = new Point(130, 70), Size = new Size(200, 25) };
            dtpDOB.MaxDate = DateTime.Today;

            // Gender
            Label lblGender = new Label() { Text = "Gender:", Location = new Point(350, 70), Size = new Size(100, 25) };
            ComboBox cmbGender = new ComboBox() { Name = "cmbGender", Location = new Point(460, 70), Size = new Size(200, 25) };
            cmbGender.Items.AddRange(new string[] { "Male", "Female", "Other" });
            cmbGender.DropDownStyle = ComboBoxStyle.DropDownList;

            // Phone Number
            Label lblPhone = new Label() { Text = "Phone Number:", Location = new Point(20, 110), Size = new Size(100, 25) };
            TextBox txtPhone = new TextBox() { Name = "txtPhone", Location = new Point(130, 110), Size = new Size(200, 25) };

            // Email
            Label lblEmail = new Label() { Text = "Email:", Location = new Point(350, 110), Size = new Size(100, 25) };
            TextBox txtEmail = new TextBox() { Name = "txtEmail", Location = new Point(460, 110), Size = new Size(200, 25) };

            // Address
            Label lblAddress = new Label() { Text = "Address:", Location = new Point(20, 150), Size = new Size(100, 25) };
            TextBox txtAddress = new TextBox() { Name = "txtAddress", Location = new Point(130, 150), Size = new Size(530, 50), Multiline = true };

            personalInfoGroup.Controls.AddRange(new Control[] { 
                lblFirstName, txtFirstName, lblLastName, txtLastName,
                lblDOB, dtpDOB, lblGender, cmbGender,
                lblPhone, txtPhone, lblEmail, txtEmail,
                lblAddress, txtAddress
            });

            // Medical Information Group
            GroupBox medicalInfoGroup = new GroupBox("Medical Information");
            medicalInfoGroup.Size = new Size(800, 150);
            medicalInfoGroup.Location = new Point(10, 270);
            medicalInfoGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Blood Group
            Label lblBloodGroup = new Label() { Text = "Blood Group:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbBloodGroup = new ComboBox() { Name = "cmbBloodGroup", Location = new Point(130, 30), Size = new Size(100, 25) };
            cmbBloodGroup.Items.AddRange(new string[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" });
            cmbBloodGroup.DropDownStyle = ComboBoxStyle.DropDownList;

            // Emergency Contact
            Label lblEmergency = new Label() { Text = "Emergency Contact:", Location = new Point(350, 30), Size = new Size(120, 25) };
            TextBox txtEmergency = new TextBox() { Name = "txtEmergency", Location = new Point(480, 30), Size = new Size(180, 25) };

            // Medical History
            Label lblMedicalHistory = new Label() { Text = "Medical History:", Location = new Point(20, 70), Size = new Size(120, 25) };
            TextBox txtMedicalHistory = new TextBox() { Name = "txtMedicalHistory", Location = new Point(130, 70), Size = new Size(530, 50), Multiline = true };

            medicalInfoGroup.Controls.AddRange(new Control[] {
                lblBloodGroup, cmbBloodGroup, lblEmergency, txtEmergency,
                lblMedicalHistory, txtMedicalHistory
            });

            // Buttons
            Button btnSavePatient = new Button() { Text = "Save Patient", Location = new Point(10, 440), Size = new Size(120, 35) };
            btnSavePatient.BackColor = Color.FromArgb(0, 123, 255);
            btnSavePatient.ForeColor = Color.White;
            btnSavePatient.FlatStyle = FlatStyle.Flat;
            btnSavePatient.Click += (s, e) => SavePatient(personalInfoGroup, medicalInfoGroup);

            Button btnClearPatient = new Button() { Text = "Clear", Location = new Point(140, 440), Size = new Size(80, 35) };
            btnClearPatient.BackColor = Color.FromArgb(108, 117, 125);
            btnClearPatient.ForeColor = Color.White;
            btnClearPatient.FlatStyle = FlatStyle.Flat;
            btnClearPatient.Click += (s, e) => ClearPatientForm(personalInfoGroup, medicalInfoGroup);

            // Patient List
            DataGridView dgvPatients = new DataGridView() { Name = "dgvPatients", Location = new Point(10, 490), Size = new Size(800, 250) };
            dgvPatients.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPatients.ReadOnly = true;
            dgvPatients.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadPatients(dgvPatients);

            mainPanel.Controls.AddRange(new Control[] { personalInfoGroup, medicalInfoGroup, btnSavePatient, btnClearPatient, dgvPatients });
            patientTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(patientTab);
        }

        // APPOINTMENT TAB
        private void CreateAppointmentTab()
        {
            TabPage appointmentTab = new TabPage("Appointments");
            appointmentTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            GroupBox appointmentGroup = new GroupBox("Schedule Appointment");
            appointmentGroup.Size = new Size(800, 200);
            appointmentGroup.Location = new Point(10, 10);
            appointmentGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Patient Selection
            Label lblPatient = new Label() { Text = "Select Patient:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbPatient = new ComboBox() { Name = "cmbPatient", Location = new Point(130, 30), Size = new Size(250, 25) };
            cmbPatient.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadPatientsInComboBox(cmbPatient);

            // Doctor Name
            Label lblDoctor = new Label() { Text = "Doctor Name:", Location = new Point(400, 30), Size = new Size(100, 25) };
            TextBox txtDoctor = new TextBox() { Name = "txtDoctor", Location = new Point(510, 30), Size = new Size(200, 25) };

            // Department
            Label lblDepartment = new Label() { Text = "Department:", Location = new Point(20, 70), Size = new Size(100, 25) };
            ComboBox cmbDepartment = new ComboBox() { Name = "cmbDepartment", Location = new Point(130, 70), Size = new Size(200, 25) };
            cmbDepartment.Items.AddRange(new string[] { "General Medicine", "Cardiology", "Orthopedics", "Pediatrics", "Gynecology", "Dermatology", "ENT", "Ophthalmology" });
            cmbDepartment.DropDownStyle = ComboBoxStyle.DropDownList;

            // Appointment Date
            Label lblAppDate = new Label() { Text = "Date:", Location = new Point(350, 70), Size = new Size(100, 25) };
            DateTimePicker dtpAppDate = new DateTimePicker() { Name = "dtpAppDate", Location = new Point(460, 70), Size = new Size(150, 25) };
            dtpAppDate.MinDate = DateTime.Today;

            // Appointment Time
            Label lblAppTime = new Label() { Text = "Time:", Location = new Point(630, 70), Size = new Size(50, 25) };
            ComboBox cmbAppTime = new ComboBox() { Name = "cmbAppTime", Location = new Point(680, 70), Size = new Size(100, 25) };
            cmbAppTime.Items.AddRange(new string[] { "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30" });
            cmbAppTime.DropDownStyle = ComboBoxStyle.DropDownList;

            // Notes
            Label lblNotes = new Label() { Text = "Notes:", Location = new Point(20, 110), Size = new Size(100, 25) };
            TextBox txtNotes = new TextBox() { Name = "txtNotes", Location = new Point(130, 110), Size = new Size(550, 50), Multiline = true };

            appointmentGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblDoctor, txtDoctor,
                lblDepartment, cmbDepartment, lblAppDate, dtpAppDate, lblAppTime, cmbAppTime,
                lblNotes, txtNotes
            });

            // Buttons
            Button btnSaveAppointment = new Button() { Text = "Schedule Appointment", Location = new Point(10, 220), Size = new Size(150, 35) };
            btnSaveAppointment.BackColor = Color.FromArgb(40, 167, 69);
            btnSaveAppointment.ForeColor = Color.White;
            btnSaveAppointment.FlatStyle = FlatStyle.Flat;
            btnSaveAppointment.Click += (s, e) => SaveAppointment(appointmentGroup);

            // Appointments List
            DataGridView dgvAppointments = new DataGridView() { Name = "dgvAppointments", Location = new Point(10, 270), Size = new Size(800, 300) };
            dgvAppointments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvAppointments.ReadOnly = true;
            dgvAppointments.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadAppointments(dgvAppointments);

            // Status buttons
            Button btnCompleted = new Button() { Text = "Mark Completed", Location = new Point(170, 220), Size = new Size(120, 35) };
            btnCompleted.BackColor = Color.FromArgb(23, 162, 184);
            btnCompleted.ForeColor = Color.White;
            btnCompleted.FlatStyle = FlatStyle.Flat;
            btnCompleted.Click += (s, e) => UpdateAppointmentStatus(dgvAppointments, "Completed");

            Button btnCancelled = new Button() { Text = "Cancel", Location = new Point(300, 220), Size = new Size(80, 35) };
            btnCancelled.BackColor = Color.FromArgb(220, 53, 69);
            btnCancelled.ForeColor = Color.White;
            btnCancelled.FlatStyle = FlatStyle.Flat;
            btnCancelled.Click += (s, e) => UpdateAppointmentStatus(dgvAppointments, "Cancelled");

            mainPanel.Controls.AddRange(new Control[] { appointmentGroup, btnSaveAppointment, btnCompleted, btnCancelled, dgvAppointments });
            appointmentTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(appointmentTab);
        }

        // PRESCRIPTION TAB
        private void CreatePrescriptionTab()
        {
            TabPage prescriptionTab = new TabPage("Prescriptions");
            prescriptionTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            GroupBox prescriptionGroup = new GroupBox("Create Prescription");
            prescriptionGroup.Size = new Size(800, 300);
            prescriptionGroup.Location = new Point(10, 10);
            prescriptionGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Patient Selection
            Label lblPatient = new Label() { Text = "Select Patient:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbPatient = new ComboBox() { Name = "cmbPatient", Location = new Point(130, 30), Size = new Size(250, 25) };
            cmbPatient.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadPatientsInComboBox(cmbPatient);

            // Doctor Name
            Label lblDoctor = new Label() { Text = "Doctor Name:", Location = new Point(400, 30), Size = new Size(100, 25) };
            TextBox txtDoctor = new TextBox() { Name = "txtDoctor", Location = new Point(510, 30), Size = new Size(200, 25) };

            // Diagnosis
            Label lblDiagnosis = new Label() { Text = "Diagnosis:", Location = new Point(20, 70), Size = new Size(100, 25) };
            TextBox txtDiagnosis = new TextBox() { Name = "txtDiagnosis", Location = new Point(130, 70), Size = new Size(580, 50), Multiline = true };

            // Medicines
            Label lblMedicines = new Label() { Text = "Medicines:", Location = new Point(20, 130), Size = new Size(100, 25) };
            TextBox txtMedicines = new TextBox() { Name = "txtMedicines", Location = new Point(130, 130), Size = new Size(580, 80), Multiline = true };
            txtMedicines.ScrollBars = ScrollBars.Vertical;

            // Instructions
            Label lblInstructions = new Label() { Text = "Instructions:", Location = new Point(20, 220), Size = new Size(100, 25) };
            TextBox txtInstructions = new TextBox() { Name = "txtInstructions", Location = new Point(130, 220), Size = new Size(400, 50), Multiline = true };

            // Follow-up Date
            Label lblFollowUp = new Label() { Text = "Follow-up Date:", Location = new Point(540, 220), Size = new Size(100, 25) };
            DateTimePicker dtpFollowUp = new DateTimePicker() { Name = "dtpFollowUp", Location = new Point(540, 245), Size = new Size(150, 25) };
            dtpFollowUp.MinDate = DateTime.Today;

            prescriptionGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblDoctor, txtDoctor,
                lblDiagnosis, txtDiagnosis, lblMedicines, txtMedicines,
                lblInstructions, txtInstructions, lblFollowUp, dtpFollowUp
            });

            // Buttons
            Button btnSavePrescription = new Button() { Text = "Save Prescription", Location = new Point(10, 320), Size = new Size(140, 35) };
            btnSavePrescription.BackColor = Color.FromArgb(111, 66, 193);
            btnSavePrescription.ForeColor = Color.White;
            btnSavePrescription.FlatStyle = FlatStyle.Flat;
            btnSavePrescription.Click += (s, e) => SavePrescription(prescriptionGroup);

            Button btnPrintPrescription = new Button() { Text = "Print", Location = new Point(160, 320), Size = new Size(80, 35) };
            btnPrintPrescription.BackColor = Color.FromArgb(108, 117, 125);
            btnPrintPrescription.ForeColor = Color.White;
            btnPrintPrescription.FlatStyle = FlatStyle.Flat;

            // Prescriptions List
            DataGridView dgvPrescriptions = new DataGridView() { Name = "dgvPrescriptions", Location = new Point(10, 370), Size = new Size(800, 250) };
            dgvPrescriptions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPrescriptions.ReadOnly = true;
            dgvPrescriptions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadPrescriptions(dgvPrescriptions);

            mainPanel.Controls.AddRange(new Control[] { prescriptionGroup, btnSavePrescription, btnPrintPrescription, dgvPrescriptions });
            prescriptionTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(prescriptionTab);
        }

        // BILLING TAB
        private void CreateBillingTab()
        {
            TabPage billingTab = new TabPage("Billing");
            billingTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            GroupBox billingGroup = new GroupBox("Create Bill");
            billingGroup.Size = new Size(800, 200);
            billingGroup.Location = new Point(10, 10);
            billingGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Patient Selection
            Label lblPatient = new Label() { Text = "Select Patient:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbPatient = new ComboBox() { Name = "cmbPatient", Location = new Point(130, 30), Size = new Size(250, 25) };
            cmbPatient.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadPatientsInComboBox(cmbPatient);

            // Service Description
            Label lblService = new Label() { Text = "Service:", Location = new Point(400, 30), Size = new Size(80, 25) };
            TextBox txtService = new TextBox() { Name = "txtService", Location = new Point(490, 30), Size = new Size(220, 25) };

            // Amount
            Label lblAmount = new Label() { Text = "Amount:", Location = new Point(20, 70), Size = new Size(100, 25) };
            NumericUpDown numAmount = new NumericUpDown() { Name = "numAmount", Location = new Point(130, 70), Size = new Size(150, 25) };
            numAmount.DecimalPlaces = 2;
            numAmount.Maximum = 999999;
            numAmount.Minimum = 0;

            // Payment Method
            Label lblPaymentMethod = new Label() { Text = "Payment Method:", Location = new Point(300, 70), Size = new Size(120, 25) };
            ComboBox cmbPaymentMethod = new ComboBox() { Name = "cmbPaymentMethod", Location = new Point(430, 70), Size = new Size(150, 25) };
            cmbPaymentMethod.Items.AddRange(new string[] { "Cash", "Credit Card", "Debit Card", "Insurance", "Online Transfer" });
            cmbPaymentMethod.DropDownStyle = ComboBoxStyle.DropDownList;

            // Due Date
            Label lblDueDate = new Label() { Text = "Due Date:", Location = new Point(600, 70), Size = new Size(70, 25) };
            DateTimePicker dtpDueDate = new DateTimePicker() { Name = "dtpDueDate", Location = new Point(680, 70), Size = new Size(100, 25) };
            dtpDueDate.MinDate = DateTime.Today;

            // Status
            Label lblStatus = new Label() { Text = "Status:", Location = new Point(20, 110), Size = new Size(100, 25) };
            ComboBox cmbStatus = new ComboBox() { Name = "cmbStatus", Location = new Point(130, 110), Size = new Size(150, 25) };
            cmbStatus.Items.AddRange(new string[] { "Pending", "Paid", "Partial", "Overdue" });
            cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStatus.SelectedIndex = 0;

            billingGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblService, txtService,
                lblAmount, numAmount, lblPaymentMethod, cmbPaymentMethod,
                lblDueDate, dtpDueDate, lblStatus, cmbStatus
            });

            // Buttons
            Button btnSaveBill = new Button() { Text = "Create Bill", Location = new Point(10, 220), Size = new Size(100, 35) };
            btnSaveBill.BackColor = Color.FromArgb(255, 193, 7);
            btnSaveBill.ForeColor = Color.Black;
            btnSaveBill.FlatStyle = FlatStyle.Flat;
            btnSaveBill.Click += (s, e) => SaveBill(billingGroup);

            Button btnMarkPaid = new Button() { Text = "Mark as Paid", Location = new Point(120, 220), Size = new Size(100, 35) };
            btnMarkPaid.BackColor = Color.FromArgb(40, 167, 69);
            btnMarkPaid.ForeColor = Color.White;
            btnMarkPaid.FlatStyle = FlatStyle.Flat;

            // Bills List
            DataGridView dgvBills = new DataGridView() { Name = "dgvBills", Location = new Point(10, 270), Size = new Size(800, 300) };
            dgvBills.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvBills.ReadOnly = true;
            dgvBills.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadBills(dgvBills);

            btnMarkPaid.Click += (s, e) => {
                if (dgvBills.SelectedRows.Count > 0)
                {
                    int billId = Convert.ToInt32(dgvBills.SelectedRows[0].Cells["BillID"].Value);
                    UpdateBillStatus(billId, "Paid");
                    LoadBills(dgvBills);
                }
            };

            mainPanel.Controls.AddRange(new Control[] { billingGroup, btnSaveBill, btnMarkPaid, dgvBills });
            billingTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(billingTab);
        }

        // REPORTS TAB
        private void CreateReportsTab()
        {
            TabPage reportsTab = new TabPage("Reports & Summary");
            reportsTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            // Summary Cards Panel
            Panel summaryPanel = new Panel();
            summaryPanel.Size = new Size(800, 100);
            summaryPanel.Location = new Point(10, 10);

            // Total Patients Card
            Panel patientsCard = CreateSummaryCard("Total Patients", GetTotalPatients().ToString(), Color.FromArgb(0, 123, 255));
            patientsCard.Location = new Point(0, 0);

            // Today's Appointments Card
            Panel appointmentsCard = CreateSummaryCard("Today's Appointments", GetTodayAppointments().ToString(), Color.FromArgb(40, 167, 69));
            appointmentsCard.Location = new Point(200, 0);

            // Pending Bills Card
            Panel billsCard = CreateSummaryCard("Pending Bills", GetPendingBills().ToString(), Color.FromArgb(255, 193, 7));
            billsCard.Location = new Point(400, 0);

            // Today's Revenue Card
            Panel revenueCard = CreateSummaryCard("Today's Revenue", $"₹{GetTodayRevenue():F2}", Color.FromArgb(220, 53, 69));
            revenueCard.Location = new Point(600, 0);

            summaryPanel.Controls.AddRange(new Control[] { patientsCard, appointmentsCard, billsCard, revenueCard });

            // Date Range Selection
            GroupBox dateRangeGroup = new GroupBox("Sales Summary");
            dateRangeGroup.Size = new Size(800, 80);
            dateRangeGroup.Location = new Point(10, 120);
            dateRangeGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            Label lblFromDate = new Label() { Text = "From Date:", Location = new Point(20, 30), Size = new Size(80, 25) };
            DateTimePicker dtpFromDate = new DateTimePicker() { Name = "dtpFromDate", Location = new Point(100, 30), Size = new Size(150, 25) };
            dtpFromDate.Value = DateTime.Today.AddDays(-7);

            Label lblToDate = new Label() { Text = "To Date:", Location = new Point(270, 30), Size = new Size(60, 25) };
            DateTimePicker dtpToDate = new DateTimePicker() { Name = "dtpToDate", Location = new Point(340, 30), Size = new Size(150, 25) };
            dtpToDate.Value = DateTime.Today;

            Button btnGenerateReport = new Button() { Text = "Generate Report", Location = new Point(510, 25), Size = new Size(130, 35) };
            btnGenerateReport.BackColor = Color.FromArgb(111, 66, 193);
            btnGenerateReport.ForeColor = Color.White;
            btnGenerateReport.FlatStyle = FlatStyle.Flat;

            dateRangeGroup.Controls.AddRange(new Control[] { lblFromDate, dtpFromDate, lblToDate, dtpToDate, btnGenerateReport });

            // Reports DataGridView
            DataGridView dgvReports = new DataGridView() { Name = "dgvReports", Location = new Point(10, 210), Size = new Size(800, 350) };
            dgvReports.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvReports.ReadOnly = true;

            btnGenerateReport.Click += (s, e) => {
                DateTime fromDate = dtpFromDate.Value.Date;
                DateTime toDate = dtpToDate.Value.Date;
                LoadSalesReport(dgvReports, fromDate, toDate);
            };

            // Export Button
            Button btnExport = new Button() { Text = "Export to CSV", Location = new Point(650, 25), Size = new Size(120, 35) };
            btnExport.BackColor = Color.FromArgb(23, 162, 184);
            btnExport.ForeColor = Color.White;
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.Click += (s, e) => ExportReportToCSV(dgvReports);

            dateRangeGroup.Controls.Add(btnExport);

            mainPanel.Controls.AddRange(new Control[] { summaryPanel, dateRangeGroup, dgvReports });
            reportsTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(reportsTab);

            // Load initial report
            LoadSalesReport(dgvReports, DateTime.Today.AddDays(-7), DateTime.Today);
        }

        private Panel CreateSummaryCard(string title, string value, Color color)
        {
            Panel card = new Panel();
            card.Size = new Size(190, 80);
            card.BackColor = color;

            Label lblTitle = new Label() { 
                Text = title, 
                ForeColor = Color.White, 
                Font = new Font("Segoe UI", 9F), 
                Location = new Point(10, 10), 
                Size = new Size(170, 20) 
            };

            Label lblValue = new Label() { 
                Text = value, 
                ForeColor = Color.White, 
                Font = new Font("Segoe UI", 16F, FontStyle.Bold), 
                Location = new Point(10, 35), 
                Size = new Size(170, 30) 
            };

            card.Controls.AddRange(new Control[] { lblTitle, lblValue });
            return card;
        }

        // DATABASE OPERATIONS
        private void SavePatient(GroupBox personalInfo, GroupBox medicalInfo)
        {
            try
            {
                // Get controls from both groups
                TextBox txtFirstName = personalInfo.Controls["txtFirstName"] as TextBox;
                TextBox txtLastName = personalInfo.Controls["txtLastName"] as TextBox;
                DateTimePicker dtpDOB = personalInfo.Controls["dtpDOB"] as DateTimePicker;
                ComboBox cmbGender = personalInfo.Controls["cmbGender"] as ComboBox;
                TextBox txtPhone = personalInfo.Controls["txtPhone"] as TextBox;
                TextBox txtEmail = personalInfo.Controls["txtEmail"] as TextBox;
                TextBox txtAddress = personalInfo.Controls["txtAddress"] as TextBox;

                ComboBox cmbBloodGroup = medicalInfo.Controls["cmbBloodGroup"] as ComboBox;
                TextBox txtEmergency = medicalInfo.Controls["txtEmergency"] as TextBox;
                TextBox txtMedicalHistory = medicalInfo.Controls["txtMedicalHistory"] as TextBox;

                // Validation
                if (string.IsNullOrWhiteSpace(txtFirstName.Text) || string.IsNullOrWhiteSpace(txtLastName.Text) ||
                    cmbGender.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtPhone.Text) ||
                    string.IsNullOrWhiteSpace(txtAddress.Text))
                {
                    MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Patients 
                    (FirstName, LastName, DateOfBirth, Gender, PhoneNumber, Email, Address, EmergencyContact, BloodGroup, MedicalHistory) 
                    VALUES (@fname, @lname, @dob, @gender, @phone, @email, @address, @emergency, @bloodgroup, @medical)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@fname", txtFirstName.Text);
                    cmd.Parameters.AddWithValue("@lname", txtLastName.Text);
                    cmd.Parameters.AddWithValue("@dob", dtpDOB.Value.Date);
                    cmd.Parameters.AddWithValue("@gender", cmbGender.Text);
                    cmd.Parameters.AddWithValue("@phone", txtPhone.Text);
                    cmd.Parameters.AddWithValue("@email", txtEmail.Text);
                    cmd.Parameters.AddWithValue("@address", txtAddress.Text);
                    cmd.Parameters.AddWithValue("@emergency", txtEmergency.Text);
                    cmd.Parameters.AddWithValue("@bloodgroup", cmbBloodGroup.Text);
                    cmd.Parameters.AddWithValue("@medical", txtMedicalHistory.Text);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                MessageBox.Show("Patient registered successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearPatientForm(personalInfo, medicalInfo);

                // Refresh patient list
                DataGridView dgvPatients = personalInfo.Parent.Controls["dgvPatients"] as DataGridView;
                LoadPatients(dgvPatients);

                // Refresh combo boxes in other tabs
                RefreshPatientComboBoxes();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error saving patient: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPatients(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = "SELECT PatientID, FirstName || ' ' || LastName as FullName, DateOfBirth, Gender, PhoneNumber, Email FROM Patients ORDER BY RegistrationDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    dgv.DataSource = dt;
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading patients: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPatientsInComboBox(ComboBox cmb)
        {
            try
            {
                connection.Open();
                string query = "SELECT PatientID, FirstName || ' ' || LastName || ' (ID: ' || PatientID || ')' as DisplayName FROM Patients ORDER BY FirstName";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    cmb.Items.Clear();
                    cmb.DisplayMember = "DisplayName";
                    cmb.ValueMember = "PatientID";

                    DataTable dt = new DataTable();
                    dt.Load(reader);
                    cmb.DataSource = dt;
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading patients: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshPatientComboBoxes()
        {
            foreach (TabPage tab in mainTabControl.TabPages)
            {
                foreach (Control control in tab.Controls)
                {
                    if (control is Panel panel)
                    {
                        foreach (Control panelControl in panel.Controls)
                        {
                            if (panelControl is GroupBox group)
                            {
                                foreach (Control groupControl in group.Controls)
                                {
                                    if (groupControl is ComboBox cmb && cmb.Name == "cmbPatient")
                                    {
                                        LoadPatientsInComboBox(cmb);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ClearPatientForm(GroupBox personalInfo, GroupBox medicalInfo)
        {
            foreach (Control control in personalInfo.Controls.Cast<Control>().Concat(medicalInfo.Controls.Cast<Control>()))
            {
                if (control is TextBox txt)
                    txt.Clear();
                else if (control is ComboBox cmb)
                    cmb.SelectedIndex = -1;
                else if (control is DateTimePicker dtp)
                    dtp.Value = DateTime.Today;
            }
        }

        private void SaveAppointment(GroupBox appointmentGroup)
        {
            try
            {
                ComboBox cmbPatient = appointmentGroup.Controls["cmbPatient"] as ComboBox;
                TextBox txtDoctor = appointmentGroup.Controls["txtDoctor"] as TextBox;
                ComboBox cmbDepartment = appointmentGroup.Controls["cmbDepartment"] as ComboBox;
                DateTimePicker dtpAppDate = appointmentGroup.Controls["dtpAppDate"] as DateTimePicker;
                ComboBox cmbAppTime = appointmentGroup.Controls["cmbAppTime"] as ComboBox;
                TextBox txtNotes = appointmentGroup.Controls["txtNotes"] as TextBox;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtDoctor.Text) ||
                    cmbDepartment.SelectedIndex == -1 || cmbAppTime.SelectedIndex == -1)
                {
                    MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Appointments 
                    (PatientID, DoctorName, AppointmentDate, AppointmentTime, Department, Notes) 
                    VALUES (@patientid, @doctor, @date, @time, @dept, @notes)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@doctor", txtDoctor.Text);
                    cmd.Parameters.AddWithValue("@date", dtpAppDate.Value.Date);
                    cmd.Parameters.AddWithValue("@time", cmbAppTime.Text);
                    cmd.Parameters.AddWithValue("@dept", cmbDepartment.Text);
                    cmd.Parameters.AddWithValue("@notes", txtNotes.Text);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                MessageBox.Show("Appointment scheduled successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Clear form and refresh list
                foreach (Control control in appointmentGroup.Controls)
                {
                    if (control is TextBox txt)
                        txt.Clear();
                    else if (control is ComboBox cmb && cmb.Name != "cmbPatient")
                        cmb.SelectedIndex = -1;
                }

                DataGridView dgvAppointments = appointmentGroup.Parent.Controls["dgvAppointments"] as DataGridView;
                LoadAppointments(dgvAppointments);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error scheduling appointment: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadAppointments(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT a.AppointmentID, p.FirstName || ' ' || p.LastName as PatientName, 
                    a.DoctorName, a.AppointmentDate, a.AppointmentTime, a.Department, a.Status, a.Notes
                    FROM Appointments a
                    JOIN Patients p ON a.PatientID = p.PatientID
                    ORDER BY a.AppointmentDate DESC, a.AppointmentTime";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    dgv.DataSource = dt;
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading appointments: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateAppointmentStatus(DataGridView dgv, string status)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                try
                {
                    int appointmentId = Convert.ToInt32(dgv.SelectedRows[0].Cells["AppointmentID"].Value);

                    connection.Open();
                    string query = "UPDATE Appointments SET Status = @status WHERE AppointmentID = @id";

                    using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@status", status);
                        cmd.Parameters.AddWithValue("@id", appointmentId);
                        cmd.ExecuteNonQuery();
                    }
                    connection.Close();

                    MessageBox.Show($"Appointment marked as {status.ToLower()}!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadAppointments(dgv);
                }
                catch (Exception ex)
                {
                    if (connection.State == ConnectionState.Open)
                        connection.Close();
                    MessageBox.Show($"Error updating appointment: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SavePrescription(GroupBox prescriptionGroup)
        {
            try
            {
                ComboBox cmbPatient = prescriptionGroup.Controls["cmbPatient"] as ComboBox;
                TextBox txtDoctor = prescriptionGroup.Controls["txtDoctor"] as TextBox;
                TextBox txtDiagnosis = prescriptionGroup.Controls["txtDiagnosis"] as TextBox;
                TextBox txtMedicines = prescriptionGroup.Controls["txtMedicines"] as TextBox;
                TextBox txtInstructions = prescriptionGroup.Controls["txtInstructions"] as TextBox;
                DateTimePicker dtpFollowUp = prescriptionGroup.Controls["dtpFollowUp"] as DateTimePicker;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtDoctor.Text) ||
                    string.IsNullOrWhiteSpace(txtDiagnosis.Text) || string.IsNullOrWhiteSpace(txtMedicines.Text))
                {
                    MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Prescriptions 
                    (PatientID, DoctorName, PrescriptionDate, Diagnosis, Medicines, Instructions, FollowUpDate) 
                    VALUES (@patientid, @doctor, @date, @diagnosis, @medicines, @instructions, @followup)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@doctor", txtDoctor.Text);
                    cmd.Parameters.AddWithValue("@date", DateTime.Today);
                    cmd.Parameters.AddWithValue("@diagnosis", txtDiagnosis.Text);
                    cmd.Parameters.AddWithValue("@medicines", txtMedicines.Text);
                    cmd.Parameters.AddWithValue("@instructions", txtInstructions.Text);
                    cmd.Parameters.AddWithValue("@followup", dtpFollowUp.Value.Date);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                MessageBox.Show("Prescription saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Clear form and refresh list
                foreach (Control control in prescriptionGroup.Controls)
                {
                    if (control is TextBox txt)
                        txt.Clear();
                    else if (control is ComboBox cmb && cmb.Name != "cmbPatient")
                        cmb.SelectedIndex = -1;
                    else if (control is DateTimePicker dtp)
                        dtp.Value = DateTime.Today;
                }

                DataGridView dgvPrescriptions = prescriptionGroup.Parent.Controls["dgvPrescriptions"] as DataGridView;
                LoadPrescriptions(dgvPrescriptions);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error saving prescription: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPrescriptions(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT pr.PrescriptionID, p.FirstName || ' ' || p.LastName as PatientName, 
                    pr.DoctorName, pr.PrescriptionDate, pr.Diagnosis, pr.Medicines, pr.FollowUpDate
                    FROM Prescriptions pr
                    JOIN Patients p ON pr.PatientID = p.PatientID
                    ORDER BY pr.PrescriptionDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    dgv.DataSource = dt;
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading prescriptions: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveBill(GroupBox billingGroup)
        {
            try
            {
                ComboBox cmbPatient = billingGroup.Controls["cmbPatient"] as ComboBox;
                TextBox txtService = billingGroup.Controls["txtService"] as TextBox;
                NumericUpDown numAmount = billingGroup.Controls["numAmount"] as NumericUpDown;
                ComboBox cmbPaymentMethod = billingGroup.Controls["cmbPaymentMethod"] as ComboBox;
                DateTimePicker dtpDueDate = billingGroup.Controls["dtpDueDate"] as DateTimePicker;
                ComboBox cmbStatus = billingGroup.Controls["cmbStatus"] as ComboBox;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtService.Text) || numAmount.Value <= 0)
                {
                    MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Billing 
                    (PatientID, ServiceDescription, Amount, PaymentStatus, PaymentMethod, BillDate, DueDate) 
                    VALUES (@patientid, @service, @amount, @status, @method, @billdate, @duedate)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@service", txtService.Text);
                    cmd.Parameters.AddWithValue("@amount", numAmount.Value);
                    cmd.Parameters.AddWithValue("@status", cmbStatus.Text);
                    cmd.Parameters.AddWithValue("@method", cmbPaymentMethod.Text);
                    cmd.Parameters.AddWithValue("@billdate", DateTime.Today);
                    cmd.Parameters.AddWithValue("@duedate", dtpDueDate.Value.Date);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                MessageBox.Show("Bill created successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Clear form and refresh list
                foreach (Control control in billingGroup.Controls)
                {
                    if (control is TextBox txt)
                        txt.Clear();
                    else if (control is NumericUpDown num)
                        num.Value = 0;
                    else if (control is ComboBox cmb && cmb.Name != "cmbPatient")
                        cmb.SelectedIndex = -1;
                }

                DataGridView dgvBills = billingGroup.Parent.Controls["dgvBills"] as DataGridView;
                LoadBills(dgvBills);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error creating bill: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadBills(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT b.BillID, p.FirstName || ' ' || p.LastName as PatientName, 
                    b.ServiceDescription, b.Amount, b.PaymentStatus, b.PaymentMethod, b.BillDate, b.DueDate
                    FROM Billing b
                    JOIN Patients p ON b.PatientID = p.PatientID
                    ORDER BY b.BillDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    dgv.DataSource = dt;
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading bills: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateBillStatus(int billId, string status)
        {
            try
            {
                connection.Open();
                string query = "UPDATE Billing SET PaymentStatus = @status WHERE BillID = @id";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@status", status);
                    cmd.Parameters.AddWithValue("@id", billId);
                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                MessageBox.Show($"Bill marked as {status.ToLower()}!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error updating bill: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // REPORT METHODS
        private int GetTotalPatients()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM Patients", connection))
                {
                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    connection.Close();
                    return count;
                }
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                return 0;
            }
        }

        private int GetTodayAppointments()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM Appointments WHERE DATE(AppointmentDate) = DATE('now')", connection))
                {
                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    connection.Close();
                    return count;
                }
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                return 0;
            }
        }

        private int GetPendingBills()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM Billing WHERE PaymentStatus = 'Pending'", connection))
                {
                    int count = Convert.ToInt32(cmd.ExecuteScalar());
                    connection.Close();
                    return count;
                }
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                return 0;
            }
        }

        private decimal GetTodayRevenue()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COALESCE(SUM(Amount), 0) FROM Billing WHERE DATE(BillDate) = DATE('now') AND PaymentStatus = 'Paid'", connection))
                {
                    decimal revenue = Convert.ToDecimal(cmd.ExecuteScalar());
                    connection.Close();
                    return revenue;
                }
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                return 0;
            }
        }

        private void LoadSalesReport(DataGridView dgv, DateTime fromDate, DateTime toDate)
        {
            try
            {
                connection.Open();
                string query = @"SELECT 
                    DATE(b.BillDate) as Date,
                    COUNT(*) as TotalBills,
                    SUM(CASE WHEN b.PaymentStatus = 'Paid' THEN b.Amount ELSE 0 END) as PaidAmount,
                    SUM(CASE WHEN b.PaymentStatus = 'Pending' THEN b.Amount ELSE 0 END) as PendingAmount,
                    SUM(b.Amount) as TotalAmount
                    FROM Billing b
                    WHERE DATE(b.BillDate) BETWEEN @fromdate AND @todate
                    GROUP BY DATE(b.BillDate)
                    ORDER BY DATE(b.BillDate) DESC";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@fromdate", fromDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@todate", toDate.ToString("yyyy-MM-dd"));

                    using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        adapter.Fill(dt);
                        dgv.DataSource = dt;
                    }
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading sales report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportReportToCSV(DataGridView dgv)
        {
            if (dgv.DataSource == null)
                return;

            try
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "CSV files (*.csv)|*.csv";
                saveDialog.FileName = $"SalesReport_{DateTime.Now:yyyyMMdd}.csv";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    using (StreamWriter sw = new StreamWriter(saveDialog.FileName))
                    {
                        // Write headers
                        string headers = string.Join(",", dgv.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText));
                        sw.WriteLine(headers);

                        // Write data
                        foreach (DataGridViewRow row in dgv.Rows)
                        {
                            if (row.IsNewRow) continue;
                            string rowData = string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(cell => $""{cell.Value}""));
                            sw.WriteLine(rowData);
                        }
                    }

                    MessageBox.Show("Report exported successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting report: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // MENU HANDLERS
        private void BackupDatabase(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Filter = "Database files (*.db)|*.db";
                saveDialog.FileName = $"PatientManagement_Backup_{DateTime.Now:yyyyMMdd_HHmm}.db";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    string sourcePath = Path.Combine(Application.StartupPath, "PatientManagement.db");
                    File.Copy(sourcePath, saveDialog.FileName, true);
                    MessageBox.Show("Database backed up successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error backing up database: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            MessageBox.Show("Settings feature will be implemented in future versions.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

       private void ShowAbout(object sender, EventArgs e)
{
    string aboutText = "Patient Management System v1.0\n\n" +
                       "A comprehensive healthcare management solution\n" +
                       "Features: Patient Registration, Appointments, Prescriptions, Billing & Reports\n\n" +
                       "© 2025 - Healthcare Solutions";

    string caption = "About";

    MessageBox.Show(aboutText, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
}

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                connection?.Close();
                connection?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
