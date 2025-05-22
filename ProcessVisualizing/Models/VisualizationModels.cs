using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProcessVisualizing.Models
{
    public class ProcessVisualizationModel
    {
        public List<SelectListItem> AvailableFiles { get; set; }
        public int? SelectedFileId { get; set; }
        public ProcessTree ProcessTree { get; set; }
        public string VisualizationData { get; set; }
        public string EditFileName { get; set; } // Добавляем новое свойство для редактирования имени
        public int? EditFileId { get; set; } // ID файла для редактирования
    }

    public class ProcessTree
    {
        public List<ProcessNode> Nodes { get; set; } = new List<ProcessNode>();
        public string VisualizationData { get; set; } // Добавляем это свойство
    }

    public class ProcessNode
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<EventNode> Events { get; set; }
    }

    public class EventNode
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
    }
}