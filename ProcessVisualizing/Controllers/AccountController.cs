using Microsoft.AspNetCore.Mvc;
using ProcessVisualizing.Models;
using System.Data.SQLite;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ProcessVisualizing.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController()
        {
            _context = new ApplicationDbContext();
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // Добавляем валидацию токена
        public IActionResult Register(User user, string password)
        {
            // Дополнительная проверка пароля
            if (string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("password", "Пароль обязателен");
            }
            try
            {
                using (var connection = _context.GetConnection())
                {
                    connection.Open();

                    // Проверка существования пользователя
                    var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM Users WHERE login = @login", connection);
                    checkCmd.Parameters.AddWithValue("@login", user.Login);
                    int exists = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (exists > 0)
                    {
                        ModelState.AddModelError("login", "Пользователь с таким логином уже существует");
                        return View(user);
                    }

                    // Хеширование пароля
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                    user.RegistrationDate = DateTime.Now;

                    // Добавление пользователя
                    //ЭТО ДЛЯ БЕЗОПАСНОСТИ SQL ИНЪЕКЦИЯ!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                    var insertCmd = new SQLiteCommand(
                        "INSERT INTO Users (login, password_hash, registration_date) VALUES (@login, @passwordHash, @regDate)",
                        connection);

                    insertCmd.Parameters.AddWithValue("@login", user.Login);
                    insertCmd.Parameters.AddWithValue("@passwordHash", user.PasswordHash);
                    insertCmd.Parameters.AddWithValue("@regDate", user.RegistrationDate);
                    insertCmd.ExecuteNonQuery();

                    // Получаем ID нового пользователя
                    var getIdCmd = new SQLiteCommand("SELECT last_insert_rowid()", connection);
                    int userId = Convert.ToInt32(getIdCmd.ExecuteScalar());

                    // Запись активности
                    var activityCmd = new SQLiteCommand(
                        "INSERT INTO UserActivity (user_id, action) VALUES (@userId, 'Регистрация')",
                        connection);
                    activityCmd.Parameters.AddWithValue("@userId", userId);
                    activityCmd.ExecuteNonQuery();

                    return RedirectToAction("Index", "Home");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Ошибка регистрации. Попробуйте позже");
                Console.WriteLine($"Validation error: {ex.Message}");
                return View(user);
            }
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(string login, string password)
        {
            try
            {
                using (var connection = _context.GetConnection())
                {
                    connection.Open();

                    var cmd = new SQLiteCommand(
                        "SELECT id, password_hash FROM Users WHERE login = @login",
                        connection);
                    cmd.Parameters.AddWithValue("@login", login);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string storedHash = reader["password_hash"].ToString();
                            if (BCrypt.Net.BCrypt.Verify(password, storedHash))
                            {
                                int userId = Convert.ToInt32(reader["id"]);

                                // Запись активности
                                var activityCmd = new SQLiteCommand(
                                    "INSERT INTO UserActivity (user_id, action) VALUES (@userId, 'Вход в систему')",
                                    connection);
                                activityCmd.Parameters.AddWithValue("@userId", userId);
                                activityCmd.ExecuteNonQuery();

                                return RedirectToAction("Index", "Home");
                            }
                        }
                    }
                }

                ModelState.AddModelError("", "Неверный логин или пароль");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Ошибка входа: {ex.Message}");
            }

            return View();
        }
    }
}