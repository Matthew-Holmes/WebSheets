using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    // One pass over the repository: read what is there, throw away what is no longer
    // trustworthy, and make one round of what is missing.
    //
    // Everything here is decision making. What a file is, what a sheet may have and
    // whether it is still current are all answered by the content model, so this reads as
    // a sequence of choices rather than as string handling.
    public partial class Orchestrator
    {
        // what a batch entry is asking for. most work synthesises a file that isn't there
        // yet, but question slides also have to have their answer overlay helpers checked,
        // and that check is what stamps the worked solutions as seen
        internal enum GenerationJob
        {
            CreateSource,
            CheckAnswerMacros,
            RefreshDictionary,
            ExtendDictionary,
            RestateGlossary,
        }

        internal record GenerationRequest
        {
            internal required SourceMetadata Target { get; init; }
            internal required GenerationJob Job { get; init; }
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
        // i.e. the repo is seeing high traffic and we need to squeeze our commits in
        private int MaxFilesToGenerate { get; set; }

        internal async Task<PassOutcome> DoOnePassAsync()
        {
            // early exit after any change to the repository, for maximum granularity of
            // interaction - somebody else may be committing, and the server always backs
            // off and retries rather than holding a claim on the branch
            _logger.LogInformation("Work commencing");

            RepoModel repo = RepoManager.GetLatestModelOfRepo();

            // names first, which is cheap and touches no files, and only then the few
            // files that have to be opened to judge what was built from what
            ContentModel model = ContentModel.From(repo, _logger);

            model = WithDictionaries(model);

            DictionaryState dictionary = model.DictionaryAt(ContentRepository.DictionaryPath);

            // read fresh from the keys as they are judged below, since a word added to
            // the dictionary last pass is not a new word this pass
            _newWords.Clear();
            _restating.Clear();

            model = model.Judged(
                Languages,
                L2Settings.GenerateVocabularyKeys,
                sheet => Outdated(sheet, dictionary));

            // before anything else, and in particular before the early return below: a
            // request is satisfied the moment its file exists, and forgetting it there is
            // what stops the file being rebuilt when a later edit makes it stale
            ForgetSatisfiedRequests(model);

            List<ContentFile> staleFiles = model.Sheets.Values
                .SelectMany(sheet => sheet.StaleFiles)
                .ToList();

            if (staleFiles.Count > 0)
            {
                _logger.LogInformation("found stale files: removing");

                bool removed = await RepoManager.RemoveFiles(
                    staleFiles.Select(f => f.FullPath).ToList(), model.LastCommitHash);

                if (!removed)
                {
                    _logger.LogWarning("failed to remove stale files, will back off and retry later");
                    return PassOutcome.GitConflict;
                }

                // this just did a commit, so end the pass here to keep synchronisation
                // snappy - the caller queues another run, which pulls and rebuilds
                return PassOutcome.RemovedStaleFiles;
            }

            List<GenerationRequest> batchToCreate = GetCreationBatch(model, MaxFilesToGenerate);

            if (batchToCreate.Count == 0)
            {
                _logger.LogInformation("nothing to do, leaving work early!");
                return PassOutcome.NothingToDo;
            }

            // generate concurrently - AgentBase already caps this at 5 locally and 20
            // across all agents, so there is no need to throttle again here
            List<List<TexSourceModel>> generated = (await Task.WhenAll(
                batchToCreate.Select(request => TryGenerateSyntheticSource(request, model)))).ToList();

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

            bool pushed = await RepoManager.CommitAndPushTexSource(
                syntheticSource, model.LastCommitHash);

            if (!pushed)
            {
                _logger.LogWarning("failed to push synthetic source, will back off and retry later");
                return PassOutcome.GitConflict;
            }

            _logger.LogInformation("successfully added synthetic source");

            return PassOutcome.Generated;
        }

        // one file failing shouldn't cost us the rest of the batch - it stays missing
        // from the model, so the next pass picks it up again
        private async Task<List<TexSourceModel>> TryGenerateSyntheticSource(
            GenerationRequest request, ContentModel model)
        {
            _logger.LogInformation($"generating Tex source for {request.Target.RootName}");

            try
            {
                return await GenerateSyntheticSource(request, model);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    "failed to {Job} {Part} for {Root}: {Message}",
                    request.Job, request.Target.Part, request.Target.RootName, e.Message);

                return new List<TexSourceModel>();
            }
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
    }
}
