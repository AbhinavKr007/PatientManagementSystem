# Patient Management System

## 🏥 Complete Healthcare Management Solution

A comprehensive, professional-grade Patient Management System designed for clinics, hospitals, and healthcare facilities. This standalone Windows application provides complete patient lifecycle management with an integrated SQLite database requiring no additional software installation.

## 🚀 Key Features

### 📋 **Patient Registration & Management**
- Complete patient demographics and medical history
- Emergency contact information
- Blood group and medical history tracking
- Patient search and filtering capabilities
- Secure patient data storage

### 📅 **Appointment Scheduling**
- Full calendar management system
- Multi-doctor scheduling support
- Department-wise appointment categorization
- Appointment status tracking (Scheduled, Completed, Cancelled)
- Time slot management with conflict prevention
- Appointment notes and special instructions

### 💊 **Prescription Management**
- Digital prescription creation and management
- Diagnosis recording and tracking
- Medicine dosage and instruction management
- Follow-up appointment scheduling
- Prescription history tracking
- Print-ready prescription formats

### 💰 **Billing & Payment System**
- Comprehensive billing management
- Multiple payment method support (Cash, Card, Insurance, Online)
- Payment status tracking (Pending, Paid, Partial, Overdue)
- Due date management and reminders
- Service-based billing with custom descriptions
- Patient payment history

### 📊 **Reports & Analytics**
- Day-wise sales summary and revenue tracking
- Patient registration statistics
- Appointment analytics and trends
- Payment collection reports
- Custom date range reporting
- CSV export functionality for external analysis
- Real-time dashboard with key metrics

## 💻 System Requirements

### **Minimum Requirements:**
- **Operating System**: Windows 7 or later (Windows 10/11 recommended)
- **Architecture**: x64 (64-bit)
- **RAM**: 2 GB minimum (4 GB recommended)
- **Storage**: 500 MB free space
- **Display**: 1024x768 resolution (1920x1080 recommended)

### **Recommended Environment:**
- **Operating System**: Windows 10/11 Professional
- **RAM**: 8 GB or higher
- **Storage**: 1 GB free space for data growth
- **Display**: Full HD (1920x1080) or higher
- **Network**: Internet connection for updates (optional)

## 📦 Installation Options

### **Option 1: Quick Setup (Recommended for End Users)**

1. **Download the installer**: `PatientManagementSystem_Setup_v1.0.exe`
2. **Run as Administrator** (right-click → "Run as administrator")
3. **Follow the installation wizard**
4. **Launch from Start Menu or Desktop shortcut**

### **Option 2: Portable Standalone Executable**

1. **Download**: `PatientManagementSystem.exe` (standalone version)
2. **Create a folder** on your computer (e.g., `C:\PatientManagement`)
3. **Copy the executable** to the folder
4. **Double-click to run** (no installation required)
5. **Database and settings** will be created automatically

### **Option 3: Development/Source Code Installation**

**Prerequisites:**
- Visual Studio 2022 (Community Edition or higher)
- .NET 6.0 SDK or later
- Git (optional, for version control)

**Installation Steps:**
```bash
# Clone or download source code
git clone <repository-url>
cd PatientManagementSystem

# Restore packages and build
dotnet restore
dotnet build --configuration Release

# Create standalone executable
dotnet publish --configuration Release --runtime win-x64 --self-contained true --output publish
```

## 🔧 Build Instructions

### **Method 1: Automated Build Script**
```batch
# Run the build script
build_patient_system.bat
```

### **Method 2: Manual Build**
```batch
# Clean and restore
dotnet clean
dotnet restore

# Build application
dotnet build --configuration Release

# Create standalone deployment
dotnet publish --configuration Release --runtime win-x64 --self-contained true --output publish
```

### **Method 3: Visual Studio**
1. Open `PatientManagementSystem.csproj` in Visual Studio
2. Select **Release** configuration
3. Right-click project → **Publish**
4. Choose **Folder** target
5. Configure for **Self-contained** deployment
6. Click **Publish**

## 📁 File Structure

```
PatientManagementSystem/
├── PatientManagementSystem.exe    # Main application executable
├── PatientManagement.db           # SQLite database (created automatically)
├── README.md                      # This documentation file
├── PatientManagementSystem.csproj # Project configuration
├── build_patient_system.bat       # Automated build script
├── PatientManagementSetup.iss     # Installer script (Inno Setup)
└── publish/                       # Standalone deployment folder
    └── PatientManagementSystem.exe # Self-contained executable
```

## 🗄️ Database Information

### **Database Engine**: SQLite 3
### **Database File**: `PatientManagement.db`
### **Location**: Same folder as the executable

### **Database Schema:**

