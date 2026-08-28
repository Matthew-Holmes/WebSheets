using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    using StratefiedFileProcessions = Dictionary<ISO639_3Code, StalenessInfo>;
    public partial class Orchestrator
    {

        // Loop over roots
        private List<GenerationRequest> GetCreationBatch(Dictionary<RootName, StratefiedFileProcessions> stratefiedFileProcessions, int maxBatch)
        {
            List<GenerationRequest> ret = new();

            foreach (var kvp in stratefiedFileProcessions)
            {
                List<GenerationRequest> toAdd = GetNextFilesToCreate(kvp.Value, kvp.Key, maxBatch - ret.Count);

                ret.AddRange(toAdd);

                if (ret.Count >= maxBatch)
                {
                    return ret;
                }
            }

            return ret;
        }

        // loop of languages
        private List<GenerationRequest> GetNextFilesToCreate(StratefiedFileProcessions sfp, RootName root, int maxCount /* maximum allowed left to create this run */)
        {
            if (maxCount < 1)
            {
                return new List<GenerationRequest>();
            }

            List<GenerationRequest> ret = new();

            if (!sfp.ContainsKey(ISO639_3Code.eng) || sfp[ISO639_3Code.eng].NoRoot)
            {
                throw new ArgumentException("all files are stale - should not have called this!");
            }

            foreach (var kvp in sfp)
            {
                if (kvp.Key != ISO639_3Code.eng)
                {
                    throw new NotImplementedException("need to implement L2 file creation logic");
                }

                if (ret.Count >= maxCount)
                {
                    return ret;
                }

                List<GenerationRequest> toAdd = GetNextLanguageSpecificFilesToCreate(kvp.Value, root, kvp.Key, sfp[ISO639_3Code.eng], maxCount - ret.Count);

                ret.AddRange(toAdd);
            }

            return ret;
        }

        // core creation logics
        private List<GenerationRequest> GetNextLanguageSpecificFilesToCreate(StalenessInfo si, RootName root, ISO639_3Code lang, StalenessInfo englishState, int maxCount)
        {
            SourceArchetype at = si.fileProcession.Archetype;

            if (maxCount <= 0) { return new List<GenerationRequest>(); }

            if (si.StaleSolutions || si.StaleWorkedSolutions)
            {
                // these should have been removed first!
                throw new ArgumentException("can't generate files while stale files exist!");
            }


            if (lang != ISO639_3Code.eng)
            {
                throw new NotImplementedException("need to implement logic for L2 sheets!");
                // use the english state too!
            }
            else
            {

                if (si.NoRoot)
                {
                    throw new Exception("can't generate English root for a file!");
                }

                switch (at)
                {
                    case SourceArchetype.Worksheet:
                        return GetNextEnglishFilesToCreate_default(si, root, englishState, maxCount);
                    case SourceArchetype.QuestionSlides:
                        return GetNextEnglishFilesToCreate_slides( si, root, englishState, maxCount);
                    case SourceArchetype.Poster:
                        return GetNextEnglishFilesToCreate_poster( si, root, englishState, maxCount);
                    default:
                        // only fires if a new archetype is added without deciding its logic
                        throw new NotImplementedException("need to decide on what the generation logic is like for this case!");
                }
            }
        }

        // every request is for one English file belonging to one root, so this saves
        // spelling the metadata out at each of the call sites below
        private static GenerationRequest Request(
            RootName root,
            SourceType type,
            SourceArchetype at,
            GenerationJob job = GenerationJob.CreateSource)
        {
            return new GenerationRequest
            {
                Target = new SourceMetadata
                {
                    RootName  = root,
                    Language  = ISO639_3Code.eng,
                    Type      = type,
                    Archetype = at,
                },
                Job = job,
            };
        }


        private List<GenerationRequest> GetNextEnglishFilesToCreate_default(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            SourceArchetype at = si.fileProcession.Archetype; // TODO this is getting a bit messy how to refactor??

            if (si.NoWorkedSolutions)
            {
                return new List<GenerationRequest> { Request(root, SourceType.WorkedSolutions, at) };
            }

            if (si.NoSolutions)
            {
                return new List<GenerationRequest> { Request(root, SourceType.Solutions, at) };
            }

            return new List<GenerationRequest>();
        }


        private List<GenerationRequest> GetNextEnglishFilesToCreate_poster(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            return new List<GenerationRequest>(); // don't need any solutions or worked solutions for posters
        }

        private List<GenerationRequest> GetNextEnglishFilesToCreate_slides(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            // Slides show the solution using the Ashow macros, **in the latex**, so a deck
            // never needs a separate answer key - just worked solutions, one worked solution
            // per slide, with a title slide that links to the first solution for each slide
            SourceArchetype at = si.fileProcession.Archetype;

            if (si.NoWorkedSolutions)
            {
                // making them checks the deck's macros first, so nothing extra is needed here
                return new List<GenerationRequest> { Request(root, SourceType.WorkedSolutions, at) };
            }

            if (WorkedSolutionsRecordAnAnswerMacroCheck(si))
            {
                return new List<GenerationRequest>();
            }

            // no record of a check on these worked solutions - either a human wrote them, or
            // the macros were taken back out of the deck since, so go and look
            return new List<GenerationRequest>
            {
                Request(root, SourceType.WorkedSolutions, at, GenerationJob.CheckAnswerMacros)
            };
        }

        // the record is a line inside the worked solutions, so this has to read the file.
        // that is disk, not network, and it stops the check being paid for every pass
        private bool WorkedSolutionsRecordAnAnswerMacroCheck(StalenessInfo si)
        {
            String filename = si.fileProcession.WorkedSolutions!.TrackedFile.FullPath;

            try
            {
                return AnswerMacros.IsMarkedVerified(RepoManager.GetContent(filename).TexSource);
            }
            catch (Exception e)
            {
                // one unreadable file mustn't stop the pass, and since we can't tell whether
                // a check is owed, the cheap answer is the one that spends nothing
                _logger.LogWarning(
                    "could not read {File} to look for the answer macro marker: {Message}",
                    filename, e.Message);

                return true;
            }
        }
    }
}
