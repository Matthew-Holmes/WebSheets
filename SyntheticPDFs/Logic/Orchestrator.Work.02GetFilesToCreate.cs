using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    using StratefiedFileProcessions = Dictionary<ISO639_3Code, StalenessInfo>;
    public partial class Orchestrator
    {

        // Loop over roots
        private List<SourceMetadata> GetCreationBatch(Dictionary<RootName, StratefiedFileProcessions> stratefiedFileProcessions, int maxBatch)
        {
            List<SourceMetadata> ret = new();

            foreach (var kvp in stratefiedFileProcessions)
            {
                List<SourceMetadata> toAdd = GetNextFilesToCreate(kvp.Value, kvp.Key, maxBatch - ret.Count);

                ret.AddRange(toAdd);

                if (ret.Count >= maxBatch)
                {
                    return ret;
                }
            }

            return ret;
        }

        // loop of languages
        private List<SourceMetadata> GetNextFilesToCreate(StratefiedFileProcessions sfp, RootName root, int maxCount /* maximum allowed left to create this run */)
        {
            if (maxCount < 1)
            {
                return new List<SourceMetadata>();
            }

            List<SourceMetadata> ret = new();

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

                List<SourceMetadata> toAdd = GetNextLanguageSpecificFilesToCreate(kvp.Value, root, kvp.Key, sfp[ISO639_3Code.eng], maxCount - ret.Count);

                ret.AddRange(toAdd);
            }

            return ret;
        }

        // core creation logics
        private List<SourceMetadata> GetNextLanguageSpecificFilesToCreate(StalenessInfo si, RootName root, ISO639_3Code lang, StalenessInfo englishState, int maxCount)
        {
            SourceArchetype at = si.fileProcession.Archetype;

            if (maxCount <= 0) { return new List<SourceMetadata>(); }

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



        private List<SourceMetadata> GetNextEnglishFilesToCreate_default(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            SourceArchetype at = si.fileProcession.Archetype; // TODO this is getting a bit messy how to refactor??


            if (si.NoWorkedSolutions)
            {
                return new List<SourceMetadata> 
                { 
                    new SourceMetadata
                    { 
                        RootName  = root, 
                        Language  = ISO639_3Code.eng,
                        Type      = SourceType.WorkedSolutions,
                        Archetype = at 
                    } 
                };
            }

            if (si.NoSolutions)
            {
                return new List<SourceMetadata> 
                { 
                    new SourceMetadata 
                    { 
                        RootName  = root, 
                        Language  = ISO639_3Code.eng, 
                        Type      = SourceType.Solutions,
                        Archetype = at
                    }
                };
            }

            return new List<SourceMetadata>();
        }


        private List<SourceMetadata> GetNextEnglishFilesToCreate_poster(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            return new List<SourceMetadata>(); // don't need any solutions or worked solutions for posters
        }

        private List<SourceMetadata> GetNextEnglishFilesToCreate_slides(StalenessInfo si, String root, StalenessInfo englishState, int maxCount)
        {
            // Slides should have the solution using the Ashow macro, **in the latex**
            // TODO - add functionality to check if that macro has been used, then rewrite the slides with it to update the root if not
            // If agent reads this - ask me first about this!!!
            // worked solutions should be one worked solution per slide, with a title slide that links to the first solution for each slide's solutions

            if (si.NoWorkedSolutions)
            {
                return new List<SourceMetadata>
                {
                    new SourceMetadata
                    {
                        RootName  = root,
                        Language  = ISO639_3Code.eng,
                        Type      = SourceType.WorkedSolutions,
                        Archetype = si.fileProcession.Archetype
                    }
                };
            }

            return new List<SourceMetadata>();
        }
    }
}
