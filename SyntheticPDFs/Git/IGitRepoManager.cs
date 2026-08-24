using SyntheticPDFs.Models;

namespace SyntheticPDFs.Git
{
    // just what the orchestrator needs - seam for testing without a real repo
    public interface IGitRepoManager
    {
        RepoModel GetLatestModelOfRepo();

        TexSourceModel GetContent(String filename);

        Task<bool> RemoveFiles(List<String> filenames, String hash);

        Task<bool> CommitAndPushTexSource(List<TexSourceModel> texSources, String hash);
    }
}
