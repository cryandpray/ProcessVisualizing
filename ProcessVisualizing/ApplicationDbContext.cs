using System.Data.SQLite;
using System.IO;

public class ApplicationDbContext
{
    private readonly string _connectionString;

    public ApplicationDbContext()
    {
        string dbFileName = "DataBase1.db";
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", dbFileName);
        _connectionString = $"Data Source={dbPath};Version=3;";

        if (!File.Exists(dbPath))
        {
            SQLiteConnection.CreateFile(dbPath);
            InitializeDatabase();
        }
    }

    public SQLiteConnection GetConnection() => new SQLiteConnection(_connectionString);

    private void InitializeDatabase()
    {
        using (var connection = GetConnection())
        {
            connection.Open();
            string sql = @"
                PRAGMA foreign_keys = on;
                
                CREATE TABLE IF NOT EXISTS Users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Login TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    RegistrationDate TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE TABLE IF NOT EXISTS UserActivity (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    action TEXT NOT NULL,
                    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (user_id) REFERENCES Users(id) ON DELETE CASCADE
                );
            ";
            var command = new SQLiteCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }
}