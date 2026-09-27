using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PatientManagementSystem;
using Xunit;

namespace PatientManagementSystem.Tests
{
    public class AuthHelperTests : IDisposable
    {
        private readonly string dbPath;
        private readonly string connectionString;

        public AuthHelperTests()
        {
            dbPath = Path.Combine(Path.GetTempPath(), $"pms_test_{Guid.NewGuid():N}.db");
            connectionString = $"Data Source={dbPath};Version=3;";
            DbInit.EnsureSchema(connectionString);
        }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }

        [Fact]
        public void HashPassword_IsDeterministic_ForSameInputs()
        {
            string hash1 = AuthHelper.HashPassword("Sup3rSecret!", "somesalt", 10_000);
            string hash2 = AuthHelper.HashPassword("Sup3rSecret!", "somesalt", 10_000);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void HashPassword_DiffersAcrossSalts()
        {
            string hash1 = AuthHelper.HashPassword("Sup3rSecret!", "salt-a", 10_000);
            string hash2 = AuthHelper.HashPassword("Sup3rSecret!", "salt-b", 10_000);

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void HashPassword_DiffersAcrossIterationCounts()
        {
            string hash1 = AuthHelper.HashPassword("Sup3rSecret!", "samesalt", 1_000);
            string hash2 = AuthHelper.HashPassword("Sup3rSecret!", "samesalt", 2_000);

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public void ValidateLogin_DefaultAdmin_SucceedsWithCorrectPassword()
        {
            bool result = AuthHelper.ValidateLogin(connectionString, "admin", "admin123", out string error);

            Assert.True(result);
            Assert.Null(error);
        }

        [Fact]
        public void ValidateLogin_DefaultAdmin_FailsWithWrongPassword()
        {
            bool result = AuthHelper.ValidateLogin(connectionString, "admin", "wrong-password", out string error);

            Assert.False(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateLogin_UnknownUser_Fails()
        {
            bool result = AuthHelper.ValidateLogin(connectionString, "nobody", "whatever", out string error);

            Assert.False(result);
        }

        [Fact]
        public void ValidateLogin_UpgradesLegacySha256Hash_OnSuccessfulLogin()
        {
            const string username = "legacyuser";
            const string password = "LegacyPass123";
            string salt = Guid.NewGuid().ToString("N");
            string legacyHash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + password));
                legacyHash = Convert.ToBase64String(bytes);
            }

            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "INSERT INTO Users (Username, PasswordHash, Salt, Iterations) VALUES (@u, @h, @s, 0)", conn))
                {
                    cmd.Parameters.AddWithValue("@u", username);
                    cmd.Parameters.AddWithValue("@h", legacyHash);
                    cmd.Parameters.AddWithValue("@s", salt);
                    cmd.ExecuteNonQuery();
                }
            }

            bool result = AuthHelper.ValidateLogin(connectionString, username, password, out string error);
            Assert.True(result);
            Assert.Null(error);

            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "SELECT Iterations, Salt, PasswordHash FROM Users WHERE Username = @u", conn))
                {
                    cmd.Parameters.AddWithValue("@u", username);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        Assert.True(reader.Read());
                        int iterations = Convert.ToInt32(reader["Iterations"]);
                        string newSalt = reader["Salt"].ToString();
                        string newHash = reader["PasswordHash"].ToString();

                        Assert.True(iterations > 0);
                        Assert.NotEqual(salt, newSalt);
                        Assert.NotEqual(legacyHash, newHash);
                    }
                }
            }

            // The upgraded hash must still verify correctly on a second login.
            bool secondLogin = AuthHelper.ValidateLogin(connectionString, username, password, out string secondError);
            Assert.True(secondLogin);
            Assert.Null(secondError);
        }

        [Fact]
        public void ValidateLogin_WrongPasswordAgainstLegacyHash_Fails()
        {
            const string username = "legacyuser2";
            string salt = Guid.NewGuid().ToString("N");
            string legacyHash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "CorrectPassword"));
                legacyHash = Convert.ToBase64String(bytes);
            }

            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(
                    "INSERT INTO Users (Username, PasswordHash, Salt, Iterations) VALUES (@u, @h, @s, 0)", conn))
                {
                    cmd.Parameters.AddWithValue("@u", username);
                    cmd.Parameters.AddWithValue("@h", legacyHash);
                    cmd.Parameters.AddWithValue("@s", salt);
                    cmd.ExecuteNonQuery();
                }
            }

            bool result = AuthHelper.ValidateLogin(connectionString, username, "WrongPassword", out string error);

            Assert.False(result);
            Assert.NotNull(error);
        }
    }
}
