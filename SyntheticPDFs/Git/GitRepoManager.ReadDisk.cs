using SyntheticPDFs.Models;

namespace SyntheticPDFs.Git
{
    public partial class GitRepoManager
    {
        public TexSourceModel GetContent(String filename)
        {
            String contents = File.ReadAllText(RepoDir + '/' + filename);

            return new TexSourceModel { FileNameFullPath = filename, TexSource = contents };
        }
    }
}
