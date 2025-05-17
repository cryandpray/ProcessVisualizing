using System;
using System.Collections.Generic;
using System.Xml;

namespace ProcessVisualizing.Models
{
    public class XesEvent
    {
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }

    public class XesTrace
    {
        public string Name { get; set; }
        public List<XesEvent> Events { get; set; } = new List<XesEvent>();
    }
}