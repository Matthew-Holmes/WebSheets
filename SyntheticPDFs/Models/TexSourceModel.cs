using System.Collections.Frozen;

namespace SyntheticPDFs.Models
{
    public record TexSourceModel
    {
        public required String TexSource { get; init; }
        public required String FileNameFullPath { get; init; }

        public String FileNameNoPathNoExt => FileNameFullPath.Split('/').Last().Split('.').First();

        public String DirNoFileName
        {
            get
            {
                if (string.IsNullOrEmpty(FileNameFullPath))
                    return string.Empty;

                var lastSlash = FileNameFullPath.LastIndexOf('/');

                return lastSlash >= 0
                    ? FileNameFullPath.Substring(0, lastSlash)
                    : string.Empty;
            }
        }
    }
}
