using Microsoft.AspNetCore.Mvc;
using ProcessVisualizing.Models;
using System.Data.SQLite;

namespace ProcessVisualizing.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtService _jwtService;

        public AccountController(ApplicationDbContext context, JwtService jwtService)
        {
            _context = context;
            _jwtService = jwtService;
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

                    // Генерируем токен
                    var token = _jwtService.GenerateToken(userId);

                    // Сохраняем в HttpOnly cookie
                    Response.Cookies.Append("jwt_token", token, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true, // включи только при HTTPS
                        Expires = DateTimeOffset.UtcNow.AddMinutes(43200)
                    });

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

                                // Генерируем токен
                                var token = _jwtService.GenerateToken(userId);

                                // Сохраняем в HttpOnly cookie
                                Response.Cookies.Append("jwt_token", token, new CookieOptions
                                {
                                    HttpOnly = true,
                                    Secure = true, // включи только при HTTPS
                                    Expires = DateTimeOffset.UtcNow.AddMinutes(43200)
                                });

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

        public int? GetUserIdFromToken()
        {
            if (Request.Cookies.TryGetValue("jwt_token", out var token))
            {
                var principal = _jwtService.ValidateToken(token);
                var uidClaim = principal?.FindFirst("uid");
                if (uidClaim != null && int.TryParse(uidClaim.Value, out var userId))
                {
                    return userId;
                }
            }
            return null;
        }

        public IActionResult Logout()
        {
            Response.Cookies.Delete("jwt_token");
            return RedirectToAction("Login");
        }


    }
}