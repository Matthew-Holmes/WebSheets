using SyntheticPDFs.Models;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        internal TexSourceModel GetContent(String filename)
        {
            String contents = File.ReadAllText(RepoDir + '/' + filename);

            return new TexSourceModel { FileNameFullPath = filename, TexSource = contents };
        }
    }
}
