using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Data.SQLite;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessVisualizing.Models;

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
                await SaveTracesToDatabaseAsync(traces);

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
                var settings = new XmlReaderSettings
                {
                    Async = true,
                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

                using (var reader = XmlReader.Create(stream, settings))
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);

                    foreach (XmlNode traceNode in xmlDoc.SelectNodes("//trace"))
                    {
                        var trace = new XesTrace();

                        // Получаем имя процесса
                        var nameNode = traceNode.SelectSingleNode("string[@key='concept:name']");
                        trace.Name = nameNode?.Attributes["value"]?.Value ?? "Unnamed Process";
                        _logger.LogDebug($"Найден процесс: {trace.Name}");

                        // Получаем все события
                        foreach (XmlNode eventNode in traceNode.SelectNodes("event"))
                        {
                            var xesEvent = new XesEvent();

                            // Имя события
                            var eventNameNode = eventNode.SelectSingleNode("string[@key='concept:name']");
                            xesEvent.Name = eventNameNode?.Attributes["value"]?.Value ?? "Unnamed Event";

                            // Время события
                            var timestampNode = eventNode.SelectSingleNode("date[@key='time:timestamp']");
                            if (timestampNode != null && DateTime.TryParse(timestampNode.Attributes["value"]?.Value, out var timestamp))
                            {
                                xesEvent.Timestamp = timestamp;
                            }

                            // Атрибуты события
                            foreach (XmlNode attrNode in eventNode.ChildNodes)
                            {
                                if (attrNode.Attributes?["key"] != null && attrNode.Attributes?["value"] != null)
                                {
                                    xesEvent.Attributes[attrNode.Attributes["key"].Value] = attrNode.Attributes["value"].Value;
                                }
                            }

                            trace.Events.Add(xesEvent);
                            _logger.LogDebug($"Добавлено событие: {xesEvent.Name} с {xesEvent.Attributes.Count} атрибутами");
                        }

                        traces.Add(trace);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге XES-файла");
                throw;
            }

            _logger.LogInformation($"Успешно распаршено {traces.Count} процессов");
            return traces;
        }

        private async Task SaveTracesToDatabaseAsync(List<XesTrace> traces)
        {
            using (var connection = _context.GetConnection())
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var trace in traces)
                        {
                            // 1. Сохраняем процесс
                            var processCmd = new SQLiteCommand(
                                "INSERT INTO Processes (name, creation_date) VALUES (@name, @date); " +
                                "SELECT last_insert_rowid();",
                                connection, transaction);

                            processCmd.Parameters.AddWithValue("@name", trace.Name);
                            processCmd.Parameters.AddWithValue("@date", DateTime.Now);

                            int processId = Convert.ToInt32(await processCmd.ExecuteScalarAsync());

                            foreach (var xesEvent in trace.Events)
                            {
                                // 2. Сохраняем событие
                                var eventCmd = new SQLiteCommand(
                                    "INSERT INTO Events (process_id, event_name, timestamp) " +
                                    "VALUES (@processId, @eventName, @timestamp); " +
                                    "SELECT last_insert_rowid();",
                                    connection, transaction);

                                eventCmd.Parameters.AddWithValue("@processId", processId);
                                eventCmd.Parameters.AddWithValue("@eventName", xesEvent.Name);
                                eventCmd.Parameters.AddWithValue("@timestamp", xesEvent.Timestamp);

                                int eventId = Convert.ToInt32(await eventCmd.ExecuteScalarAsync());

                                // 3. Сохраняем атрибуты
                                foreach (var attr in xesEvent.Attributes)
                                {
                                    var attrCmd = new SQLiteCommand(
                                        "INSERT INTO Attributes (event_id, attribute_name, attribute_value) " +
                                        "VALUES (@eventId, @name, @value)",
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