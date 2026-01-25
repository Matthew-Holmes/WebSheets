using SyntheticPDFs.Models;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    // Put the actual orchestration process in here
    // queuing etc. boilerplate is in the main class file

    using RootName = String;

    using VariantInfo = HashSet<TrackedFileWitMetadata>;

    using StratefiedVariantInfo = Dictionary<ISO639_3Code, HashSet<TrackedFileWitMetadata>>;

    using StalenessInformation = Dictionary<ISO639_3Code, StalenessInfo>;

    public partial class Orchestrator
    {


        internal record SourceMetadata
        {
            internal required SourceType Type { get; init; }
            internal required ISO639_3Code Language { get; init; }

            internal required RootName RootName { get; init; }
        }


        internal record TrackedFileWitMetadata
        {
            public required TrackedFile TrackedFile { get; init; }

            public required SourceMetadata SourceMetadata { get; init; }
        }

        private int MaxFilesToGenerateBase { get; init; } = 30;

        // dynamically change this if having to back off too much
        // i.e. the repo is seeing high traffic and we need to sqeeze our commits in
        private int MaxFilesToGenerate { get; set; } = 30;

        private async Task DoWorkAsync()
        {

            // early exit after any git repo change, more maximum granularity of interactions
            // to avoid conflict with other users making changes (since server will always back off and retry)
            // keep track of if had to backoff then reduce the number of files added at a time??

            _logger.LogInformation("Work commencing");

            // prepare model of the repo so can apply business logic
            
            RepoModel repoModel = RepoManager.GetLatestModelOfRepo();

            String texExtNoDot = "tex";

            Dictionary<RootName, StalenessInformation> stalenessInformation = GetStalenessInformation(repoModel, texExtNoDot);
          
            List<TrackedFileWitMetadata> staleFiles = stalenessInformation.Values
                .SelectMany(
                    sfp => sfp.Values.Select(si => si.StaleFiles))
                .SelectMany(x => x)
                .ToList();

            if (staleFiles.Count > 0)
            {
                _logger.LogInformation("found stale files: removing");

                bool removed = await RepoManager.RemoveFiles(
                    staleFiles.Select(f => f.TrackedFile.FullPath).ToList(),
                    repoModel.LastCommitHash);

                if (!removed)
                {
                    _logger.LogWarning("failed to remove stale files, backing off, will retry later");
                    BackoffAndRegisterRetry();
                    return;
                }

                // this just did a commit, to keep synchronisation snappy, lets end it here
                // register a Ping to queue another run:
                Ping();

                // then exit - will pull the latest repo and build a model in the queued work
                return;

            }

            // decide what to generate (get this in separate file for business logic clarity)
            // get batch of files to create - since some will depend on these, not all the required files
            // will be in this batch

            List<SourceMetadata> batchToCreate = GetCreationBatch(stalenessInformation, MaxFilesToGenerate);

            if (batchToCreate.Count == 0)
            {
                _logger.LogInformation("nothing to do, leaving work early!");
                return;
            }


            // create synthetic Tex source

            List<TexSourceModel> syntheticSource = new();

            // TODO - make this an await all
            foreach (SourceMetadata sm in batchToCreate)
            {
                _logger.LogInformation($"generating Tex source for {sm.RootName}");

                syntheticSource.Add(await GenerateSyntheticSource(sm));
                break; // just for testing! TODO - remove
            }


            _logger.LogInformation("created source, committing and pushing");

            bool pushed = await RepoManager.CommitAndPushTexSource(syntheticSource, repoModel.LastCommitHash);
            
            if (!pushed)
            {
                _logger.LogWarning("failed to push synthetic source, backing off, consider adding caching!");
                BackoffAndRegisterRetry();
            }

            _logger.LogInformation("succesfully added synthetic source");

            RollbackBackoffStrategy();
        }


        private StratefiedVariantInfo StratifyByLanguage(VariantInfo variants)
        {
            return variants
                        .GroupBy(item => item.SourceMetadata.Language)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToHashSet()
                        );
        }

        private void RollbackBackoffStrategy()
        {
            MaxFilesToGenerate = Math.Min(MaxFilesToGenerate * 2, MaxFilesToGenerateBase);

            _logger.LogInformation($"Max files to generate set to {MaxFilesToGenerate}");
        }

        private void BackoffAndRegisterRetry()
        {
            MaxFilesToGenerate /= 2;

            if (MaxFilesToGenerate < 1) { MaxFilesToGenerate = 1; }

            _logger.LogInformation($"Max files to generate set to {MaxFilesToGenerate}, trying work again");

            Ping();
        }

        private Dictionary<RootName, HashSet<TrackedFileWitMetadata>> GetVariantInfo(RepoModel repoModel, String extSubset = "tex")
        {
            Dictionary<RootName, HashSet<TrackedFileWitMetadata>> variantInfo = new();

            foreach (TrackedFile tf in repoModel.Contents)
            {
                String ext = tf.FullPath.Split('.').Last();

                if (ext != extSubset) { continue; }

                //                                                                  extension       + "."
                String withoutExt = tf.FullPath.Substring(0, tf.FullPath.Length - (extSubset.Length + 1));

                SourceMetadata sourceMetadata = ParseMetadataFromFilename(withoutExt);

                TrackedFileWitMetadata tsm = new TrackedFileWitMetadata
                {
                    TrackedFile = tf,
                    SourceMetadata = sourceMetadata
                };

                if (variantInfo.ContainsKey(sourceMetadata.RootName))
                {
                    variantInfo[sourceMetadata.RootName].Add(tsm);
                }
                else
                {
                    variantInfo[sourceMetadata.RootName] = new HashSet<TrackedFileWitMetadata> { tsm };
                }
            }

            return variantInfo;
        }


    }
}
