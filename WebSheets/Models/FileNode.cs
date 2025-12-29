namespace WebSheets.Models
{
    public class FileNode
    {
        private static int GitHashLen => 12;
        private static int PathNoiseToStrip => GitHashLen + 1; // plus the underscore

        public string Name { get; set; } = "";

        public string CleanName
        {
            get
            {
                if (string.IsNullOrEmpty(Name))
                    return Name;

                const string extension = ".pdf";
                int extIndex = Name.LastIndexOf(extension, StringComparison.OrdinalIgnoreCase);

                if (extIndex <= PathNoiseToStrip)
                    return Name; // Removing 12 chars would leave empty or negative length

                return Name.Substring(0, extIndex - PathNoiseToStrip) + Name.Substring(extIndex);
            }
        }


        public bool IsDirectory { get; set; }
        public Dictionary<string, FileNode> Children { get; set; } = new();
    }
}