#### **Patients Table**
- PatientID (Primary Key, Auto-increment)
- FirstName, LastName
- DateOfBirth, Gender
- PhoneNumber, Email, Address
- EmergencyContact, BloodGroup
- MedicalHistory
- RegistrationDate (Auto-timestamp)

#### **Appointments Table**
- AppointmentID (Primary Key)
- PatientID (Foreign Key)
- DoctorName, Department
- AppointmentDate, AppointmentTime
- Status (Scheduled/Completed/Cancelled)
- Notes, CreatedDate

#### **Prescriptions Table**
- PrescriptionID (Primary Key)
- PatientID (Foreign Key)
- DoctorName, PrescriptionDate
- Diagnosis, Medicines, Instructions
- FollowUpDate, CreatedDate

#### **Billing Table**
- BillID (Primary Key)
- PatientID (Foreign Key)
- ServiceDescription, Amount
- PaymentStatus, PaymentMethod
- BillDate, DueDate, CreatedDate

## 🔐 Security Features

- **Local Data Storage**: All patient data stored locally on your computer
- **No Internet Dependency**: Works completely offline
- **Database Encryption**: SQLite database with optional encryption
- **Access Control**: Application-level security (extensible)
- **Data Backup**: Built-in database backup functionality
- **Audit Trail**: Timestamp tracking for all records

## 📚 User Guide

### **Getting Started**
1. **Launch the application**
2. **Register your first patient** in the "Patient Registration" tab
3. **Schedule appointments** using the "Appointments" tab
4. **Create prescriptions** in the "Prescriptions" tab
5. **Generate bills** in the "Billing" tab
6. **View reports** in the "Reports & Summary" tab

### **Daily Workflow**
1. **Morning**: Check today's appointments in Reports tab
2. **Registration**: Add new patients as they arrive
3. **Appointments**: Update appointment status as completed
4. **Prescriptions**: Create prescriptions after consultations
5. **Billing**: Generate bills for services provided
6. **Evening**: Review daily sales summary and pending payments

### **Data Management**
- **Backup**: Use Tools → Backup Database regularly
- **Export**: Use CSV export for external analysis
- **Search**: Use patient search functionality efficiently
- **Reports**: Generate custom date range reports as needed

## 🔧 Troubleshooting

### **Application Won't Start**
- Ensure Windows is up to date
- Check if .NET 6.0 Runtime is installed (for framework-dependent version)
- Run as Administrator if security settings are restrictive
- Check available disk space (minimum 500 MB)

### **Database Issues**
- Database file is created automatically in application folder
- For data recovery, restore from backup using Tools → Restore Database
- Database corruption: Delete `PatientManagement.db` (will create new empty database)

### **Performance Issues**
- Close other applications to free up memory
- Ensure adequate disk space for database growth
- Consider moving to SSD storage for better performance

### **Display Issues**
- Adjust Windows display scaling to 100% for optimal appearance
- Minimum resolution: 1024x768 (1920x1080 recommended)
- Update graphics drivers if display issues persist

## 🆘 Support & Updates

### **Technical Support**
- **Email**: support@healthcaresolutions.com
- **Documentation**: Complete user manual included
- **Video Tutorials**: Available on request
- **Remote Support**: Available for enterprise customers

### **Updates & Maintenance**
- **Automatic Updates**: Check for updates in application
- **Version History**: Maintained in changelog
- **Data Migration**: Automatic when upgrading versions
- **Backward Compatibility**: Maintained for previous versions

## 📄 License & Legal

### **Software License**
- **Type**: Proprietary Software
- **Usage**: Licensed for healthcare facilities
- **Distribution**: Authorized distributors only
- **Support**: Included with license purchase

### **Compliance**
- **HIPAA**: Designed with healthcare privacy requirements in mind
- **Data Protection**: Local storage ensures data privacy
- **Medical Standards**: Follows healthcare industry best practices
- **Security**: Regular security updates and patches

### **Disclaimer**
This software is provided as-is for healthcare management purposes. Users are responsible for data backup, security, and compliance with local healthcare regulations.

## 🚀 Professional Version Features

### **Additional Features Available:**
- Multi-user access control with role-based permissions
- Network database support (MySQL, SQL Server)
- Advanced reporting with charts and graphs
- Integration with external systems (EMR, Insurance)
- Automated appointment reminders (SMS, Email)
- Barcode scanning for patient identification
- Document management and storage
- Telehealth integration
- Advanced analytics and business intelligence

### **Enterprise Support:**
- 24/7 technical support
- Custom feature development
- Data migration services
- Staff training and onboarding
- Regular system maintenance and updates

---

**Patient Management System v1.0**  
**© 2025 Healthcare Solutions Ltd.**  
**All rights reserved.**

For more information, visit: https://healthcaresolutions.com  
Technical Support: support@healthcaresolutions.com  
Sales Inquiries: sales@healthcaresolutions.com