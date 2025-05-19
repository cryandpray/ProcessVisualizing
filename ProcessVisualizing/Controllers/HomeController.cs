using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProcessVisualizing.Models;
using System.Data.SQLite;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;

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

        //public IActionResult Index()
        //{
        //    return View();
        //}

        public IActionResult Index(int? fileId)
        {
            var model = new ProcessVisualizationModel
            {
                SelectedFileId = fileId
            };

            using (var connection = _context.GetConnection())
            {
                connection.Open();

                // Получаем список файлов
                var filesCmd = new SQLiteCommand("SELECT id, filename FROM Files ORDER BY id DESC", connection);
                using (var reader = filesCmd.ExecuteReader())
                {
                    model.AvailableFiles = new List<SelectListItem>();
                    while (reader.Read())
                    {
                        model.AvailableFiles.Add(new SelectListItem
                        {
                            Value = reader["id"].ToString(),
                            Text = reader["filename"].ToString(),
                            Selected = fileId.HasValue && reader["id"].ToString() == fileId.Value.ToString()
                        });
                    }
                }

                // Если выбран файл - загружаем его процессы
                if (model.SelectedFileId.HasValue)
                {
                    model.ProcessTree = GetProcessTree(model.SelectedFileId.Value, connection);
                }
            }

            return View(model);
        }

        private ProcessTree GetProcessTree(int fileId, SQLiteConnection connection)
        {
            var tree = new ProcessTree();
            var visualizationNodes = new List<object>();

            // Получаем процессы для файла
            var processesCmd = new SQLiteCommand(
                "SELECT id, name FROM Processes WHERE file_id = @fileId",
                connection);
            processesCmd.Parameters.AddWithValue("@fileId", fileId);

            using (var processesReader = processesCmd.ExecuteReader())
            {
                while (processesReader.Read())
                {
                    var processId = Convert.ToInt32(processesReader["id"]);
                    var processName = processesReader["name"].ToString();

                    var processNode = new ProcessNode
                    {
                        Id = processId,
                        Name = processName,
                        Events = new List<EventNode>()
                    };

                    var visProcessNode = new
                    {
                        id = processId,
                        text = processName,
                        children = new List<object>()
                    };

                    // Получаем события для процесса
                    var eventsCmd = new SQLiteCommand(
                        "SELECT id, event_name, timestamp FROM Events WHERE process_id = @processId ORDER BY timestamp",
                        connection);
                    eventsCmd.Parameters.AddWithValue("@processId", processId);

                    using (var eventsReader = eventsCmd.ExecuteReader())
                    {
                        while (eventsReader.Read())
                        {
                            var eventNode = new EventNode
                            {
                                Id = Convert.ToInt32(eventsReader["id"]),
                                Name = eventsReader["event_name"].ToString(),
                                Timestamp = Convert.ToDateTime(eventsReader["timestamp"])
                            };

                            processNode.Events.Add(eventNode);

                            visProcessNode.children.Add(new
                            {
                                id = eventNode.Id,
                                text = $"{eventNode.Name} ({eventNode.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")})"
                            });
                        }
                    }

                    tree.Nodes.Add(processNode);
                    visualizationNodes.Add(visProcessNode);
                }
            }

            // Сериализуем данные для визуализации
            tree.VisualizationData = Newtonsoft.Json.JsonConvert.SerializeObject(visualizationNodes);
            return tree;
        }

        [HttpPost]
        public async Task<IActionResult> UploadXes(IFormFile xesFile)
        {
            var model = new ProcessVisualizationModel();

            if (xesFile == null || xesFile.Length == 0)
            {
                ViewBag.Error = "Файл не выбран или пуст";
                return View("Index", model);
            }

            if (xesFile.Length > 10 * 1024 * 1024)
            {
                ViewBag.Error = "Файл слишком большой. Максимальный размер - 10MB";
                return View("Index", model);
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

                // После успешной загрузки обновляем список файлов
                using (var connection = _context.GetConnection())
                {
                    connection.Open();
                    // 1. Загружаем список файлов
                    var filesCmd = new SQLiteCommand("SELECT id, filename FROM Files ORDER BY id DESC", connection);
                    using (var reader = filesCmd.ExecuteReader())
                    {
                        model.AvailableFiles = new List<SelectListItem>();
                        while (reader.Read())
                        {
                            model.AvailableFiles.Add(new SelectListItem
                            {
                                Value = reader["id"].ToString(),
                                Text = reader["filename"].ToString()
                            });
                        }
                    }

                    // 2. Устанавливаем SelectedFileId на только что загруженный файл
                    if (model.AvailableFiles.Any())
                    {
                        model.SelectedFileId = int.Parse(model.AvailableFiles.First().Value);
                        model.ProcessTree = GetProcessTree(model.SelectedFileId.Value, connection);
                    }
                }

                ViewBag.Message = $"Успешно загружено {traces.Count} процессов";
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

            return View("Index", model); // Возвращаем модель с данными
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