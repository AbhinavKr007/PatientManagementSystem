// Full and corrected PatientManagementSystem.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Globalization;
using System.Drawing.Printing;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace PatientManagementSystem
{
    internal static class DbInit
    {
        public static readonly string[] Scripts = {
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
            )",

            @"CREATE TABLE IF NOT EXISTS Users (
                UserID INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Salt TEXT NOT NULL,
                Iterations INTEGER NOT NULL DEFAULT 0,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            )",

            @"CREATE TABLE IF NOT EXISTS AuditLog (
                AuditLogID INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                Action TEXT NOT NULL,
                Details TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            )"
        };

        public static void EnsureSchema(string connectionString)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(conn))
                {
                    foreach (string script in Scripts)
                    {
                        cmd.CommandText = script;
                        cmd.ExecuteNonQuery();
                    }
                }
                MigrateAddIterationsColumn(conn);
                EnsureDefaultAdmin(conn);
            }
        }

        // Existing installs created their Users table before the Iterations column
        // existed; CREATE TABLE IF NOT EXISTS won't retrofit it, so add it here.
        private static void MigrateAddIterationsColumn(SQLiteConnection conn)
        {
            try
            {
                using (SQLiteCommand alter = new SQLiteCommand(
                    "ALTER TABLE Users ADD COLUMN Iterations INTEGER NOT NULL DEFAULT 0", conn))
                {
                    alter.ExecuteNonQuery();
                }
            }
            catch (SQLiteException)
            {
                // Column already exists.
            }
        }

        private static void EnsureDefaultAdmin(SQLiteConnection conn)
        {
            using (SQLiteCommand check = new SQLiteCommand("SELECT COUNT(*) FROM Users", conn))
            {
                long userCount = Convert.ToInt64(check.ExecuteScalar());
                if (userCount == 0)
                {
                    string salt = Guid.NewGuid().ToString("N");
                    string hash = AuthHelper.HashPassword("admin123", salt, AuthHelper.DefaultIterations);

                    using (SQLiteCommand insert = new SQLiteCommand(
                        "INSERT INTO Users (Username, PasswordHash, Salt, Iterations) VALUES (@u, @h, @s, @i)", conn))
                    {
                        insert.Parameters.AddWithValue("@u", "admin");
                        insert.Parameters.AddWithValue("@h", hash);
                        insert.Parameters.AddWithValue("@s", salt);
                        insert.Parameters.AddWithValue("@i", AuthHelper.DefaultIterations);
                        insert.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    internal static class AuthHelper
    {
        public const int DefaultIterations = 100_000;
        private const int HashSizeBytes = 32;

        // PBKDF2-HMACSHA256: a single SHA256(salt + password) round is fast enough
        // for an attacker to brute-force offline at billions of guesses/sec; PBKDF2
        // with a high iteration count makes each guess deliberately expensive.
        public static string HashPassword(string password, string salt, int iterations)
        {
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                Encoding.UTF8.GetBytes(salt),
                iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);
            return Convert.ToBase64String(hash);
        }

        // Only used to verify passwords hashed before PBKDF2 was introduced.
        private static string LegacyHashPassword(string password, string salt)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + password));
                return Convert.ToBase64String(bytes);
            }
        }

        private static bool HashesMatch(string computedHash, string storedHash)
        {
            byte[] computedBytes = Convert.FromBase64String(computedHash);
            byte[] storedBytes;
            try
            {
                storedBytes = Convert.FromBase64String(storedHash);
            }
            catch (FormatException)
            {
                return false;
            }

            return computedBytes.Length == storedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
        }

        public static bool ValidateLogin(string connectionString, string username, string password, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(
                        "SELECT PasswordHash, Salt, Iterations FROM Users WHERE Username = @u", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", username);
                        string storedHash, salt;
                        int iterations;

                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                errorMessage = "Invalid username or password.";
                                return false;
                            }

                            storedHash = reader["PasswordHash"].ToString();
                            salt = reader["Salt"].ToString();
                            iterations = Convert.ToInt32(reader["Iterations"]);
                        }

                        bool isValid;
                        if (iterations > 0)
                        {
                            isValid = HashesMatch(HashPassword(password, salt, iterations), storedHash);
                        }
                        else
                        {
                            // Legacy single-round SHA256 hash from before PBKDF2 was introduced.
                            isValid = HashesMatch(LegacyHashPassword(password, salt), storedHash);
                            if (isValid)
                                UpgradeToPbkdf2(conn, username, password);
                        }

                        if (!isValid)
                        {
                            errorMessage = "Invalid username or password.";
                            return false;
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // Transparently re-hashes a legacy password with PBKDF2 on successful login,
        // so it never has to be verified against the weaker SHA256 hash again.
        private static void UpgradeToPbkdf2(SQLiteConnection conn, string username, string password)
        {
            string newSalt = Guid.NewGuid().ToString("N");
            string newHash = HashPassword(password, newSalt, DefaultIterations);

            using (SQLiteCommand update = new SQLiteCommand(
                "UPDATE Users SET PasswordHash = @h, Salt = @s, Iterations = @i WHERE Username = @u", conn))
            {
                update.Parameters.AddWithValue("@h", newHash);
                update.Parameters.AddWithValue("@s", newSalt);
                update.Parameters.AddWithValue("@i", DefaultIterations);
                update.Parameters.AddWithValue("@u", username);
                update.ExecuteNonQuery();
            }
        }
    }

    public class LoginForm : Form
    {
        private readonly string connectionString;
        private TextBox txtUsername;
        private TextBox txtPassword;

        public string AuthenticatedUsername { get; private set; }

        public LoginForm(string connectionString)
        {
            this.connectionString = connectionString;

            this.Text = "Patient Management System - Login";
            this.Size = new Size(360, 230);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lblUsername = new Label() { Text = "Username:", Location = new Point(20, 30), Size = new Size(90, 25) };
            txtUsername = new TextBox() { Location = new Point(120, 30), Size = new Size(190, 25), Text = "admin" };

            Label lblPassword = new Label() { Text = "Password:", Location = new Point(20, 70), Size = new Size(90, 25) };
            txtPassword = new TextBox() { Location = new Point(120, 70), Size = new Size(190, 25), UseSystemPasswordChar = true };

            Button btnLogin = new Button() { Text = "Login", Location = new Point(120, 110), Size = new Size(100, 32) };
            btnLogin.BackColor = Color.FromArgb(0, 123, 255);
            btnLogin.ForeColor = Color.White;
            btnLogin.FlatStyle = FlatStyle.Flat;
            btnLogin.Click += (s, e) => AttemptLogin();

            Label lblHint = new Label()
            {
                Text = "Default login: admin / admin123",
                ForeColor = Color.Gray,
                Location = new Point(20, 155),
                Size = new Size(300, 20)
            };

            this.AcceptButton = btnLogin;
            this.Controls.AddRange(new Control[] { lblUsername, txtUsername, lblPassword, txtPassword, btnLogin, lblHint });
        }

        private void AttemptLogin()
        {
            if (string.IsNullOrWhiteSpace(txtUsername.Text) || string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show("Please enter both username and password.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (AuthHelper.ValidateLogin(connectionString, txtUsername.Text.Trim(), txtPassword.Text, out string error))
            {
                AuthenticatedUsername = txtUsername.Text.Trim();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show(error ?? "Login failed.", "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public partial class MainForm : Form
    {
        private SQLiteConnection connection;
        private string connectionString;
        private TabControl mainTabControl;
        private readonly string currentUsername;

        public MainForm(string username)
        {
            currentUsername = username;
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
            CreateDashboardTab();
            CreatePatientRegistrationTab();
            CreateAppointmentTab();
            CreatePrescriptionTab();
            CreateBillingTab();
            CreateReportsTab();

            mainTabControl.SelectedIndexChanged += (s, e) => {
                if (mainTabControl.SelectedTab?.Name == "dashboardTab")
                    RefreshDashboard();
            };

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
            toolsMenu.DropDownItems.Add("View Audit Log", null, ShowAuditLog);
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

            using (SQLiteCommand cmd = new SQLiteCommand(connection))
            {
                foreach (string createTable in DbInit.Scripts)
                {
                    cmd.CommandText = createTable;
                    cmd.ExecuteNonQuery();
                }
            }

            connection.Close();
        }

        // DASHBOARD TAB
        private void CreateDashboardTab()
        {
            TabPage dashboardTab = new TabPage("Dashboard");
            dashboardTab.Name = "dashboardTab";
            dashboardTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);
            mainPanel.AutoScroll = true;

            // Summary Cards
            Panel summaryPanel = new Panel();
            summaryPanel.Size = new Size(800, 100);
            summaryPanel.Location = new Point(10, 10);

            var (appointmentsCard, lblAppointmentsValue) = CreateDashboardCard("Today's Appointments", "0", Color.FromArgb(40, 167, 69));
            appointmentsCard.Location = new Point(0, 0);
            lblAppointmentsValue.Name = "lblDashTodayAppointments";

            var (pendingBillsCard, lblPendingBillsValue) = CreateDashboardCard("Pending Bills", "0", Color.FromArgb(255, 193, 7));
            pendingBillsCard.Location = new Point(200, 0);
            lblPendingBillsValue.Name = "lblDashPendingBills";

            var (revenueCard, lblRevenueValue) = CreateDashboardCard("Revenue This Month", "₹0.00", Color.FromArgb(220, 53, 69));
            revenueCard.Location = new Point(400, 0);
            lblRevenueValue.Name = "lblDashMonthRevenue";

            Button btnRefreshDashboard = new Button() { Text = "Refresh", Location = new Point(630, 10), Size = new Size(160, 35) };
            btnRefreshDashboard.BackColor = Color.FromArgb(0, 123, 255);
            btnRefreshDashboard.ForeColor = Color.White;
            btnRefreshDashboard.FlatStyle = FlatStyle.Flat;
            btnRefreshDashboard.Click += (s, e) => RefreshDashboard();

            summaryPanel.Controls.AddRange(new Control[] { appointmentsCard, pendingBillsCard, revenueCard, btnRefreshDashboard });

            // Today's Appointments
            GroupBox appointmentsGroup = new GroupBox(); appointmentsGroup.Text = "Today's Appointments";
            appointmentsGroup.Size = new Size(800, 250);
            appointmentsGroup.Location = new Point(10, 120);
            appointmentsGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            DataGridView dgvDashAppointments = new DataGridView() {
                Name = "dgvDashTodayAppointments",
                Location = new Point(15, 30),
                Size = new Size(770, 205)
            };
            dgvDashAppointments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDashAppointments.ReadOnly = true;
            appointmentsGroup.Controls.Add(dgvDashAppointments);

            // Pending Bills
            GroupBox pendingBillsGroup = new GroupBox(); pendingBillsGroup.Text = "Pending Bills";
            pendingBillsGroup.Size = new Size(800, 250);
            pendingBillsGroup.Location = new Point(10, 380);
            pendingBillsGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            DataGridView dgvDashPendingBills = new DataGridView() {
                Name = "dgvDashPendingBills",
                Location = new Point(15, 30),
                Size = new Size(770, 205)
            };
            dgvDashPendingBills.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDashPendingBills.ReadOnly = true;
            pendingBillsGroup.Controls.Add(dgvDashPendingBills);

            mainPanel.Controls.AddRange(new Control[] { summaryPanel, appointmentsGroup, pendingBillsGroup });
            dashboardTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(dashboardTab);

            RefreshDashboard();
        }

        private (Panel, Label) CreateDashboardCard(string title, string value, Color color)
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
            return (card, lblValue);
        }

        private void RefreshDashboard()
        {
            Label lblAppointments = FindControlsRecursive(mainTabControl, c => c.Name == "lblDashTodayAppointments").FirstOrDefault() as Label;
            if (lblAppointments != null) lblAppointments.Text = GetTodayAppointments().ToString();

            Label lblPendingBills = FindControlsRecursive(mainTabControl, c => c.Name == "lblDashPendingBills").FirstOrDefault() as Label;
            if (lblPendingBills != null) lblPendingBills.Text = GetPendingBills().ToString();

            Label lblMonthRevenue = FindControlsRecursive(mainTabControl, c => c.Name == "lblDashMonthRevenue").FirstOrDefault() as Label;
            if (lblMonthRevenue != null) lblMonthRevenue.Text = $"₹{GetMonthRevenue():F2}";

            DataGridView dgvAppointments = FindControlsRecursive(mainTabControl, c => c.Name == "dgvDashTodayAppointments").FirstOrDefault() as DataGridView;
            if (dgvAppointments != null) LoadDashboardTodayAppointments(dgvAppointments);

            DataGridView dgvPendingBills = FindControlsRecursive(mainTabControl, c => c.Name == "dgvDashPendingBills").FirstOrDefault() as DataGridView;
            if (dgvPendingBills != null) LoadDashboardPendingBills(dgvPendingBills);
        }

        private void LoadDashboardTodayAppointments(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT a.AppointmentID, p.FirstName || ' ' || p.LastName as PatientName,
                    a.DoctorName, a.AppointmentTime, a.Department, a.Status
                    FROM Appointments a
                    JOIN Patients p ON a.PatientID = p.PatientID
                    WHERE DATE(a.AppointmentDate) = DATE('now')
                    ORDER BY a.AppointmentTime";

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
                MessageBox.Show($"Error loading today's appointments: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDashboardPendingBills(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT b.BillID, p.FirstName || ' ' || p.LastName as PatientName,
                    b.ServiceDescription, b.Amount, b.DueDate
                    FROM Billing b
                    JOIN Patients p ON b.PatientID = p.PatientID
                    WHERE b.PaymentStatus = 'Pending'
                    ORDER BY b.DueDate";

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
                MessageBox.Show($"Error loading pending bills: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private decimal GetMonthRevenue()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COALESCE(SUM(Amount), 0) FROM Billing WHERE strftime('%Y-%m', BillDate) = strftime('%Y-%m', 'now') AND PaymentStatus = 'Paid'", connection))
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

        // PATIENT REGISTRATION TAB
        private void CreatePatientRegistrationTab()
        {
            TabPage patientTab = new TabPage("Patient Registration");
            patientTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);
            mainPanel.AutoScroll = true;

            // Create form controls
            GroupBox personalInfoGroup = new GroupBox(); personalInfoGroup.Text = "Personal Information";
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
            GroupBox medicalInfoGroup = new GroupBox(); medicalInfoGroup.Text = "Medical Information";
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

            Button btnDeletePatient = new Button() { Text = "Delete Patient", Location = new Point(230, 440), Size = new Size(120, 35) };
            btnDeletePatient.BackColor = Color.FromArgb(220, 53, 69);
            btnDeletePatient.ForeColor = Color.White;
            btnDeletePatient.FlatStyle = FlatStyle.Flat;

            // Search
            Label lblSearchPatient = new Label() { Text = "Search:", Location = new Point(10, 490), Size = new Size(60, 25) };
            TextBox txtSearchPatient = new TextBox() { Name = "txtSearchPatient", Location = new Point(75, 490), Size = new Size(300, 25) };

            // Patient List
            DataGridView dgvPatients = new DataGridView() { Name = "dgvPatients", Location = new Point(10, 520), Size = new Size(800, 220) };
            dgvPatients.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPatients.ReadOnly = true;
            dgvPatients.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadPatients(dgvPatients);

            txtSearchPatient.TextChanged += (s, e) => LoadPatients(dgvPatients, txtSearchPatient.Text);
            btnDeletePatient.Click += (s, e) => DeletePatient(dgvPatients);

            mainPanel.Controls.AddRange(new Control[] {
                personalInfoGroup, medicalInfoGroup, btnSavePatient, btnClearPatient, btnDeletePatient,
                lblSearchPatient, txtSearchPatient, dgvPatients
            });
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
            mainPanel.AutoScroll = true;

            GroupBox appointmentGroup = new GroupBox(); appointmentGroup.Text = "Schedule Appointment";
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

            // Search
            Label lblSearchAppointment = new Label() { Text = "Search:", Location = new Point(10, 270), Size = new Size(60, 25) };
            TextBox txtSearchAppointment = new TextBox() { Name = "txtSearchAppointment", Location = new Point(75, 270), Size = new Size(300, 25) };

            // Appointments List
            DataGridView dgvAppointments = new DataGridView() { Name = "dgvAppointments", Location = new Point(10, 300), Size = new Size(800, 270) };
            dgvAppointments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvAppointments.ReadOnly = true;
            dgvAppointments.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadAppointments(dgvAppointments);

            txtSearchAppointment.TextChanged += (s, e) => LoadAppointments(dgvAppointments, txtSearchAppointment.Text);

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

            Button btnDeleteAppointment = new Button() { Text = "Delete", Location = new Point(390, 220), Size = new Size(90, 35) };
            btnDeleteAppointment.BackColor = Color.FromArgb(108, 117, 125);
            btnDeleteAppointment.ForeColor = Color.White;
            btnDeleteAppointment.FlatStyle = FlatStyle.Flat;
            btnDeleteAppointment.Click += (s, e) => DeleteAppointment(dgvAppointments);

            mainPanel.Controls.AddRange(new Control[] {
                appointmentGroup, btnSaveAppointment, btnCompleted, btnCancelled, btnDeleteAppointment,
                lblSearchAppointment, txtSearchAppointment, dgvAppointments
            });
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
            mainPanel.AutoScroll = true;

            GroupBox prescriptionGroup = new GroupBox(); prescriptionGroup.Text = "Create Prescription";
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
            btnPrintPrescription.Click += (s, e) => PrintPrescription(prescriptionGroup, cmbPatient);

            Button btnDeletePrescription = new Button() { Text = "Delete", Location = new Point(250, 320), Size = new Size(90, 35) };
            btnDeletePrescription.BackColor = Color.FromArgb(220, 53, 69);
            btnDeletePrescription.ForeColor = Color.White;
            btnDeletePrescription.FlatStyle = FlatStyle.Flat;

            // Search
            Label lblSearchPrescription = new Label() { Text = "Search:", Location = new Point(10, 370), Size = new Size(60, 25) };
            TextBox txtSearchPrescription = new TextBox() { Name = "txtSearchPrescription", Location = new Point(75, 370), Size = new Size(300, 25) };

            // Prescriptions List
            DataGridView dgvPrescriptions = new DataGridView() { Name = "dgvPrescriptions", Location = new Point(10, 400), Size = new Size(800, 220) };
            dgvPrescriptions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPrescriptions.ReadOnly = true;
            dgvPrescriptions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadPrescriptions(dgvPrescriptions);

            txtSearchPrescription.TextChanged += (s, e) => LoadPrescriptions(dgvPrescriptions, txtSearchPrescription.Text);
            btnDeletePrescription.Click += (s, e) => DeletePrescription(dgvPrescriptions);

            mainPanel.Controls.AddRange(new Control[] {
                prescriptionGroup, btnSavePrescription, btnPrintPrescription, btnDeletePrescription,
                lblSearchPrescription, txtSearchPrescription, dgvPrescriptions
            });
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
            mainPanel.AutoScroll = true;

            GroupBox billingGroup = new GroupBox(); billingGroup.Text = "Create Bill";
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

            Button btnDeleteBill = new Button() { Text = "Delete", Location = new Point(230, 220), Size = new Size(90, 35) };
            btnDeleteBill.BackColor = Color.FromArgb(220, 53, 69);
            btnDeleteBill.ForeColor = Color.White;
            btnDeleteBill.FlatStyle = FlatStyle.Flat;

            // Search
            Label lblSearchBill = new Label() { Text = "Search:", Location = new Point(10, 270), Size = new Size(60, 25) };
            TextBox txtSearchBill = new TextBox() { Name = "txtSearchBill", Location = new Point(75, 270), Size = new Size(300, 25) };

            // Bills List
            DataGridView dgvBills = new DataGridView() { Name = "dgvBills", Location = new Point(10, 300), Size = new Size(800, 270) };
            dgvBills.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvBills.ReadOnly = true;
            dgvBills.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadBills(dgvBills);

            txtSearchBill.TextChanged += (s, e) => LoadBills(dgvBills, txtSearchBill.Text);

            btnMarkPaid.Click += (s, e) => {
                if (dgvBills.SelectedRows.Count > 0)
                {
                    int billId = Convert.ToInt32(dgvBills.SelectedRows[0].Cells["BillID"].Value);
                    UpdateBillStatus(billId, "Paid");
                    LoadBills(dgvBills);
                }
            };

            btnDeleteBill.Click += (s, e) => DeleteBill(dgvBills);

            mainPanel.Controls.AddRange(new Control[] {
                billingGroup, btnSaveBill, btnMarkPaid, btnDeleteBill,
                lblSearchBill, txtSearchBill, dgvBills
            });
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
            GroupBox dateRangeGroup = new GroupBox(); dateRangeGroup.Text = "Sales Summary";
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

                if (!Regex.IsMatch(txtPhone.Text.Trim(), @"^[0-9+\-\s()]{7,20}$"))
                {
                    MessageBox.Show("Please enter a valid phone number.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(txtEmail.Text) && !Regex.IsMatch(txtEmail.Text.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    MessageBox.Show("Please enter a valid email address.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dtpDOB.Value.Date > DateTime.Today)
                {
                    MessageBox.Show("Date of birth cannot be in the future.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Patients
                    (FirstName, LastName, DateOfBirth, Gender, PhoneNumber, Email, Address, EmergencyContact, BloodGroup, MedicalHistory)
                    VALUES (@fname, @lname, @dob, @gender, @phone, @email, @address, @emergency, @bloodgroup, @medical)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@fname", txtFirstName.Text.Trim());
                    cmd.Parameters.AddWithValue("@lname", txtLastName.Text.Trim());
                    cmd.Parameters.AddWithValue("@dob", dtpDOB.Value.Date);
                    cmd.Parameters.AddWithValue("@gender", cmbGender.Text);
                    cmd.Parameters.AddWithValue("@phone", txtPhone.Text.Trim());
                    cmd.Parameters.AddWithValue("@email", txtEmail.Text.Trim());
                    cmd.Parameters.AddWithValue("@address", txtAddress.Text.Trim());
                    cmd.Parameters.AddWithValue("@emergency", txtEmergency.Text.Trim());
                    cmd.Parameters.AddWithValue("@bloodgroup", cmbBloodGroup.Text);
                    cmd.Parameters.AddWithValue("@medical", txtMedicalHistory.Text.Trim());

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Register Patient", $"{txtFirstName.Text.Trim()} {txtLastName.Text.Trim()}");

                MessageBox.Show("Patient registered successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearPatientForm(personalInfo, medicalInfo);

                // Refresh patient list
                TextBox txtSearchPatient = personalInfo.Parent.Controls["txtSearchPatient"] as TextBox;
                txtSearchPatient?.Clear();
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

        private void LoadPatients(DataGridView dgv, string search = "")
        {
            try
            {
                connection.Open();
                string query = "SELECT PatientID, FirstName || ' ' || LastName as FullName, DateOfBirth, Gender, PhoneNumber, Email FROM Patients";
                if (!string.IsNullOrWhiteSpace(search))
                    query += " WHERE FirstName LIKE @s OR LastName LIKE @s OR PhoneNumber LIKE @s";
                query += " ORDER BY RegistrationDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        adapter.SelectCommand.Parameters.AddWithValue("@s", $"%{search.Trim()}%");

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
            foreach (Control control in FindControlsRecursive(mainTabControl, c => c is ComboBox cmb && cmb.Name == "cmbPatient"))
            {
                LoadPatientsInComboBox((ComboBox)control);
            }
        }

        private IEnumerable<Control> FindControlsRecursive(Control root, Func<Control, bool> predicate)
        {
            foreach (Control child in root.Controls)
            {
                if (predicate(child))
                    yield return child;

                foreach (Control nested in FindControlsRecursive(child, predicate))
                    yield return nested;
            }
        }

        private void RefreshAllGrids()
        {
            DataGridView dgvPatients = FindControlsRecursive(mainTabControl, c => c.Name == "dgvPatients").FirstOrDefault() as DataGridView;
            if (dgvPatients != null) LoadPatients(dgvPatients);

            DataGridView dgvAppointments = FindControlsRecursive(mainTabControl, c => c.Name == "dgvAppointments").FirstOrDefault() as DataGridView;
            if (dgvAppointments != null) LoadAppointments(dgvAppointments);

            DataGridView dgvPrescriptions = FindControlsRecursive(mainTabControl, c => c.Name == "dgvPrescriptions").FirstOrDefault() as DataGridView;
            if (dgvPrescriptions != null) LoadPrescriptions(dgvPrescriptions);

            DataGridView dgvBills = FindControlsRecursive(mainTabControl, c => c.Name == "dgvBills").FirstOrDefault() as DataGridView;
            if (dgvBills != null) LoadBills(dgvBills);

            RefreshDashboard();
        }

        private void LogAudit(string action, string details)
        {
            try
            {
                using (SQLiteConnection auditConn = new SQLiteConnection(connectionString))
                {
                    auditConn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(
                        "INSERT INTO AuditLog (Username, Action, Details) VALUES (@u, @a, @d)", auditConn))
                    {
                        cmd.Parameters.AddWithValue("@u", currentUsername ?? "unknown");
                        cmd.Parameters.AddWithValue("@a", action);
                        cmd.Parameters.AddWithValue("@d", details ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                // Audit logging must never block the primary workflow.
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

        private void DeletePatient(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count == 0)
                return;

            DialogResult confirm = MessageBox.Show(
                "Deleting this patient will also permanently delete all of their appointments, prescriptions, and bills. Continue?",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            int patientId = Convert.ToInt32(dgv.SelectedRows[0].Cells["PatientID"].Value);

            try
            {
                connection.Open();
                using (SQLiteTransaction tx = connection.BeginTransaction())
                {
                    DeleteByPatientId("DELETE FROM Billing WHERE PatientID = @id", patientId, tx);
                    DeleteByPatientId("DELETE FROM Prescriptions WHERE PatientID = @id", patientId, tx);
                    DeleteByPatientId("DELETE FROM Appointments WHERE PatientID = @id", patientId, tx);
                    DeleteByPatientId("DELETE FROM Patients WHERE PatientID = @id", patientId, tx);
                    tx.Commit();
                }
                connection.Close();

                LogAudit("Delete Patient", $"PatientID {patientId}");

                RefreshAllGrids();
                RefreshPatientComboBoxes();

                MessageBox.Show("Patient and all related records were deleted.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error deleting patient: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteByPatientId(string sql, int patientId, SQLiteTransaction tx)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(sql, connection, tx))
            {
                cmd.Parameters.AddWithValue("@id", patientId);
                cmd.ExecuteNonQuery();
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

                if (DateTime.TryParse($"{dtpAppDate.Value.Date:yyyy-MM-dd} {cmbAppTime.Text}", out DateTime appointmentDateTime)
                    && appointmentDateTime < DateTime.Now)
                {
                    MessageBox.Show("Please choose an appointment date and time that hasn't already passed.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();

                using (SQLiteCommand conflictCmd = new SQLiteCommand(
                    "SELECT COUNT(*) FROM Appointments WHERE DoctorName = @doctor AND AppointmentDate = @date AND AppointmentTime = @time AND Status != 'Cancelled'",
                    connection))
                {
                    conflictCmd.Parameters.AddWithValue("@doctor", txtDoctor.Text.Trim());
                    conflictCmd.Parameters.AddWithValue("@date", dtpAppDate.Value.Date);
                    conflictCmd.Parameters.AddWithValue("@time", cmbAppTime.Text);

                    int conflictCount = Convert.ToInt32(conflictCmd.ExecuteScalar());
                    if (conflictCount > 0)
                    {
                        connection.Close();
                        MessageBox.Show("This doctor already has an appointment at the selected date and time.", "Scheduling Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                string query = @"INSERT INTO Appointments
                    (PatientID, DoctorName, AppointmentDate, AppointmentTime, Department, Notes)
                    VALUES (@patientid, @doctor, @date, @time, @dept, @notes)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@doctor", txtDoctor.Text.Trim());
                    cmd.Parameters.AddWithValue("@date", dtpAppDate.Value.Date);
                    cmd.Parameters.AddWithValue("@time", cmbAppTime.Text);
                    cmd.Parameters.AddWithValue("@dept", cmbDepartment.Text);
                    cmd.Parameters.AddWithValue("@notes", txtNotes.Text.Trim());

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Schedule Appointment", $"Doctor {txtDoctor.Text.Trim()} on {dtpAppDate.Value.Date:d} {cmbAppTime.Text}");

                MessageBox.Show("Appointment scheduled successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Clear form and refresh list
                foreach (Control control in appointmentGroup.Controls)
                {
                    if (control is TextBox txt)
                        txt.Clear();
                    else if (control is ComboBox cmb && cmb.Name != "cmbPatient")
                        cmb.SelectedIndex = -1;
                }

                TextBox txtSearchAppointment = appointmentGroup.Parent.Controls["txtSearchAppointment"] as TextBox;
                txtSearchAppointment?.Clear();
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

        private void LoadAppointments(DataGridView dgv, string search = "")
        {
            try
            {
                connection.Open();
                string query = @"SELECT a.AppointmentID, p.FirstName || ' ' || p.LastName as PatientName,
                    a.DoctorName, a.AppointmentDate, a.AppointmentTime, a.Department, a.Status, a.Notes
                    FROM Appointments a
                    JOIN Patients p ON a.PatientID = p.PatientID";

                if (!string.IsNullOrWhiteSpace(search))
                    query += " WHERE p.FirstName LIKE @s OR p.LastName LIKE @s OR a.DoctorName LIKE @s";

                query += " ORDER BY a.AppointmentDate DESC, a.AppointmentTime";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        adapter.SelectCommand.Parameters.AddWithValue("@s", $"%{search.Trim()}%");

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
                if (status == "Cancelled")
                {
                    DialogResult confirm = MessageBox.Show("Are you sure you want to cancel this appointment?", "Confirm Cancellation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes)
                        return;
                }

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

                    LogAudit("Update Appointment Status", $"AppointmentID {appointmentId} -> {status}");

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

        private void DeleteAppointment(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count == 0)
                return;

            DialogResult confirm = MessageBox.Show("Delete this appointment? This cannot be undone.", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                int appointmentId = Convert.ToInt32(dgv.SelectedRows[0].Cells["AppointmentID"].Value);

                connection.Open();
                using (SQLiteCommand unlinkCmd = new SQLiteCommand("UPDATE Billing SET AppointmentID = NULL WHERE AppointmentID = @id", connection))
                {
                    unlinkCmd.Parameters.AddWithValue("@id", appointmentId);
                    unlinkCmd.ExecuteNonQuery();
                }

                using (SQLiteCommand deleteCmd = new SQLiteCommand("DELETE FROM Appointments WHERE AppointmentID = @id", connection))
                {
                    deleteCmd.Parameters.AddWithValue("@id", appointmentId);
                    deleteCmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Delete Appointment", $"AppointmentID {appointmentId}");

                MessageBox.Show("Appointment deleted.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadAppointments(dgv);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error deleting appointment: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                if (dtpFollowUp.Value.Date < DateTime.Today)
                {
                    MessageBox.Show("Follow-up date cannot be in the past.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Prescriptions
                    (PatientID, DoctorName, PrescriptionDate, Diagnosis, Medicines, Instructions, FollowUpDate)
                    VALUES (@patientid, @doctor, @date, @diagnosis, @medicines, @instructions, @followup)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@doctor", txtDoctor.Text.Trim());
                    cmd.Parameters.AddWithValue("@date", DateTime.Today);
                    cmd.Parameters.AddWithValue("@diagnosis", txtDiagnosis.Text.Trim());
                    cmd.Parameters.AddWithValue("@medicines", txtMedicines.Text.Trim());
                    cmd.Parameters.AddWithValue("@instructions", txtInstructions.Text.Trim());
                    cmd.Parameters.AddWithValue("@followup", dtpFollowUp.Value.Date);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Create Prescription", $"Diagnosis: {txtDiagnosis.Text.Trim()}");

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

                TextBox txtSearchPrescription = prescriptionGroup.Parent.Controls["txtSearchPrescription"] as TextBox;
                txtSearchPrescription?.Clear();
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

        private void PrintPrescription(GroupBox prescriptionGroup, ComboBox cmbPatient)
        {
            TextBox txtDoctor = prescriptionGroup.Controls["txtDoctor"] as TextBox;
            TextBox txtDiagnosis = prescriptionGroup.Controls["txtDiagnosis"] as TextBox;
            TextBox txtMedicines = prescriptionGroup.Controls["txtMedicines"] as TextBox;
            TextBox txtInstructions = prescriptionGroup.Controls["txtInstructions"] as TextBox;
            DateTimePicker dtpFollowUp = prescriptionGroup.Controls["dtpFollowUp"] as DateTimePicker;

            if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtDoctor.Text) ||
                string.IsNullOrWhiteSpace(txtDiagnosis.Text) || string.IsNullOrWhiteSpace(txtMedicines.Text))
            {
                MessageBox.Show("Please fill in the prescription fields before printing.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string patientName = ((DataRowView)cmbPatient.SelectedItem)["DisplayName"].ToString();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PRESCRIPTION");
            sb.AppendLine($"Date: {DateTime.Today:d}");
            sb.AppendLine();
            sb.AppendLine($"Patient: {patientName}");
            sb.AppendLine($"Doctor: {txtDoctor.Text}");
            sb.AppendLine();
            sb.AppendLine($"Diagnosis: {txtDiagnosis.Text}");
            sb.AppendLine();
            sb.AppendLine("Medicines:");
            sb.AppendLine(txtMedicines.Text);
            if (!string.IsNullOrWhiteSpace(txtInstructions.Text))
            {
                sb.AppendLine();
                sb.AppendLine($"Instructions: {txtInstructions.Text}");
            }
            sb.AppendLine();
            sb.AppendLine($"Follow-up Date: {dtpFollowUp.Value.Date:d}");

            string content = sb.ToString();

            try
            {
                PrintDocument printDoc = new PrintDocument();
                Font printFont = new Font("Segoe UI", 11F);
                printDoc.PrintPage += (s, e) =>
                {
                    e.Graphics.DrawString(content, printFont, Brushes.Black, e.MarginBounds);
                };

                using (PrintPreviewDialog previewDialog = new PrintPreviewDialog())
                {
                    previewDialog.Document = printDoc;
                    previewDialog.Width = 800;
                    previewDialog.Height = 900;
                    previewDialog.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error printing prescription: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadPrescriptions(DataGridView dgv, string search = "")
        {
            try
            {
                connection.Open();
                string query = @"SELECT pr.PrescriptionID, p.FirstName || ' ' || p.LastName as PatientName,
                    pr.DoctorName, pr.PrescriptionDate, pr.Diagnosis, pr.Medicines, pr.FollowUpDate
                    FROM Prescriptions pr
                    JOIN Patients p ON pr.PatientID = p.PatientID";

                if (!string.IsNullOrWhiteSpace(search))
                    query += " WHERE p.FirstName LIKE @s OR p.LastName LIKE @s OR pr.Diagnosis LIKE @s";

                query += " ORDER BY pr.PrescriptionDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        adapter.SelectCommand.Parameters.AddWithValue("@s", $"%{search.Trim()}%");

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

        private void DeletePrescription(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count == 0)
                return;

            DialogResult confirm = MessageBox.Show("Delete this prescription? This cannot be undone.", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                int prescriptionId = Convert.ToInt32(dgv.SelectedRows[0].Cells["PrescriptionID"].Value);

                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("DELETE FROM Prescriptions WHERE PrescriptionID = @id", connection))
                {
                    cmd.Parameters.AddWithValue("@id", prescriptionId);
                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Delete Prescription", $"PrescriptionID {prescriptionId}");

                MessageBox.Show("Prescription deleted.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadPrescriptions(dgv);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error deleting prescription: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                if (dtpDueDate.Value.Date < DateTime.Today)
                {
                    MessageBox.Show("Due date cannot be in the past.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connection.Open();
                string query = @"INSERT INTO Billing
                    (PatientID, ServiceDescription, Amount, PaymentStatus, PaymentMethod, BillDate, DueDate)
                    VALUES (@patientid, @service, @amount, @status, @method, @billdate, @duedate)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@service", txtService.Text.Trim());
                    cmd.Parameters.AddWithValue("@amount", numAmount.Value);
                    cmd.Parameters.AddWithValue("@status", cmbStatus.Text);
                    cmd.Parameters.AddWithValue("@method", cmbPaymentMethod.Text);
                    cmd.Parameters.AddWithValue("@billdate", DateTime.Today);
                    cmd.Parameters.AddWithValue("@duedate", dtpDueDate.Value.Date);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Create Bill", $"Service: {txtService.Text.Trim()}, Amount: {numAmount.Value}");

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

                TextBox txtSearchBill = billingGroup.Parent.Controls["txtSearchBill"] as TextBox;
                txtSearchBill?.Clear();
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

        private void LoadBills(DataGridView dgv, string search = "")
        {
            try
            {
                connection.Open();
                string query = @"SELECT b.BillID, p.FirstName || ' ' || p.LastName as PatientName,
                    b.ServiceDescription, b.Amount, b.PaymentStatus, b.PaymentMethod, b.BillDate, b.DueDate
                    FROM Billing b
                    JOIN Patients p ON b.PatientID = p.PatientID";

                if (!string.IsNullOrWhiteSpace(search))
                    query += " WHERE p.FirstName LIKE @s OR p.LastName LIKE @s OR b.PaymentStatus LIKE @s";

                query += " ORDER BY b.BillDate DESC";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        adapter.SelectCommand.Parameters.AddWithValue("@s", $"%{search.Trim()}%");

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

                LogAudit("Update Bill Status", $"BillID {billId} -> {status}");

                MessageBox.Show($"Bill marked as {status.ToLower()}!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error updating bill: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteBill(DataGridView dgv)
        {
            if (dgv.SelectedRows.Count == 0)
                return;

            DialogResult confirm = MessageBox.Show("Delete this bill? This cannot be undone.", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                int billId = Convert.ToInt32(dgv.SelectedRows[0].Cells["BillID"].Value);

                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("DELETE FROM Billing WHERE BillID = @id", connection))
                {
                    cmd.Parameters.AddWithValue("@id", billId);
                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Delete Bill", $"BillID {billId}");

                MessageBox.Show("Bill deleted.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadBills(dgv);
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error deleting bill: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                            string rowData = string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(cell => EscapeCsvField(cell.Value?.ToString() ?? "")));
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

        private string EscapeCsvField(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
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
                    LogAudit("Backup Database", saveDialog.FileName);
                    MessageBox.Show("Database backed up successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error backing up database: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowAuditLog(object sender, EventArgs e)
        {
            try
            {
                connection.Open();
                DataTable dt = new DataTable();
                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(
                    "SELECT Username, Action, Details, Timestamp FROM AuditLog ORDER BY Timestamp DESC", connection))
                {
                    adapter.Fill(dt);
                }
                connection.Close();

                using (Form auditForm = new Form())
                {
                    auditForm.Text = "Audit Log";
                    auditForm.Size = new Size(750, 500);
                    auditForm.StartPosition = FormStartPosition.CenterParent;

                    DataGridView dgv = new DataGridView()
                    {
                        Dock = DockStyle.Fill,
                        ReadOnly = true,
                        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                        DataSource = dt
                    };

                    auditForm.Controls.Add(dgv);
                    auditForm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading audit log: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            MessageBox.Show("Settings feature will be implemented in future versions.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            string aboutText = @"Patient Management System v1.0

A comprehensive healthcare management solution
Features: Patient Registration, Appointments, Prescriptions, Billing & Reports

© 2025 - Healthcare Solutions";

            MessageBox.Show(aboutText, "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            string dbPath = Path.Combine(Application.StartupPath, "PatientManagement.db");
            string connectionString = $"Data Source={dbPath};Version=3;";

            try
            {
                DbInit.EnsureSchema(connectionString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database initialization error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string authenticatedUsername;
            using (LoginForm loginForm = new LoginForm(connectionString))
            {
                if (loginForm.ShowDialog() != DialogResult.OK)
                    return;

                authenticatedUsername = loginForm.AuthenticatedUsername;
            }

            Application.Run(new MainForm(authenticatedUsername));
        }
    }
}
