using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProcessVisualizing.Models
{
    public class ProcessVisualizationModel
    {
        public List<SelectListItem> AvailableFiles { get; set; }
        public int? SelectedFileId { get; set; }
        public ProcessTree ProcessTree { get; set; }
    }

    public class ProcessTree
    {
        public List<ProcessNode> Nodes { get; set; } = new List<ProcessNode>();
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