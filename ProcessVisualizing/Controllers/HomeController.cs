using Microsoft.AspNetCore.Mvc;
using ProcessVisualizing.Models;
using System.Data.SQLite;
using System.Xml;
using System.Xml.Linq;

namespace ProcessVisualizing.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ApplicationDbContext context, ILogger<HomeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadXes(IFormFile xesFile)
        {
            if (xesFile == null || xesFile.Length == 0)
            {
                ViewBag.Error = "Файл не выбран или пуст";
                return View("Index");
            }

            // Проверка размера файла (максимум 10MB)
            if (xesFile.Length > 10 * 1024 * 1024)
            {
                ViewBag.Error = "Файл слишком большой. Максимальный размер - 10MB";
                return View("Index");
            }

            try
            {
                // Сохраняем файл во временное место
                var tempFilePath = Path.GetTempFileName();
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await xesFile.CopyToAsync(stream);
                }

                // Парсинг XES-файла
                List<XesTrace> traces;
                using (var fileStream = System.IO.File.OpenRead(tempFilePath))
                {
                    traces = ParseXesFile(fileStream);
                }

                // Удаляем временный файл
                System.IO.File.Delete(tempFilePath);

                // Сохранение в БД
                await SaveTracesToDatabaseAsync(traces, xesFile.FileName);

                ViewBag.Message = $"Успешно загружено {traces.Count} процессов";
                _logger.LogInformation($"Успешно загружен XES-файл: {xesFile.FileName}, процессов: {traces.Count}");
            }
            catch (XmlException xmlEx)
            {
                ViewBag.Error = $"Ошибка формата XES-файла: {xmlEx.Message}";
                _logger.LogError(xmlEx, "Ошибка парсинга XES-файла");
            }
            catch (SQLiteException sqlEx)
            {
                ViewBag.Error = $"Ошибка базы данных: {sqlEx.Message}";
                _logger.LogError(sqlEx, "Ошибка SQLite при сохранении XES-данных");
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Неожиданная ошибка: {ex.Message}";
                _logger.LogError(ex, "Ошибка при загрузке XES-файла");
            }

            return View("Index");
        }

        private List<XesTrace> ParseXesFile(Stream stream)
        {
            var traces = new List<XesTrace>();
            try
            {
                var doc = XDocument.Load(stream);

                // Получаем namespace из корневого элемента
                XNamespace ns = doc.Root?.Name.Namespace ?? "";

                foreach (var traceElem in doc.Descendants(ns + "trace"))
                {
                    var trace = new XesTrace();

                    // Имя процесса (trace-level attribute)
                    var nameAttr = traceElem.Elements(ns + "string")
                        .FirstOrDefault(e => e.Attribute("key")?.Value == "concept:name");
                    trace.Name = nameAttr?.Attribute("value")?.Value ?? "Unnamed Process";

                    // Обработка событий внутри trace
                    foreach (var eventElem in traceElem.Elements(ns + "event"))
                    {
                        var xesEvent = new XesEvent();

                        // Имя события
                        var nameElem = eventElem.Elements(ns + "string")
                            .FirstOrDefault(e => e.Attribute("key")?.Value == "concept:name");
                        xesEvent.Name = nameElem?.Attribute("value")?.Value ?? "Unnamed Event";

                        // Временная метка
                        var timeElem = eventElem.Elements(ns + "date")
                            .FirstOrDefault(e => e.Attribute("key")?.Value == "time:timestamp");
                        if (timeElem != null && DateTime.TryParse(timeElem.Attribute("value")?.Value, out var timestamp))
                        {
                            xesEvent.Timestamp = timestamp;
                        }

                        // Все атрибуты (string, date и др.)
                        foreach (var attr in eventElem.Elements())
                        {
                            var key = attr.Attribute("key")?.Value;
                            var value = attr.Attribute("value")?.Value;

                            if (!string.IsNullOrWhiteSpace(key) && value != null)
                            {
                                xesEvent.Attributes[key] = value;
                            }
                        }

                        trace.Events.Add(xesEvent);
                    }

                    traces.Add(trace);
                }

                _logger.LogInformation($"Успешно распаршено {traces.Count} процессов.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге XES-файла с использованием LINQ to XML.");
                throw;
            }

            return traces;

        }

        private async Task SaveTracesToDatabaseAsync(List<XesTrace> traces, string filename)
        {
            using (var connection = _context.GetConnection())
            {
                await connection.OpenAsync();
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Сохраняем информацию о файле
                            int fileId;
                            using (var fileCmd = new SQLiteCommand(
                                "INSERT INTO Files (filename) VALUES (@filename); SELECT last_insert_rowid();",
                                connection, transaction))
                            {
                                fileCmd.Parameters.AddWithValue("@filename", filename);
                                fileId = Convert.ToInt32(await fileCmd.ExecuteScalarAsync());
                            }

                            // 2. Сохраняем процессы и связанные данные
                            foreach (var trace in traces)
                            {
                                var processCmd = new SQLiteCommand(
                                    "INSERT INTO Processes (name, creation_date, file_id) VALUES (@name, @date, @fileId); " +
                                    "SELECT last_insert_rowid();",
                                    connection, transaction);

                                processCmd.Parameters.AddWithValue("@name", trace.Name);
                                processCmd.Parameters.AddWithValue("@date", DateTime.Now);
                                processCmd.Parameters.AddWithValue("@fileId", fileId);

                                int processId = Convert.ToInt32(await processCmd.ExecuteScalarAsync());

                                foreach (var xesEvent in trace.Events)
                                {
                                    var eventCmd = new SQLiteCommand(
                                        "INSERT INTO Events (process_id, event_name, timestamp) VALUES (@processId, @eventName, @timestamp); " +
                                        "SELECT last_insert_rowid();",
                                        connection, transaction);

                                    eventCmd.Parameters.AddWithValue("@processId", processId);
                                    eventCmd.Parameters.AddWithValue("@eventName", xesEvent.Name);
                                    eventCmd.Parameters.AddWithValue("@timestamp", xesEvent.Timestamp);

                                    int eventId = Convert.ToInt32(await eventCmd.ExecuteScalarAsync());

                                    foreach (var attr in xesEvent.Attributes)
                                    {
                                        var attrCmd = new SQLiteCommand(
                                            "INSERT INTO Attributes (event_id, attribute_name, attribute_value) VALUES (@eventId, @name, @value)",
                                            connection, transaction);

                                        attrCmd.Parameters.AddWithValue("@eventId", eventId);
                                        attrCmd.Parameters.AddWithValue("@name", attr.Key);
                                        attrCmd.Parameters.AddWithValue("@value", attr.Value);

                                        await attrCmd.ExecuteNonQueryAsync();
                                    }
                                }
                            }

                            await transaction.CommitAsync();
                            _logger.LogInformation("Данные успешно сохранены в БД");
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            _logger.LogError(ex, "Ошибка при сохранении данных в БД");
                            throw;
                        }
                    }
                }

            }
        }
    }
}