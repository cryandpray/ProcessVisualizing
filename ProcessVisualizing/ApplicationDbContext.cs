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
            
            CREATE TABLE IF NOT EXISTS Files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                filename TEXT NOT NULL,
                upload_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            
            CREATE TABLE IF NOT EXISTS Processes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                creation_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                file_id INTEGER NOT NULL,
                FOREIGN KEY (file_id) REFERENCES Files(id) ON DELETE CASCADE
            );
            
            CREATE TABLE IF NOT EXISTS Events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                process_id INTEGER NOT NULL,
                event_name TEXT NOT NULL,
                timestamp TIMESTAMP NOT NULL,
                FOREIGN KEY (process_id) REFERENCES Processes(id) ON DELETE CASCADE
            );
            
            CREATE TABLE IF NOT EXISTS Attributes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id INTEGER NOT NULL,
                attribute_name TEXT NOT NULL,
                attribute_value TEXT NOT NULL,
                FOREIGN KEY (event_id) REFERENCES Events(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS UserFile (
                user_id INTEGER NOT NULL,
                file_id INTEGER NOT NULL,
                PRIMARY KEY (user_id, file_id),
                FOREIGN KEY (user_id) REFERENCES Users(id) ON DELETE CASCADE,
                FOREIGN KEY (file_id) REFERENCES Files(id) ON DELETE CASCADE
            );
        ";
            var command = new SQLiteCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }



}