namespace WebSheets.Models
{
    public class FileNode
    {
        public string Name { get; set; } = "";

        public string CleanName
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                    return Name;

                const string extension = ".pdf";
                int extIndex = Name.LastIndexOf(extension, StringComparison.OrdinalIgnoreCase);

                if (extIndex < 0)
                    return Name;

                string baseName = WorksheetNaming.StripHashSuffix(Name[..extIndex]);
                return baseName + Name[extIndex..];
            }
        }


        public bool IsDirectory { get; set; }
        public Dictionary<string, FileNode> Children { get; set; } = new();

        public FileNode? Parent { get; init; }


    }
}
