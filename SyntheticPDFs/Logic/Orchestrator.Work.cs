using SyntheticPDFs.Models;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    // Put the actual orchestration process in here
    // queuing etc. boilerplate is in the main class file

    using RootName = String;

    using VariantInfo = HashSet<TrackedFileWithMetadata>;

    using StratefiedVariantInfo = Dictionary<ISO639_3Code, HashSet<TrackedFileWithMetadata>>;

    using StalenessInformation = Dictionary<ISO639_3Code, StalenessInfo>;

    public partial class Orchestrator
    {


        internal record SourceMetadata
        {
            internal required SourceType Type { get; init; } // root/solution/worked solutions
            internal required SourceArchetype Archetype { get; init; } // worksheet/slides/poster etc.
            internal required ISO639_3Code Language { get; init; }

            internal required RootName RootName { get; init; }

            // defaulted rather than required, since all the English source the pipeline
            // started out generating is the original of its type
            internal SourceRendition Rendition { get; init; } = SourceRendition.Original;
        }

        // what a batch entry is asking for. most work synthesises a file that isn't there
        // yet, but question slides also have to have their answer overlay helpers checked,
        // and that check is what stamps the worked solutions as seen
        internal enum GenerationJob
        {
            CreateSource,
            CheckAnswerMacros,
        }

        internal record GenerationRequest
        {
            internal required SourceMetadata Target { get; init; }
            internal required GenerationJob Job { get; init; }
        }


        internal record TrackedFileWithMetadata
        {
            public required TrackedFile TrackedFile { get; init; }

            public required SourceMetadata SourceMetadata { get; init; }
        }

        // what one pass actually did, so the caller can decide whether to queue another
        internal enum PassOutcome
        {
            RemovedStaleFiles,
            Generated,
            NothingToDo,
            GitConflict,
            GenerationFailed,
        }

        private int MaxFilesToGenerateBase { get; init; }

        // dynamically change this if having to back off too much
        // i.e. the repo is seeing high traffic and we need to sqeeze our commits in
        private int MaxFilesToGenerate { get; set; }

        internal async Task<PassOutcome> DoOnePassAsync()
        {

            // early exit after any git repo change, more maximum granularity of interactions
            // to avoid conflict with other users making changes (since server will always back off and retry)
            // keep track of if had to backoff then reduce the number of files added at a time??

            _logger.LogInformation("Work commencing");

            // prepare model of the repo so can apply business logic
            
            RepoModel repoModel = RepoManager.GetLatestModelOfRepo();

            String texExtNoDot = "tex";

            Dictionary<RootName, RootPlanState> planStates = GetPlanStates(repoModel, texExtNoDot);

            // before anything else, and in particular before the early return below: a
            // request is satisfied the moment its file exists, and forgetting it there is
            // what stops the file being rebuilt when a later edit makes it stale
            ForgetSatisfiedRequests(planStates);

            List<TrackedFileWithMetadata> staleFiles = planStates.Values
                .SelectMany(state => state.StaleFiles)
                .ToList();

            if (staleFiles.Count > 0)
            {
                _logger.LogInformation("found stale files: removing");

                bool removed = await RepoManager.RemoveFiles(
                    staleFiles.Select(f => f.TrackedFile.FullPath).ToList(),
                    repoModel.LastCommitHash);

                if (!removed)
                {
                    _logger.LogWarning("failed to remove stale files, will back off and retry later");
                    return PassOutcome.GitConflict;
                }

                // this just did a commit, to keep synchronisation snappy, lets end the pass here
                // the caller queues another run, which pulls the latest and rebuilds the model
                return PassOutcome.RemovedStaleFiles;
            }

            // decide what to generate (get this in separate file for business logic clarity)
            // get batch of files to create - since some will depend on these, not all the required files
            // will be in this batch

            List<GenerationRequest> batchToCreate = GetCreationBatch(planStates, MaxFilesToGenerate);

            if (batchToCreate.Count == 0)
            {
                _logger.LogInformation("nothing to do, leaving work early!");
                return PassOutcome.NothingToDo;
            }

            // generate concurrently - AgentBase already caps this at 5 locally and 20 across
            // all agents, so there is no need to throttle again here
            List<List<TexSourceModel>> generated = (await Task.WhenAll(
                batchToCreate.Select(TryGenerateSyntheticSource))).ToList();

            List<TexSourceModel> syntheticSource = generated
                .SelectMany(ts => ts)
                .ToList();

            // a request that settled nothing produced no files at all
            int failed = generated.Count(ts => ts.Count == 0);

            if (syntheticSource.Count == 0)
            {
                _logger.LogError("every file in the batch failed to generate, giving up on this pass");
                return PassOutcome.GenerationFailed;
            }

            if (failed > 0)
            {
                _logger.LogWarning(
                    "{Failed} of {Total} requests failed to generate, pushing the rest",
                    failed,
                    batchToCreate.Count);
            }

            _logger.LogInformation("created source, committing and pushing");

            bool pushed = await RepoManager.CommitAndPushTexSource(syntheticSource, repoModel.LastCommitHash);

            if (!pushed)
            {
                _logger.LogWarning("failed to push synthetic source, will back off and retry later");
                return PassOutcome.GitConflict;
            }

            _logger.LogInformation("succesfully added synthetic source");

            return PassOutcome.Generated;
        }

        // one file failing shouldn't cost us the rest of the batch - it stays missing
        // from the repo model, so the next pass picks it up again
        private async Task<List<TexSourceModel>> TryGenerateSyntheticSource(GenerationRequest request)
        {
            _logger.LogInformation($"generating Tex source for {request.Target.RootName}");

            try
            {
                return await GenerateSyntheticSource(request);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    "failed to {Job} {Type} for {Root}: {Message}",
                    request.Job, request.Target.Type, request.Target.RootName, e.Message);

                return new List<TexSourceModel>();
            }
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

        private void Backoff()
        {
            MaxFilesToGenerate /= 2;

            if (MaxFilesToGenerate < 1) { MaxFilesToGenerate = 1; }

            _logger.LogInformation($"Max files to generate set to {MaxFilesToGenerate}");
        }

        private Dictionary<RootName, HashSet<TrackedFileWithMetadata>> GetVariantInfo(RepoModel repoModel, String extSubset = "tex")
        {
            Dictionary<RootName, HashSet<TrackedFileWithMetadata>> variantInfo = new();

            foreach (TrackedFile tf in repoModel.Contents)
            {
                String ext = tf.FullPath.Split('.').Last();

                if (ext != extSubset) { continue; }

                // the dictionary is a source of definitions, not a worksheet - nothing is
                // derived from it, and treating it as a root would have the pipeline try
                // to write worked solutions for a list of words
                if (tf.FullPath == ContentRepository.DictionaryPath) { continue; }

                //                                                                  extension       + "."
                String withoutExt = tf.FullPath.Substring(0, tf.FullPath.Length - (extSubset.Length + 1));

                SourceMetadata sourceMetadata = ParseMetadataFromFilename(withoutExt, _logger);

                TrackedFileWithMetadata tsm = new TrackedFileWithMetadata
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
                    variantInfo[sourceMetadata.RootName] = new HashSet<TrackedFileWithMetadata> { tsm };
                }
            }

            return variantInfo;
        }


    }
}
