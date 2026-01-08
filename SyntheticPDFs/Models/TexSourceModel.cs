namespace SyntheticPDFs.Models
{
    public record TexSourceModel
    {
        public required String TexSource { get; init; }
        public String FileNameFullPath => DirNoFileName + "/" + FileNameNoPathNoExt + ".tex";

        
        public required String FileNameNoPathNoExt { get; init; }
        public required String DirNoFileName { get; init; }

    }
}
