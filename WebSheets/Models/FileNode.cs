namespace WebSheets.Models
{
    public class FileNode
    {
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public Dictionary<string, FileNode> Children { get; set; } = new();
    }
}
