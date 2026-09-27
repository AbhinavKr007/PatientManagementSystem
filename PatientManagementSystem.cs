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
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PatientManagementSystem.Tests")]

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
                AmountPaid DECIMAL(10,2) NOT NULL DEFAULT 0,
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
                Role TEXT NOT NULL DEFAULT 'User',
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            )",

            @"CREATE TABLE IF NOT EXISTS AuditLog (
                AuditLogID INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                Action TEXT NOT NULL,
                Details TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            )",

            @"CREATE TABLE IF NOT EXISTS Doctors (
                DoctorID INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Specialization TEXT,
                CreatedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            )",

            @"CREATE TABLE IF NOT EXISTS HospitalProfile (
                ProfileID INTEGER PRIMARY KEY CHECK (ProfileID = 1),
                HospitalName TEXT NOT NULL DEFAULT 'Patient Management System',
                Motto TEXT,
                Address TEXT,
                Phone TEXT,
                Logo BLOB
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
                MigrateAddRoleColumn(conn);
                MigrateAddAmountPaidColumn(conn);
                EnsureDefaultAdmin(conn);
                EnsureDefaultHospitalProfile(conn);
            }
        }

        // Existing installs created their Users table before the Role column
        // existed; CREATE TABLE IF NOT EXISTS won't retrofit it, so add it here.
        private static void MigrateAddRoleColumn(SQLiteConnection conn)
        {
            try
            {
                using (SQLiteCommand alter = new SQLiteCommand(
                    "ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT 'User'", conn))
                {
                    alter.ExecuteNonQuery();
                }
            }
            catch (SQLiteException)
            {
                // Column already exists.
            }

            // The very first account created (before roles existed) is always
            // the original administrator; make sure it keeps admin rights.
            using (SQLiteCommand promote = new SQLiteCommand(
                "UPDATE Users SET Role = 'Admin' WHERE UserID = (SELECT MIN(UserID) FROM Users)", conn))
            {
                promote.ExecuteNonQuery();
            }
        }

        // Existing installs created their Billing table before the AmountPaid
        // column existed; CREATE TABLE IF NOT EXISTS won't retrofit it.
        private static void MigrateAddAmountPaidColumn(SQLiteConnection conn)
        {
            try
            {
                using (SQLiteCommand alter = new SQLiteCommand(
                    "ALTER TABLE Billing ADD COLUMN AmountPaid DECIMAL(10,2) NOT NULL DEFAULT 0", conn))
                {
                    alter.ExecuteNonQuery();
                }
            }
            catch (SQLiteException)
            {
                // Column already exists.
            }

            // Backfill: bills already marked Paid before this column existed
            // have no recorded AmountPaid yet -- it must equal the full amount.
            using (SQLiteCommand backfill = new SQLiteCommand(
                "UPDATE Billing SET AmountPaid = Amount WHERE PaymentStatus = 'Paid' AND AmountPaid = 0", conn))
            {
                backfill.ExecuteNonQuery();
            }
        }

        private static void EnsureDefaultHospitalProfile(SQLiteConnection conn)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO HospitalProfile (ProfileID, HospitalName) VALUES (1, 'Patient Management System')", conn))
            {
                cmd.ExecuteNonQuery();
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
                        "INSERT INTO Users (Username, PasswordHash, Salt, Iterations, Role) VALUES (@u, @h, @s, @i, 'Admin')", conn))
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

        public static bool ValidateLogin(string connectionString, string username, string password, out string errorMessage, out string role)
        {
            errorMessage = null;
            role = null;
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(
                        "SELECT PasswordHash, Salt, Iterations, Role FROM Users WHERE Username = @u", conn))
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
                            role = reader["Role"].ToString();
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
                            role = null;
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
        public string AuthenticatedRole { get; private set; }

        public LoginForm(string connectionString)
        {
            this.connectionString = connectionString;

            this.Text = "Patient Management System - Login";
            this.Size = new Size(360, 230);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { this.Icon = SystemIcons.Application; }

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

            if (AuthHelper.ValidateLogin(connectionString, txtUsername.Text.Trim(), txtPassword.Text, out string error, out string role))
            {
                AuthenticatedUsername = txtUsername.Text.Trim();
                AuthenticatedRole = role;
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
        private readonly string currentUserRole;
        private System.Windows.Forms.Timer homeClockTimer;
        private bool isFullScreen = false;
        private FormWindowState previousWindowState;

        private bool IsAdmin => string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        public MainForm(string username, string role)
        {
            currentUsername = username;
            currentUserRole = role;
            // Must run before InitializeComponent(): tab creation queries the
            // database immediately (e.g. the Dashboard's initial refresh), and
            // connection is null until this sets it up.
            InitializeDatabase();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Form properties
            this.Text = "Patient Management System";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.FromArgb(240, 248, 255);
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { this.Icon = SystemIcons.Application; }

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.F11)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
            };

            // Create main tab control
            mainTabControl = new TabControl();
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.Font = new Font("Segoe UI", 10F);
            mainTabControl.ItemSize = new Size(150, 30);
            mainTabControl.SizeMode = TabSizeMode.Fixed;

            // Create tabs
            CreateHomeTab();
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

            // View Menu
            ToolStripMenuItem viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.Add("Toggle Full Screen\tF11", null, (s, e) => ToggleFullScreen());

            // Tools Menu
            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add("View Audit Log", null, ShowAuditLog);

            if (IsAdmin)
            {
                toolsMenu.DropDownItems.Add("Manage Doctors", null, ShowManageDoctors);
                toolsMenu.DropDownItems.Add("Manage Users", null, ShowManageUsers);
                toolsMenu.DropDownItems.Add("Hospital Profile", null, ShowSettings);
                toolsMenu.DropDownItems.Add("Backup Database", null, BackupDatabase);
                toolsMenu.DropDownItems.Add("Import Database...", null, ImportDatabase);
            }

            // Help Menu
            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("About", null, ShowAbout);

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu, toolsMenu, helpMenu });
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

        // HOME / LANDING TAB
        private void CreateHomeTab()
        {
            TabPage homeTab = new TabPage("Home");
            homeTab.Name = "homeTab";
            homeTab.BackColor = Color.White;

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);
            mainPanel.AutoScroll = true;

            // Header: logo, hospital name/motto/address/phone, live clock
            Panel headerPanel = new Panel();
            headerPanel.Location = new Point(10, 10);
            headerPanel.Size = new Size(1140, 140);
            headerPanel.BackColor = Color.FromArgb(0, 123, 255);
            headerPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            PictureBox picLogo = new PictureBox() {
                Name = "picHomeLogo",
                Location = new Point(15, 15),
                Size = new Size(110, 110),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };

            Label lblHospitalName = new Label() {
                Name = "lblHomeHospitalName",
                Location = new Point(140, 15),
                Size = new Size(700, 35),
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "Patient Management System"
            };

            Label lblMotto = new Label() {
                Name = "lblHomeMotto",
                Location = new Point(140, 52),
                Size = new Size(700, 25),
                Font = new Font("Segoe UI", 10F, FontStyle.Italic),
                ForeColor = Color.White
            };

            Label lblAddress = new Label() {
                Name = "lblHomeAddress",
                Location = new Point(140, 82),
                Size = new Size(700, 20),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.White
            };

            Label lblPhone = new Label() {
                Name = "lblHomePhone",
                Location = new Point(140, 104),
                Size = new Size(700, 20),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.White
            };

            Label lblClock = new Label() {
                Name = "lblHomeClock",
                Location = new Point(870, 30),
                Size = new Size(260, 60),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight
            };

            headerPanel.Controls.AddRange(new Control[] { picLogo, lblHospitalName, lblMotto, lblAddress, lblPhone, lblClock });

            // Quick access navigation
            Label lblNavTitle = new Label() {
                Text = "Quick Access",
                Location = new Point(10, 165),
                Size = new Size(300, 25),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            };

            Panel navPanel = new Panel();
            navPanel.Location = new Point(10, 195);
            navPanel.Size = new Size(1140, 220);

            var navItems = new (string Label, string TabName, Color Color, Color ForeColor)[] {
                ("Dashboard", "dashboardTab", Color.FromArgb(40, 167, 69), Color.White),
                ("Patient Registration", "Patient Registration", Color.FromArgb(0, 123, 255), Color.White),
                ("Appointments", "Appointments", Color.FromArgb(23, 162, 184), Color.White),
                ("Prescriptions", "Prescriptions", Color.FromArgb(111, 66, 193), Color.White),
                ("Billing", "Billing", Color.FromArgb(255, 193, 7), Color.Black),
                ("Reports & Summary", "Reports & Summary", Color.FromArgb(220, 53, 69), Color.White),
            };

            int navX = 0, navY = 0;
            foreach (var item in navItems)
            {
                Button navButton = new Button() {
                    Text = item.Label,
                    Size = new Size(180, 100),
                    Location = new Point(navX, navY),
                    BackColor = item.Color,
                    ForeColor = item.ForeColor,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold)
                };
                string targetTab = item.TabName;
                navButton.Click += (s, e) => {
                    foreach (TabPage tp in mainTabControl.TabPages)
                    {
                        if (tp.Name == targetTab || tp.Text == targetTab)
                        {
                            mainTabControl.SelectedTab = tp;
                            break;
                        }
                    }
                };
                navPanel.Controls.Add(navButton);

                navX += 200;
                if (navX > 1000) { navX = 0; navY += 120; }
            }

            mainPanel.Controls.AddRange(new Control[] { headerPanel, lblNavTitle, navPanel });
            homeTab.Controls.Add(mainPanel);
            mainTabControl.TabPages.Add(homeTab);

            homeClockTimer = new System.Windows.Forms.Timer();
            homeClockTimer.Interval = 1000;
            homeClockTimer.Tick += (s, e) => {
                Label clockLbl = FindControlsRecursive(mainTabControl, c => c.Name == "lblHomeClock").FirstOrDefault() as Label;
                if (clockLbl != null) clockLbl.Text = DateTime.Now.ToString("dddd, MMM dd, yyyy") + "\n" + DateTime.Now.ToString("hh:mm:ss tt");
            };
            homeClockTimer.Start();

            RefreshHomeProfile();
        }

        private void RefreshHomeProfile()
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "SELECT HospitalName, Motto, Address, Phone, Logo FROM HospitalProfile WHERE ProfileID = 1", connection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string name = reader["HospitalName"].ToString();
                        string motto = reader["Motto"] == DBNull.Value ? "" : reader["Motto"].ToString();
                        string address = reader["Address"] == DBNull.Value ? "" : reader["Address"].ToString();
                        string phone = reader["Phone"] == DBNull.Value ? "" : reader["Phone"].ToString();
                        byte[] logoBytes = reader["Logo"] == DBNull.Value ? null : (byte[])reader["Logo"];

                        Label lblName = FindControlsRecursive(mainTabControl, c => c.Name == "lblHomeHospitalName").FirstOrDefault() as Label;
                        if (lblName != null) lblName.Text = name;

                        Label lblMotto = FindControlsRecursive(mainTabControl, c => c.Name == "lblHomeMotto").FirstOrDefault() as Label;
                        if (lblMotto != null) lblMotto.Text = motto;

                        Label lblAddress = FindControlsRecursive(mainTabControl, c => c.Name == "lblHomeAddress").FirstOrDefault() as Label;
                        if (lblAddress != null) lblAddress.Text = string.IsNullOrWhiteSpace(address) ? "" : $"Address: {address}";

                        Label lblPhone = FindControlsRecursive(mainTabControl, c => c.Name == "lblHomePhone").FirstOrDefault() as Label;
                        if (lblPhone != null) lblPhone.Text = string.IsNullOrWhiteSpace(phone) ? "" : $"Phone: {phone}";

                        PictureBox picLogo = FindControlsRecursive(mainTabControl, c => c.Name == "picHomeLogo").FirstOrDefault() as PictureBox;
                        if (picLogo != null)
                        {
                            Image oldImage = picLogo.Image;
                            picLogo.Image = logoBytes != null ? Image.FromStream(new MemoryStream(logoBytes)) : null;
                            oldImage?.Dispose();
                        }

                        this.Text = $"Patient Management System - {name}";
                    }
                }
                connection.Close();
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
            }
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

            // Reminder Banner
            Label lblReminderBanner = new Label() {
                Name = "lblDashReminderBanner",
                Location = new Point(10, 10),
                Size = new Size(800, 32),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Padding = new Padding(10, 0, 0, 0)
            };

            // Summary Cards
            Panel summaryPanel = new Panel();
            summaryPanel.Size = new Size(800, 100);
            summaryPanel.Location = new Point(10, 52);

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
            appointmentsGroup.Location = new Point(10, 162);
            appointmentsGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            appointmentsGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            DataGridView dgvDashAppointments = new DataGridView() {
                Name = "dgvDashTodayAppointments",
                Location = new Point(15, 30),
                Size = new Size(770, 205),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            dgvDashAppointments.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDashAppointments.ReadOnly = true;
            appointmentsGroup.Controls.Add(dgvDashAppointments);

            // Pending Bills
            GroupBox pendingBillsGroup = new GroupBox(); pendingBillsGroup.Text = "Pending Bills";
            pendingBillsGroup.Size = new Size(800, 250);
            pendingBillsGroup.Location = new Point(10, 422);
            pendingBillsGroup.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            pendingBillsGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            DataGridView dgvDashPendingBills = new DataGridView() {
                Name = "dgvDashPendingBills",
                Location = new Point(15, 30),
                Size = new Size(770, 205),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            dgvDashPendingBills.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvDashPendingBills.ReadOnly = true;
            pendingBillsGroup.Controls.Add(dgvDashPendingBills);

            mainPanel.Controls.AddRange(new Control[] { lblReminderBanner, summaryPanel, appointmentsGroup, pendingBillsGroup });
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

            Label lblReminderBanner = FindControlsRecursive(mainTabControl, c => c.Name == "lblDashReminderBanner").FirstOrDefault() as Label;
            if (lblReminderBanner != null)
            {
                int upcoming = GetUpcomingAppointmentsCount(48);
                if (upcoming > 0)
                {
                    lblReminderBanner.Text = $"⏰ {upcoming} appointment(s) scheduled in the next 48 hours";
                    lblReminderBanner.BackColor = Color.FromArgb(255, 243, 205);
                    lblReminderBanner.ForeColor = Color.FromArgb(133, 100, 4);
                }
                else
                {
                    lblReminderBanner.Text = "No appointments scheduled in the next 48 hours";
                    lblReminderBanner.BackColor = Color.FromArgb(240, 248, 255);
                    lblReminderBanner.ForeColor = Color.FromArgb(108, 117, 125);
                }
            }
        }

        private int GetUpcomingAppointmentsCount(int hours)
        {
            try
            {
                connection.Open();
                // Compare against C#-computed local timestamps rather than SQLite's
                // 'now' (which is always UTC) so this doesn't drift a day off from
                // BillDate/AppointmentDate, which are stored using local DateTime.
                string query = @"SELECT COUNT(*) FROM Appointments
                    WHERE Status = 'Scheduled'
                    AND datetime(DATE(AppointmentDate) || ' ' || AppointmentTime) BETWEEN datetime(@nowlocal) AND datetime(@untillocal)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    DateTime now = DateTime.Now;
                    cmd.Parameters.AddWithValue("@nowlocal", now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@untillocal", now.AddHours(hours).ToString("yyyy-MM-dd HH:mm:ss"));
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

        private void LoadDashboardTodayAppointments(DataGridView dgv)
        {
            try
            {
                connection.Open();
                string query = @"SELECT a.AppointmentID, p.FirstName || ' ' || p.LastName as PatientName,
                    a.DoctorName, a.AppointmentTime, a.Department, a.Status
                    FROM Appointments a
                    JOIN Patients p ON a.PatientID = p.PatientID
                    WHERE DATE(a.AppointmentDate) = @today
                    ORDER BY a.AppointmentTime";

                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(query, connection))
                {
                    adapter.SelectCommand.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));
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
                    b.ServiceDescription, b.Amount as TotalAmount, b.AmountPaid, (b.Amount - b.AmountPaid) as Balance, b.DueDate
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
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COALESCE(SUM(AmountPaid), 0) FROM Billing WHERE strftime('%Y-%m', BillDate) = @yearmonth", connection))
                {
                    cmd.Parameters.AddWithValue("@yearmonth", DateTime.Today.ToString("yyyy-MM"));
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
            personalInfoGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

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
            dtpDOB.Format = DateTimePickerFormat.Custom;
            dtpDOB.CustomFormat = "ddd, MMM dd, yyyy";

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
            medicalInfoGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

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
            Button btnSavePatient = new Button() { Name = "btnSavePatient", Text = "Save Patient", Location = new Point(10, 440), Size = new Size(120, 35) };
            btnSavePatient.BackColor = Color.FromArgb(0, 123, 255);
            btnSavePatient.ForeColor = Color.White;
            btnSavePatient.FlatStyle = FlatStyle.Flat;
            btnSavePatient.Click += (s, e) => SavePatient(personalInfoGroup, medicalInfoGroup, btnSavePatient);

            Button btnClearPatient = new Button() { Text = "Clear", Location = new Point(140, 440), Size = new Size(80, 35) };
            btnClearPatient.BackColor = Color.FromArgb(108, 117, 125);
            btnClearPatient.ForeColor = Color.White;
            btnClearPatient.FlatStyle = FlatStyle.Flat;
            btnClearPatient.Click += (s, e) => {
                ClearPatientForm(personalInfoGroup, medicalInfoGroup);
                ResetPatientFormMode(btnSavePatient);
            };

            Button btnDeletePatient = new Button() { Text = "Delete Patient", Location = new Point(230, 440), Size = new Size(120, 35) };
            btnDeletePatient.BackColor = Color.FromArgb(220, 53, 69);
            btnDeletePatient.ForeColor = Color.White;
            btnDeletePatient.FlatStyle = FlatStyle.Flat;

            // Search
            Label lblSearchPatient = new Label() { Text = "Search:", Location = new Point(10, 490), Size = new Size(60, 25) };
            TextBox txtSearchPatient = new TextBox() { Name = "txtSearchPatient", Location = new Point(75, 490), Size = new Size(300, 25) };

            // Patient List
            DataGridView dgvPatients = new DataGridView() { Name = "dgvPatients", Location = new Point(10, 520), Size = new Size(800, 220), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            dgvPatients.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvPatients.ReadOnly = true;
            dgvPatients.AllowUserToAddRows = false;
            dgvPatients.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            LoadPatients(dgvPatients);

            txtSearchPatient.TextChanged += (s, e) => LoadPatients(dgvPatients, txtSearchPatient.Text);
            btnDeletePatient.Click += (s, e) => {
                DeletePatient(dgvPatients);
                ClearPatientForm(personalInfoGroup, medicalInfoGroup);
                ResetPatientFormMode(btnSavePatient);
            };
            dgvPatients.CellClick += (s, e) => {
                if (e.RowIndex < 0) return;
                object idValue = dgvPatients.Rows[e.RowIndex].Cells["PatientID"].Value;
                if (idValue == null || idValue == DBNull.Value) return;
                LoadPatientIntoForm(Convert.ToInt32(idValue), personalInfoGroup, medicalInfoGroup, btnSavePatient);
            };

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
            appointmentGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Patient Selection
            Label lblPatient = new Label() { Text = "Select Patient:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbPatient = new ComboBox() { Name = "cmbPatient", Location = new Point(130, 30), Size = new Size(250, 25) };
            cmbPatient.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadPatientsInComboBox(cmbPatient);

            // Doctor Name
            Label lblDoctor = new Label() { Text = "Doctor Name:", Location = new Point(400, 30), Size = new Size(100, 25) };
            ComboBox cmbDoctor = new ComboBox() { Name = "cmbDoctor", Location = new Point(510, 30), Size = new Size(160, 25) };
            cmbDoctor.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadDoctorsInComboBox(cmbDoctor);
            Button btnAddDoctor = new Button() { Text = "+", Location = new Point(675, 30), Size = new Size(30, 25) };
            btnAddDoctor.FlatStyle = FlatStyle.Flat;
            btnAddDoctor.Click += (s, e) => { ShowManageDoctors(s, e); LoadDoctorsInComboBox(cmbDoctor); };

            // Department
            Label lblDepartment = new Label() { Text = "Department:", Location = new Point(20, 70), Size = new Size(100, 25) };
            ComboBox cmbDepartment = new ComboBox() { Name = "cmbDepartment", Location = new Point(130, 70), Size = new Size(200, 25) };
            cmbDepartment.Items.AddRange(new string[] { "General Medicine", "Cardiology", "Orthopedics", "Pediatrics", "Gynecology", "Dermatology", "ENT", "Ophthalmology" });
            cmbDepartment.DropDownStyle = ComboBoxStyle.DropDownList;

            // Appointment Date
            Label lblAppDate = new Label() { Text = "Date:", Location = new Point(350, 70), Size = new Size(100, 25) };
            DateTimePicker dtpAppDate = new DateTimePicker() { Name = "dtpAppDate", Location = new Point(460, 70), Size = new Size(150, 25) };
            dtpAppDate.MinDate = DateTime.Today;
            dtpAppDate.Format = DateTimePickerFormat.Custom;
            dtpAppDate.CustomFormat = "ddd, MMM dd, yyyy";

            // Appointment Time
            Label lblAppTime = new Label() { Text = "Time:", Location = new Point(630, 70), Size = new Size(50, 25) };
            ComboBox cmbAppTime = new ComboBox() { Name = "cmbAppTime", Location = new Point(680, 70), Size = new Size(100, 25) };
            cmbAppTime.Items.AddRange(new string[] { "09:00", "09:30", "10:00", "10:30", "11:00", "11:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30" });
            cmbAppTime.DropDownStyle = ComboBoxStyle.DropDownList;

            // Notes
            Label lblNotes = new Label() { Text = "Notes:", Location = new Point(20, 110), Size = new Size(100, 25) };
            TextBox txtNotes = new TextBox() { Name = "txtNotes", Location = new Point(130, 110), Size = new Size(550, 50), Multiline = true };

            appointmentGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblDoctor, cmbDoctor, btnAddDoctor,
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
            DataGridView dgvAppointments = new DataGridView() { Name = "dgvAppointments", Location = new Point(10, 300), Size = new Size(800, 270), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
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
            prescriptionGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Patient Selection
            Label lblPatient = new Label() { Text = "Select Patient:", Location = new Point(20, 30), Size = new Size(100, 25) };
            ComboBox cmbPatient = new ComboBox() { Name = "cmbPatient", Location = new Point(130, 30), Size = new Size(250, 25) };
            cmbPatient.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadPatientsInComboBox(cmbPatient);

            // Doctor Name
            Label lblDoctor = new Label() { Text = "Doctor Name:", Location = new Point(400, 30), Size = new Size(100, 25) };
            ComboBox cmbDoctor = new ComboBox() { Name = "cmbDoctor", Location = new Point(510, 30), Size = new Size(160, 25) };
            cmbDoctor.DropDownStyle = ComboBoxStyle.DropDownList;
            LoadDoctorsInComboBox(cmbDoctor);
            Button btnAddDoctor = new Button() { Text = "+", Location = new Point(675, 30), Size = new Size(30, 25) };
            btnAddDoctor.FlatStyle = FlatStyle.Flat;
            btnAddDoctor.Click += (s, e) => { ShowManageDoctors(s, e); LoadDoctorsInComboBox(cmbDoctor); };

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
            dtpFollowUp.Format = DateTimePickerFormat.Custom;
            dtpFollowUp.CustomFormat = "ddd, MMM dd, yyyy";

            prescriptionGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblDoctor, cmbDoctor, btnAddDoctor,
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
            DataGridView dgvPrescriptions = new DataGridView() { Name = "dgvPrescriptions", Location = new Point(10, 400), Size = new Size(800, 220), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
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
            billingGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

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
            cmbPaymentMethod.Items.AddRange(new string[] { "Cash", "Credit Card", "Debit Card", "UPI", "Insurance", "Online Transfer" });
            cmbPaymentMethod.DropDownStyle = ComboBoxStyle.DropDownList;

            // Due Date
            Label lblDueDate = new Label() { Text = "Due Date:", Location = new Point(600, 70), Size = new Size(70, 25) };
            DateTimePicker dtpDueDate = new DateTimePicker() { Name = "dtpDueDate", Location = new Point(680, 70), Size = new Size(100, 25) };
            dtpDueDate.MinDate = DateTime.Today;
            dtpDueDate.Format = DateTimePickerFormat.Custom;
            dtpDueDate.CustomFormat = "dd-MMM-yy";

            // Status
            Label lblStatus = new Label() { Text = "Status:", Location = new Point(20, 110), Size = new Size(100, 25) };
            ComboBox cmbStatus = new ComboBox() { Name = "cmbStatus", Location = new Point(130, 110), Size = new Size(150, 25) };
            cmbStatus.Items.AddRange(new string[] { "Pending", "Paid", "Partial", "Overdue" });
            cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStatus.SelectedIndex = 0;

            // Amount Paid (only editable when Status = Partial)
            Label lblAmountPaid = new Label() { Text = "Amount Paid:", Location = new Point(300, 110), Size = new Size(100, 25) };
            NumericUpDown numAmountPaid = new NumericUpDown() { Name = "numAmountPaid", Location = new Point(430, 110), Size = new Size(150, 25), Enabled = false };
            numAmountPaid.DecimalPlaces = 2;
            numAmountPaid.Maximum = 999999;
            numAmountPaid.Minimum = 0;

            cmbStatus.SelectedIndexChanged += (s, e) => {
                if (cmbStatus.Text == "Paid")
                {
                    numAmountPaid.Value = numAmount.Value;
                    numAmountPaid.Enabled = false;
                }
                else if (cmbStatus.Text == "Partial")
                {
                    numAmountPaid.Enabled = true;
                }
                else
                {
                    numAmountPaid.Value = 0;
                    numAmountPaid.Enabled = false;
                }
            };
            numAmount.ValueChanged += (s, e) => {
                if (cmbStatus.Text == "Paid")
                    numAmountPaid.Value = numAmount.Value;
            };

            billingGroup.Controls.AddRange(new Control[] {
                lblPatient, cmbPatient, lblService, txtService,
                lblAmount, numAmount, lblPaymentMethod, cmbPaymentMethod,
                lblDueDate, dtpDueDate, lblStatus, cmbStatus,
                lblAmountPaid, numAmountPaid
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

            Button btnPrintInvoice = new Button() { Text = "Print Invoice", Location = new Point(330, 220), Size = new Size(110, 35) };
            btnPrintInvoice.BackColor = Color.FromArgb(108, 117, 125);
            btnPrintInvoice.ForeColor = Color.White;
            btnPrintInvoice.FlatStyle = FlatStyle.Flat;

            // Search
            Label lblSearchBill = new Label() { Text = "Search:", Location = new Point(10, 270), Size = new Size(60, 25) };
            TextBox txtSearchBill = new TextBox() { Name = "txtSearchBill", Location = new Point(75, 270), Size = new Size(300, 25) };

            // Bills List
            DataGridView dgvBills = new DataGridView() { Name = "dgvBills", Location = new Point(10, 300), Size = new Size(800, 270), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            dgvBills.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvBills.ReadOnly = true;
            dgvBills.AllowUserToAddRows = false;
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
            btnPrintInvoice.Click += (s, e) => PrintInvoice(dgvBills);

            mainPanel.Controls.AddRange(new Control[] {
                billingGroup, btnSaveBill, btnMarkPaid, btnDeleteBill, btnPrintInvoice,
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
            dateRangeGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label lblFromDate = new Label() { Text = "From Date:", Location = new Point(20, 30), Size = new Size(80, 25) };
            DateTimePicker dtpFromDate = new DateTimePicker() { Name = "dtpFromDate", Location = new Point(100, 30), Size = new Size(150, 25) };
            dtpFromDate.Format = DateTimePickerFormat.Custom;
            dtpFromDate.CustomFormat = "ddd, MMM dd, yyyy";
            dtpFromDate.Value = DateTime.Today.AddDays(-7);

            Label lblToDate = new Label() { Text = "To Date:", Location = new Point(270, 30), Size = new Size(60, 25) };
            DateTimePicker dtpToDate = new DateTimePicker() { Name = "dtpToDate", Location = new Point(340, 30), Size = new Size(150, 25) };
            dtpToDate.Format = DateTimePickerFormat.Custom;
            dtpToDate.CustomFormat = "ddd, MMM dd, yyyy";
            dtpToDate.Value = DateTime.Today;

            Button btnGenerateReport = new Button() { Text = "Generate Report", Location = new Point(510, 25), Size = new Size(130, 35) };
            btnGenerateReport.BackColor = Color.FromArgb(111, 66, 193);
            btnGenerateReport.ForeColor = Color.White;
            btnGenerateReport.FlatStyle = FlatStyle.Flat;

            dateRangeGroup.Controls.AddRange(new Control[] { lblFromDate, dtpFromDate, lblToDate, dtpToDate, btnGenerateReport });

            // Reports DataGridView
            DataGridView dgvReports = new DataGridView() { Name = "dgvReports", Location = new Point(10, 210), Size = new Size(800, 350), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
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
        private void SavePatient(GroupBox personalInfo, GroupBox medicalInfo, Button btnSavePatient)
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

                bool isEdit = btnSavePatient.Tag is int;
                int editingPatientId = isEdit ? (int)btnSavePatient.Tag : 0;

                connection.Open();
                string query = isEdit
                    ? @"UPDATE Patients SET
                        FirstName=@fname, LastName=@lname, DateOfBirth=@dob, Gender=@gender,
                        PhoneNumber=@phone, Email=@email, Address=@address, EmergencyContact=@emergency,
                        BloodGroup=@bloodgroup, MedicalHistory=@medical
                        WHERE PatientID=@id"
                    : @"INSERT INTO Patients
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
                    if (isEdit)
                        cmd.Parameters.AddWithValue("@id", editingPatientId);

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit(isEdit ? "Update Patient" : "Register Patient", $"{txtFirstName.Text.Trim()} {txtLastName.Text.Trim()}");

                MessageBox.Show(isEdit ? "Patient updated successfully!" : "Patient registered successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearPatientForm(personalInfo, medicalInfo);
                ResetPatientFormMode(btnSavePatient);

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

        private void LoadPatientIntoForm(int patientId, GroupBox personalInfo, GroupBox medicalInfo, Button btnSavePatient)
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM Patients WHERE PatientID = @id", connection))
                {
                    cmd.Parameters.AddWithValue("@id", patientId);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            (personalInfo.Controls["txtFirstName"] as TextBox).Text = reader["FirstName"].ToString();
                            (personalInfo.Controls["txtLastName"] as TextBox).Text = reader["LastName"].ToString();
                            (personalInfo.Controls["dtpDOB"] as DateTimePicker).Value = Convert.ToDateTime(reader["DateOfBirth"]);
                            (personalInfo.Controls["cmbGender"] as ComboBox).Text = reader["Gender"].ToString();
                            (personalInfo.Controls["txtPhone"] as TextBox).Text = reader["PhoneNumber"].ToString();
                            (personalInfo.Controls["txtEmail"] as TextBox).Text = reader["Email"] == DBNull.Value ? "" : reader["Email"].ToString();
                            (personalInfo.Controls["txtAddress"] as TextBox).Text = reader["Address"].ToString();
                            (medicalInfo.Controls["cmbBloodGroup"] as ComboBox).Text = reader["BloodGroup"] == DBNull.Value ? "" : reader["BloodGroup"].ToString();
                            (medicalInfo.Controls["txtEmergency"] as TextBox).Text = reader["EmergencyContact"] == DBNull.Value ? "" : reader["EmergencyContact"].ToString();
                            (medicalInfo.Controls["txtMedicalHistory"] as TextBox).Text = reader["MedicalHistory"] == DBNull.Value ? "" : reader["MedicalHistory"].ToString();
                        }
                    }
                }
                connection.Close();

                btnSavePatient.Tag = patientId;
                btnSavePatient.Text = "Update Patient";
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading patient: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetPatientFormMode(Button btnSavePatient)
        {
            btnSavePatient.Tag = null;
            btnSavePatient.Text = "Save Patient";
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
                    // Items.Clear() throws once DataSource is set from a previous
                    // call (e.g. on refresh after saving a patient); replacing
                    // DataSource directly is the correct way to rebind.
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
                ComboBox cmbDoctor = appointmentGroup.Controls["cmbDoctor"] as ComboBox;
                ComboBox cmbDepartment = appointmentGroup.Controls["cmbDepartment"] as ComboBox;
                DateTimePicker dtpAppDate = appointmentGroup.Controls["dtpAppDate"] as DateTimePicker;
                ComboBox cmbAppTime = appointmentGroup.Controls["cmbAppTime"] as ComboBox;
                TextBox txtNotes = appointmentGroup.Controls["txtNotes"] as TextBox;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(cmbDoctor.Text) ||
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
                    conflictCmd.Parameters.AddWithValue("@doctor", cmbDoctor.Text.Trim());
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
                    cmd.Parameters.AddWithValue("@doctor", cmbDoctor.Text.Trim());
                    cmd.Parameters.AddWithValue("@date", dtpAppDate.Value.Date);
                    cmd.Parameters.AddWithValue("@time", cmbAppTime.Text);
                    cmd.Parameters.AddWithValue("@dept", cmbDepartment.Text);
                    cmd.Parameters.AddWithValue("@notes", txtNotes.Text.Trim());

                    cmd.ExecuteNonQuery();
                }
                connection.Close();

                LogAudit("Schedule Appointment", $"Doctor {cmbDoctor.Text.Trim()} on {dtpAppDate.Value.Date:d} {cmbAppTime.Text}");

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
                ComboBox cmbDoctor = prescriptionGroup.Controls["cmbDoctor"] as ComboBox;
                TextBox txtDiagnosis = prescriptionGroup.Controls["txtDiagnosis"] as TextBox;
                TextBox txtMedicines = prescriptionGroup.Controls["txtMedicines"] as TextBox;
                TextBox txtInstructions = prescriptionGroup.Controls["txtInstructions"] as TextBox;
                DateTimePicker dtpFollowUp = prescriptionGroup.Controls["dtpFollowUp"] as DateTimePicker;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(cmbDoctor.Text) ||
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
                    cmd.Parameters.AddWithValue("@doctor", cmbDoctor.Text.Trim());
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
            ComboBox cmbDoctor = prescriptionGroup.Controls["cmbDoctor"] as ComboBox;
            TextBox txtDiagnosis = prescriptionGroup.Controls["txtDiagnosis"] as TextBox;
            TextBox txtMedicines = prescriptionGroup.Controls["txtMedicines"] as TextBox;
            TextBox txtInstructions = prescriptionGroup.Controls["txtInstructions"] as TextBox;
            DateTimePicker dtpFollowUp = prescriptionGroup.Controls["dtpFollowUp"] as DateTimePicker;

            if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(cmbDoctor.Text) ||
                string.IsNullOrWhiteSpace(txtDiagnosis.Text) || string.IsNullOrWhiteSpace(txtMedicines.Text))
            {
                MessageBox.Show("Please fill in the prescription fields before printing.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string patientName = ((DataRowView)cmbPatient.SelectedItem)["DisplayName"].ToString();
            string doctorName = cmbDoctor.Text;
            string diagnosis = txtDiagnosis.Text;
            string medicines = txtMedicines.Text;
            string instructions = txtInstructions.Text;
            DateTime followUpDate = dtpFollowUp.Value.Date;

            try
            {
                PrintDocument printDoc = new PrintDocument();
                Font titleFont = new Font("Segoe UI", 14F, FontStyle.Bold);
                Font bodyFont = new Font("Segoe UI", 11F);

                printDoc.PrintPage += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    Rectangle bounds = e.MarginBounds;
                    int y = DrawPrintHeader(g, bounds);

                    g.DrawString("PRESCRIPTION", titleFont, Brushes.Black, bounds.Left, y);
                    y += 32;
                    g.DrawString($"Date: {DateTime.Today:d}", bodyFont, Brushes.Black, bounds.Left, y);
                    y += 28;

                    y = DrawPrintSection(g, bounds, bodyFont, y, $"Patient: {patientName}\nDoctor: {doctorName}");

                    string clinicalBlock = $"Diagnosis: {diagnosis}\n\nMedicines:\n{medicines}";
                    if (!string.IsNullOrWhiteSpace(instructions))
                        clinicalBlock += $"\n\nInstructions: {instructions}";
                    y = DrawPrintSection(g, bounds, bodyFont, y, clinicalBlock);

                    DrawPrintSection(g, bounds, bodyFont, y, $"Follow-up Date: {followUpDate:d}");

                    DrawPrintFooter(g, bounds);
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

        // Draws one block of text with a divider line above it, and returns the
        // Y position for the next block. Gives printed documents clear visual
        // breaks between entries instead of one dense wall of text.
        private int DrawPrintSection(Graphics g, Rectangle bounds, Font font, int y, string text)
        {
            g.DrawLine(Pens.LightGray, bounds.Left, y, bounds.Right, y);
            y += 12;

            SizeF size = g.MeasureString(text, font, bounds.Width);
            g.DrawString(text, font, Brushes.Black, new RectangleF(bounds.Left, y, bounds.Width, size.Height));
            return y + (int)size.Height + 12;
        }

        // Draws a small "Generated by <app>" credit line at the bottom of a
        // printed page, distinct from the hospital's own letterhead at the top.
        private void DrawPrintFooter(Graphics g, Rectangle bounds)
        {
            using (Font footerFont = new Font("Segoe UI", 8F, FontStyle.Italic))
            {
                string footerText = "Generated by Patient Management System";
                SizeF size = g.MeasureString(footerText, footerFont);
                float x = bounds.Left + (bounds.Width - size.Width) / 2;
                float y = bounds.Bottom - size.Height;
                g.DrawLine(Pens.LightGray, bounds.Left, y - 5, bounds.Right, y - 5);
                g.DrawString(footerText, footerFont, Brushes.Gray, x, y);
            }
        }

        // Draws the hospital logo, name, and contact details at the top of a
        // printed page, returning the Y position where the body content
        // should start. Shared by prescription and invoice printing.
        private int DrawPrintHeader(Graphics g, Rectangle bounds)
        {
            string name = "Patient Management System", address = "", phone = "";
            byte[] logoBytes = null;

            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "SELECT HospitalName, Address, Phone, Logo FROM HospitalProfile WHERE ProfileID = 1", connection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        name = reader["HospitalName"].ToString();
                        address = reader["Address"] == DBNull.Value ? "" : reader["Address"].ToString();
                        phone = reader["Phone"] == DBNull.Value ? "" : reader["Phone"].ToString();
                        if (reader["Logo"] != DBNull.Value) logoBytes = (byte[])reader["Logo"];
                    }
                }
                connection.Close();
            }
            catch
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
            }

            const int logoSize = 70;
            int y = bounds.Top;

            if (logoBytes != null)
            {
                using (MemoryStream ms = new MemoryStream(logoBytes))
                using (Image logoImage = Image.FromStream(ms))
                {
                    g.DrawImage(logoImage, bounds.Left, y, logoSize, logoSize);
                }
            }

            int textX = bounds.Left + logoSize + 15;
            using (Font nameFont = new Font("Segoe UI", 16F, FontStyle.Bold))
            {
                g.DrawString(name, nameFont, Brushes.Black, textX, y);
            }

            string details = string.Join("   |   ", new[] { address, phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(details))
            {
                using (Font detailFont = new Font("Segoe UI", 9F))
                {
                    g.DrawString(details, detailFont, Brushes.Black, textX, y + 30);
                }
            }

            int headerBottom = y + logoSize + 10;
            g.DrawLine(Pens.Black, bounds.Left, headerBottom, bounds.Right, headerBottom);
            return headerBottom + 15;
        }

        private void PrintInvoice(DataGridView dgvBills)
        {
            if (dgvBills.SelectedRows.Count == 0)
            {
                MessageBox.Show("Select a bill from the list first.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DataGridViewRow row = dgvBills.SelectedRows[0];
            string patientName = row.Cells["PatientName"].Value?.ToString();
            string service = row.Cells["ServiceDescription"].Value?.ToString();
            decimal totalAmount = Convert.ToDecimal(row.Cells["TotalAmount"].Value);
            decimal amountPaid = Convert.ToDecimal(row.Cells["AmountPaid"].Value);
            decimal balance = Convert.ToDecimal(row.Cells["Balance"].Value);
            string status = row.Cells["PaymentStatus"].Value?.ToString();
            string method = row.Cells["PaymentMethod"].Value?.ToString();
            DateTime billDate = Convert.ToDateTime(row.Cells["BillDate"].Value);
            object dueDateValue = row.Cells["DueDate"].Value;

            string dateLine = $"Bill Date: {billDate:d}";
            if (dueDateValue != null && dueDateValue != DBNull.Value)
                dateLine += $"\nDue Date: {Convert.ToDateTime(dueDateValue):d}";

            string patientBlock = $"Patient: {patientName}\nService: {service}";
            string amountBlock = $"Total Amount: {totalAmount:C2}\nAmount Paid: {amountPaid:C2}\nBalance Due: {balance:C2}";
            string statusBlock = $"Payment Status: {status}";
            if (!string.IsNullOrWhiteSpace(method))
                statusBlock += $"\nPayment Method: {method}";

            try
            {
                PrintDocument printDoc = new PrintDocument();
                Font titleFont = new Font("Segoe UI", 14F, FontStyle.Bold);
                Font bodyFont = new Font("Segoe UI", 11F);

                printDoc.PrintPage += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    Rectangle bounds = e.MarginBounds;
                    int y = DrawPrintHeader(g, bounds);

                    g.DrawString("INVOICE", titleFont, Brushes.Black, bounds.Left, y);
                    y += 32;
                    SizeF dateSize = g.MeasureString(dateLine, bodyFont, bounds.Width);
                    g.DrawString(dateLine, bodyFont, Brushes.Black, new RectangleF(bounds.Left, y, bounds.Width, dateSize.Height));
                    y += (int)dateSize.Height + 12;

                    y = DrawPrintSection(g, bounds, bodyFont, y, patientBlock);
                    y = DrawPrintSection(g, bounds, bodyFont, y, amountBlock);
                    DrawPrintSection(g, bounds, bodyFont, y, statusBlock);

                    DrawPrintFooter(g, bounds);
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
                MessageBox.Show($"Error printing invoice: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                NumericUpDown numAmountPaid = billingGroup.Controls["numAmountPaid"] as NumericUpDown;

                if (cmbPatient.SelectedIndex == -1 || string.IsNullOrWhiteSpace(txtService.Text) || numAmount.Value <= 0 || cmbStatus.SelectedIndex == -1)
                {
                    MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dtpDueDate.Value.Date < DateTime.Today)
                {
                    MessageBox.Show("Due date cannot be in the past.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                decimal amountPaid;
                if (cmbStatus.Text == "Paid")
                    amountPaid = numAmount.Value;
                else if (cmbStatus.Text == "Partial")
                {
                    if (numAmountPaid.Value <= 0 || numAmountPaid.Value >= numAmount.Value)
                    {
                        MessageBox.Show("Amount paid must be greater than 0 and less than the total amount for a partial payment.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    amountPaid = numAmountPaid.Value;
                }
                else
                    amountPaid = 0;

                connection.Open();
                string query = @"INSERT INTO Billing
                    (PatientID, ServiceDescription, Amount, AmountPaid, PaymentStatus, PaymentMethod, BillDate, DueDate)
                    VALUES (@patientid, @service, @amount, @amountpaid, @status, @method, @billdate, @duedate)";

                using (SQLiteCommand cmd = new SQLiteCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@patientid", ((DataRowView)cmbPatient.SelectedItem)["PatientID"]);
                    cmd.Parameters.AddWithValue("@service", txtService.Text.Trim());
                    cmd.Parameters.AddWithValue("@amount", numAmount.Value);
                    cmd.Parameters.AddWithValue("@amountpaid", amountPaid);
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
                    b.ServiceDescription, b.Amount as TotalAmount, b.AmountPaid, (b.Amount - b.AmountPaid) as Balance,
                    b.PaymentStatus, b.PaymentMethod, b.BillDate, b.DueDate
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
                string query = status == "Paid"
                    ? "UPDATE Billing SET PaymentStatus = @status, AmountPaid = Amount WHERE BillID = @id"
                    : "UPDATE Billing SET PaymentStatus = @status WHERE BillID = @id";

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
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM Appointments WHERE DATE(AppointmentDate) = @today", connection))
                {
                    cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));
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
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COALESCE(SUM(AmountPaid), 0) FROM Billing WHERE DATE(BillDate) = @today", connection))
                {
                    cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("yyyy-MM-dd"));
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
                    SUM(b.AmountPaid) as PaidAmount,
                    SUM(b.Amount - b.AmountPaid) as PendingAmount,
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

        private void ImportDatabase(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                "Importing a database will replace ALL current data (patients, appointments, bills, etc.) " +
                "with the contents of the selected file. Your current database will be backed up first, but this cannot be undone from within the app. Continue?",
                "Import Database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            using (OpenFileDialog openDialog = new OpenFileDialog())
            {
                openDialog.Filter = "Database files (*.db)|*.db|All files (*.*)|*.*";
                if (openDialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    string dbPath = Path.Combine(Application.StartupPath, "PatientManagement.db");
                    string backupPath = Path.Combine(Application.StartupPath, $"PatientManagement_PreImport_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                    connection.Close();
                    SQLiteConnection.ClearAllPools();

                    File.Copy(dbPath, backupPath, true);
                    File.Copy(openDialog.FileName, dbPath, true);

                    // Bring the imported file up to the current schema (adds any
                    // columns/tables this version expects that the import predates).
                    DbInit.EnsureSchema(connectionString);

                    connection = new SQLiteConnection(connectionString);

                    RefreshAllGrids();
                    RefreshPatientComboBoxes();
                    RefreshDoctorComboBoxes();
                    RefreshHomeProfile();

                    LogAudit("Import Database", openDialog.FileName);
                    MessageBox.Show($"Database imported successfully.\n\nYour previous database was backed up to:\n{backupPath}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing database: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
            string name = "", motto = "", address = "", phone = "";
            byte[] logoBytes = null;

            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "SELECT HospitalName, Motto, Address, Phone, Logo FROM HospitalProfile WHERE ProfileID = 1", connection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        name = reader["HospitalName"].ToString();
                        motto = reader["Motto"] == DBNull.Value ? "" : reader["Motto"].ToString();
                        address = reader["Address"] == DBNull.Value ? "" : reader["Address"].ToString();
                        phone = reader["Phone"] == DBNull.Value ? "" : reader["Phone"].ToString();
                        if (reader["Logo"] != DBNull.Value) logoBytes = (byte[])reader["Logo"];
                    }
                }
                connection.Close();
            }
            catch (Exception ex)
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                MessageBox.Show($"Error loading hospital profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (Form settingsForm = new Form())
            {
                settingsForm.Text = "Hospital Profile";
                settingsForm.Size = new Size(480, 430);
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                settingsForm.MaximizeBox = false;
                settingsForm.MinimizeBox = false;

                Label lblName = new Label() { Text = "Hospital Name:", Location = new Point(20, 20), Size = new Size(120, 25) };
                TextBox txtName = new TextBox() { Location = new Point(150, 20), Size = new Size(290, 25), Text = name };

                Label lblMotto = new Label() { Text = "Motto:", Location = new Point(20, 55), Size = new Size(120, 25) };
                TextBox txtMotto = new TextBox() { Location = new Point(150, 55), Size = new Size(290, 25), Text = motto };

                Label lblAddress = new Label() { Text = "Address:", Location = new Point(20, 90), Size = new Size(120, 45) };
                TextBox txtAddress = new TextBox() { Location = new Point(150, 90), Size = new Size(290, 45), Multiline = true, Text = address };

                Label lblPhone = new Label() { Text = "Phone:", Location = new Point(20, 145), Size = new Size(120, 25) };
                TextBox txtPhone = new TextBox() { Location = new Point(150, 145), Size = new Size(290, 25), Text = phone };

                Label lblLogo = new Label() { Text = "Logo:", Location = new Point(20, 180), Size = new Size(120, 100) };
                PictureBox picPreview = new PictureBox() {
                    Location = new Point(150, 180),
                    Size = new Size(100, 100),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.FixedSingle
                };
                if (logoBytes != null)
                    picPreview.Image = Image.FromStream(new MemoryStream(logoBytes));

                byte[] pendingLogoBytes = logoBytes;

                Button btnChooseLogo = new Button() { Text = "Choose Image...", Location = new Point(260, 210), Size = new Size(140, 30) };
                btnChooseLogo.BackColor = Color.FromArgb(108, 117, 125);
                btnChooseLogo.ForeColor = Color.White;
                btnChooseLogo.FlatStyle = FlatStyle.Flat;
                btnChooseLogo.Click += (s, e2) =>
                {
                    using (OpenFileDialog ofd = new OpenFileDialog())
                    {
                        ofd.Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                        if (ofd.ShowDialog() == DialogResult.OK)
                        {
                            pendingLogoBytes = File.ReadAllBytes(ofd.FileName);
                            Image oldImage = picPreview.Image;
                            picPreview.Image = Image.FromFile(ofd.FileName);
                            oldImage?.Dispose();
                        }
                    }
                };

                Button btnSave = new Button() { Text = "Save", Location = new Point(150, 340), Size = new Size(100, 35), DialogResult = DialogResult.OK };
                btnSave.BackColor = Color.FromArgb(0, 123, 255);
                btnSave.ForeColor = Color.White;
                btnSave.FlatStyle = FlatStyle.Flat;

                Button btnCancel = new Button() { Text = "Cancel", Location = new Point(260, 340), Size = new Size(100, 35), DialogResult = DialogResult.Cancel };

                settingsForm.Controls.AddRange(new Control[] {
                    lblName, txtName, lblMotto, txtMotto, lblAddress, txtAddress,
                    lblPhone, txtPhone, lblLogo, picPreview, btnChooseLogo, btnSave, btnCancel
                });
                settingsForm.AcceptButton = btnSave;
                settingsForm.CancelButton = btnCancel;

                if (settingsForm.ShowDialog(this) != DialogResult.OK)
                    return;

                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Hospital name is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    connection.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(
                        "UPDATE HospitalProfile SET HospitalName=@name, Motto=@motto, Address=@address, Phone=@phone, Logo=@logo WHERE ProfileID = 1",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@name", txtName.Text.Trim());
                        cmd.Parameters.AddWithValue("@motto", txtMotto.Text.Trim());
                        cmd.Parameters.AddWithValue("@address", txtAddress.Text.Trim());
                        cmd.Parameters.AddWithValue("@phone", txtPhone.Text.Trim());
                        cmd.Parameters.AddWithValue("@logo", (object)pendingLogoBytes ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                    connection.Close();

                    LogAudit("Update Hospital Profile", txtName.Text.Trim());
                    RefreshHomeProfile();
                    MessageBox.Show("Hospital profile updated.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    if (connection.State == ConnectionState.Open)
                        connection.Close();
                    MessageBox.Show($"Error updating hospital profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                previousWindowState = this.WindowState;
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                isFullScreen = true;
            }
            else
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = previousWindowState;
                isFullScreen = false;
            }
        }

        private void ShowManageUsers(object sender, EventArgs e)
        {
            using (Form usersForm = new Form())
            {
                usersForm.Text = "Manage Users";
                usersForm.Size = new Size(600, 480);
                usersForm.StartPosition = FormStartPosition.CenterParent;

                Label lblUsername = new Label() { Text = "Username:", Location = new Point(15, 15), Size = new Size(100, 25) };
                TextBox txtUsername = new TextBox() { Location = new Point(120, 15), Size = new Size(180, 25) };

                Label lblPassword = new Label() { Text = "Password:", Location = new Point(15, 50), Size = new Size(100, 25) };
                TextBox txtPassword = new TextBox() { Location = new Point(120, 50), Size = new Size(180, 25), UseSystemPasswordChar = true };

                Label lblRole = new Label() { Text = "Role:", Location = new Point(15, 85), Size = new Size(100, 25) };
                ComboBox cmbRole = new ComboBox() { Location = new Point(120, 85), Size = new Size(180, 25) };
                cmbRole.Items.AddRange(new string[] { "User", "Admin" });
                cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
                cmbRole.SelectedIndex = 0;

                Button btnAdd = new Button() { Text = "Add User", Location = new Point(320, 15), Size = new Size(120, 30) };
                btnAdd.BackColor = Color.FromArgb(40, 167, 69);
                btnAdd.ForeColor = Color.White;
                btnAdd.FlatStyle = FlatStyle.Flat;

                Button btnResetPassword = new Button() { Text = "Reset Password", Location = new Point(320, 50), Size = new Size(120, 30) };
                btnResetPassword.BackColor = Color.FromArgb(0, 123, 255);
                btnResetPassword.ForeColor = Color.White;
                btnResetPassword.FlatStyle = FlatStyle.Flat;

                Button btnSetRole = new Button() { Text = "Set Role", Location = new Point(320, 85), Size = new Size(120, 30) };
                btnSetRole.BackColor = Color.FromArgb(108, 117, 125);
                btnSetRole.ForeColor = Color.White;
                btnSetRole.FlatStyle = FlatStyle.Flat;

                Button btnDelete = new Button() { Text = "Delete Selected", Location = new Point(15, 125), Size = new Size(140, 30) };
                btnDelete.BackColor = Color.FromArgb(220, 53, 69);
                btnDelete.ForeColor = Color.White;
                btnDelete.FlatStyle = FlatStyle.Flat;

                DataGridView dgvUsers = new DataGridView() {
                    Location = new Point(15, 165),
                    Size = new Size(555, 260),
                    ReadOnly = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    AllowUserToAddRows = false
                };

                void LoadUsersGrid()
                {
                    try
                    {
                        connection.Open();
                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(
                            "SELECT UserID, Username, Role, CreatedDate FROM Users ORDER BY Username", connection))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            dgvUsers.DataSource = dt;
                        }
                        connection.Close();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error loading users: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                string SelectedUsername()
                {
                    if (dgvUsers.SelectedRows.Count == 0)
                        return null;
                    return dgvUsers.SelectedRows[0].Cells["Username"].Value?.ToString();
                }

                btnAdd.Click += (s, e2) =>
                {
                    if (string.IsNullOrWhiteSpace(txtUsername.Text) || string.IsNullOrWhiteSpace(txtPassword.Text))
                    {
                        MessageBox.Show("Username and password are required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (txtPassword.Text.Length < 8)
                    {
                        MessageBox.Show("Password must be at least 8 characters.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        string salt = Guid.NewGuid().ToString("N");
                        string hash = AuthHelper.HashPassword(txtPassword.Text, salt, AuthHelper.DefaultIterations);

                        connection.Open();
                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "INSERT INTO Users (Username, PasswordHash, Salt, Iterations, Role) VALUES (@u, @h, @s, @i, @r)", connection))
                        {
                            cmd.Parameters.AddWithValue("@u", txtUsername.Text.Trim());
                            cmd.Parameters.AddWithValue("@h", hash);
                            cmd.Parameters.AddWithValue("@s", salt);
                            cmd.Parameters.AddWithValue("@i", AuthHelper.DefaultIterations);
                            cmd.Parameters.AddWithValue("@r", cmbRole.Text);
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Add User", $"{txtUsername.Text.Trim()} ({cmbRole.Text})");
                        txtUsername.Clear();
                        txtPassword.Clear();
                        LoadUsersGrid();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error adding user: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                btnResetPassword.Click += (s, e2) =>
                {
                    string username = SelectedUsername();
                    if (username == null)
                    {
                        MessageBox.Show("Select a user from the list first.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(txtPassword.Text) || txtPassword.Text.Length < 8)
                    {
                        MessageBox.Show("Enter a new password (at least 8 characters) in the Password field first.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        string salt = Guid.NewGuid().ToString("N");
                        string hash = AuthHelper.HashPassword(txtPassword.Text, salt, AuthHelper.DefaultIterations);

                        connection.Open();
                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "UPDATE Users SET PasswordHash=@h, Salt=@s, Iterations=@i WHERE Username=@u", connection))
                        {
                            cmd.Parameters.AddWithValue("@h", hash);
                            cmd.Parameters.AddWithValue("@s", salt);
                            cmd.Parameters.AddWithValue("@i", AuthHelper.DefaultIterations);
                            cmd.Parameters.AddWithValue("@u", username);
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Reset Password", username);
                        txtPassword.Clear();
                        MessageBox.Show($"Password reset for {username}.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error resetting password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                btnSetRole.Click += (s, e2) =>
                {
                    string username = SelectedUsername();
                    if (username == null)
                    {
                        MessageBox.Show("Select a user from the list first.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        connection.Open();
                        if (cmbRole.Text != "Admin")
                        {
                            using (SQLiteCommand check = new SQLiteCommand(
                                "SELECT COUNT(*) FROM Users WHERE Role = 'Admin' AND Username != @u", connection))
                            {
                                check.Parameters.AddWithValue("@u", username);
                                long remainingAdmins = Convert.ToInt64(check.ExecuteScalar());
                                if (remainingAdmins == 0)
                                {
                                    connection.Close();
                                    MessageBox.Show("At least one Admin account must remain.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    return;
                                }
                            }
                        }

                        using (SQLiteCommand cmd = new SQLiteCommand("UPDATE Users SET Role=@r WHERE Username=@u", connection))
                        {
                            cmd.Parameters.AddWithValue("@r", cmbRole.Text);
                            cmd.Parameters.AddWithValue("@u", username);
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Change User Role", $"{username} -> {cmbRole.Text}");
                        LoadUsersGrid();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error changing role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                btnDelete.Click += (s, e2) =>
                {
                    string username = SelectedUsername();
                    if (username == null) return;

                    if (string.Equals(username, currentUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("You cannot delete the account you're currently logged in as.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (MessageBox.Show($"Delete user '{username}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    try
                    {
                        connection.Open();
                        using (SQLiteCommand check = new SQLiteCommand(
                            "SELECT COUNT(*) FROM Users WHERE Role = 'Admin' AND Username != @u", connection))
                        {
                            check.Parameters.AddWithValue("@u", username);
                            long remainingAdmins = Convert.ToInt64(check.ExecuteScalar());

                            using (SQLiteCommand roleCheck = new SQLiteCommand("SELECT Role FROM Users WHERE Username = @u", connection))
                            {
                                roleCheck.Parameters.AddWithValue("@u", username);
                                string targetRole = roleCheck.ExecuteScalar()?.ToString();
                                if (targetRole == "Admin" && remainingAdmins == 0)
                                {
                                    connection.Close();
                                    MessageBox.Show("At least one Admin account must remain.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    return;
                                }
                            }
                        }

                        using (SQLiteCommand cmd = new SQLiteCommand("DELETE FROM Users WHERE Username=@u", connection))
                        {
                            cmd.Parameters.AddWithValue("@u", username);
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Delete User", username);
                        LoadUsersGrid();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error deleting user: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                usersForm.Controls.AddRange(new Control[] {
                    lblUsername, txtUsername, lblPassword, txtPassword, lblRole, cmbRole,
                    btnAdd, btnResetPassword, btnSetRole, btnDelete, dgvUsers
                });

                LoadUsersGrid();
                usersForm.ShowDialog(this);
            }
        }

        private void ShowManageDoctors(object sender, EventArgs e)
        {
            using (Form doctorsForm = new Form())
            {
                doctorsForm.Text = "Manage Doctors";
                doctorsForm.Size = new Size(580, 480);
                doctorsForm.StartPosition = FormStartPosition.CenterParent;

                Label lblName = new Label() { Text = "Doctor Name:", Location = new Point(15, 15), Size = new Size(100, 25) };
                TextBox txtName = new TextBox() { Location = new Point(120, 15), Size = new Size(220, 25) };

                Label lblSpec = new Label() { Text = "Specialization:", Location = new Point(15, 50), Size = new Size(100, 25) };
                TextBox txtSpec = new TextBox() { Location = new Point(120, 50), Size = new Size(220, 25) };

                Button btnAdd = new Button() { Text = "Add", Location = new Point(360, 15), Size = new Size(90, 30) };
                btnAdd.BackColor = Color.FromArgb(40, 167, 69);
                btnAdd.ForeColor = Color.White;
                btnAdd.FlatStyle = FlatStyle.Flat;

                Button btnUpdate = new Button() { Text = "Update", Location = new Point(360, 50), Size = new Size(90, 30) };
                btnUpdate.BackColor = Color.FromArgb(0, 123, 255);
                btnUpdate.ForeColor = Color.White;
                btnUpdate.FlatStyle = FlatStyle.Flat;

                Button btnClear = new Button() { Text = "Clear", Location = new Point(15, 90), Size = new Size(90, 30) };
                btnClear.BackColor = Color.FromArgb(108, 117, 125);
                btnClear.ForeColor = Color.White;
                btnClear.FlatStyle = FlatStyle.Flat;

                Button btnDelete = new Button() { Text = "Delete Selected", Location = new Point(115, 90), Size = new Size(140, 30) };
                btnDelete.BackColor = Color.FromArgb(220, 53, 69);
                btnDelete.ForeColor = Color.White;
                btnDelete.FlatStyle = FlatStyle.Flat;

                DataGridView dgvDoctors = new DataGridView() {
                    Location = new Point(15, 130),
                    Size = new Size(535, 300),
                    ReadOnly = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    AllowUserToAddRows = false
                };

                void LoadDoctorsGrid()
                {
                    try
                    {
                        connection.Open();
                        using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(
                            "SELECT DoctorID, Name, Specialization FROM Doctors ORDER BY Name", connection))
                        {
                            DataTable dt = new DataTable();
                            adapter.Fill(dt);
                            dgvDoctors.DataSource = dt;
                        }
                        connection.Close();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error loading doctors: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                void ClearDoctorForm()
                {
                    txtName.Clear();
                    txtSpec.Clear();
                    txtName.Tag = null;
                }

                btnAdd.Click += (s, e2) =>
                {
                    if (string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        MessageBox.Show("Doctor name is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    try
                    {
                        connection.Open();
                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "INSERT INTO Doctors (Name, Specialization) VALUES (@n, @s)", connection))
                        {
                            cmd.Parameters.AddWithValue("@n", txtName.Text.Trim());
                            cmd.Parameters.AddWithValue("@s", txtSpec.Text.Trim());
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Add Doctor", txtName.Text.Trim());
                        ClearDoctorForm();
                        LoadDoctorsGrid();
                        RefreshDoctorComboBoxes();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error adding doctor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                dgvDoctors.CellClick += (s, e2) =>
                {
                    if (e2.RowIndex < 0) return;
                    object idVal = dgvDoctors.Rows[e2.RowIndex].Cells["DoctorID"].Value;
                    if (idVal == null || idVal == DBNull.Value) return;

                    txtName.Tag = Convert.ToInt32(idVal);
                    txtName.Text = dgvDoctors.Rows[e2.RowIndex].Cells["Name"].Value?.ToString();
                    txtSpec.Text = dgvDoctors.Rows[e2.RowIndex].Cells["Specialization"].Value?.ToString();
                };

                btnUpdate.Click += (s, e2) =>
                {
                    if (!(txtName.Tag is int doctorId))
                    {
                        MessageBox.Show("Select a doctor from the list first.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        MessageBox.Show("Doctor name is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    try
                    {
                        connection.Open();
                        using (SQLiteCommand cmd = new SQLiteCommand(
                            "UPDATE Doctors SET Name=@n, Specialization=@s WHERE DoctorID=@id", connection))
                        {
                            cmd.Parameters.AddWithValue("@n", txtName.Text.Trim());
                            cmd.Parameters.AddWithValue("@s", txtSpec.Text.Trim());
                            cmd.Parameters.AddWithValue("@id", doctorId);
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Update Doctor", txtName.Text.Trim());
                        ClearDoctorForm();
                        LoadDoctorsGrid();
                        RefreshDoctorComboBoxes();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error updating doctor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                btnClear.Click += (s, e2) => ClearDoctorForm();

                btnDelete.Click += (s, e2) =>
                {
                    if (dgvDoctors.SelectedRows.Count == 0) return;
                    object idVal = dgvDoctors.SelectedRows[0].Cells["DoctorID"].Value;
                    if (idVal == null || idVal == DBNull.Value) return;

                    if (MessageBox.Show("Delete the selected doctor?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;

                    try
                    {
                        connection.Open();
                        using (SQLiteCommand cmd = new SQLiteCommand("DELETE FROM Doctors WHERE DoctorID=@id", connection))
                        {
                            cmd.Parameters.AddWithValue("@id", Convert.ToInt32(idVal));
                            cmd.ExecuteNonQuery();
                        }
                        connection.Close();

                        LogAudit("Delete Doctor", idVal.ToString());
                        ClearDoctorForm();
                        LoadDoctorsGrid();
                        RefreshDoctorComboBoxes();
                    }
                    catch (Exception ex)
                    {
                        if (connection.State == ConnectionState.Open)
                            connection.Close();
                        MessageBox.Show($"Error deleting doctor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                doctorsForm.Controls.AddRange(new Control[] {
                    lblName, txtName, lblSpec, txtSpec, btnAdd, btnUpdate, btnClear, btnDelete, dgvDoctors
                });

                LoadDoctorsGrid();
                doctorsForm.ShowDialog(this);
            }
        }

        private void LoadDoctorsInComboBox(ComboBox cmb)
        {
            try
            {
                connection.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT DoctorID, Name FROM Doctors ORDER BY Name", connection))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    cmb.DisplayMember = "Name";
                    cmb.ValueMember = "DoctorID";

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
                MessageBox.Show($"Error loading doctors: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshDoctorComboBoxes()
        {
            foreach (Control control in FindControlsRecursive(mainTabControl, c => c is ComboBox cmb && cmb.Name == "cmbDoctor"))
            {
                LoadDoctorsInComboBox((ComboBox)control);
            }
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            string aboutText = @"Patient Management System v1.0

A comprehensive healthcare management solution
Features: Patient Registration, Appointments, Prescriptions, Billing & Reports

Developed by Abhinav Kumar

© 2025 - Healthcare Solutions";

            MessageBox.Show(aboutText, "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                homeClockTimer?.Stop();
                homeClockTimer?.Dispose();
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
            string authenticatedRole;
            using (LoginForm loginForm = new LoginForm(connectionString))
            {
                if (loginForm.ShowDialog() != DialogResult.OK)
                    return;

                authenticatedUsername = loginForm.AuthenticatedUsername;
                authenticatedRole = loginForm.AuthenticatedRole;
            }

            Application.Run(new MainForm(authenticatedUsername, authenticatedRole));
        }
    }
}
