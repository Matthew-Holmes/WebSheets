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
        private List<SourceMetadata> GetNextFilesToCreate(StratefiedFileProcessions sfp, RootName root, int maxCount)
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

                List<SourceMetadata> toAdd = GetNextLanguageSpecificFilesToCreate(kvp.Value, root, kvp.Key, sfp[ISO639_3Code.eng], maxCount - ret.Count);

                if (ret.Count >= maxCount)
                {
                    return ret;
                }
            }

            return ret;
        }

        // core creation logics
        private List<SourceMetadata> GetNextLanguageSpecificFilesToCreate(StalenessInfo si, RootName root, ISO639_3Code lang, StalenessInfo englishState, int maxCount)
        {
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


            if (si.NoRoot)
            {
                if (lang == ISO639_3Code.eng)
                {
                    throw new ArgumentException("can't generate root files for English");
                }
                return new List<SourceMetadata> { new SourceMetadata{ RootName = root, Language = lang, Type = SourceType.Root } };
            }

            if (si.NoWorkedSolutions)
            {
                return new List<SourceMetadata> { new SourceMetadata { RootName = root, Language = lang, Type = SourceType.WorkedSolutions } };
            }

            if (si.NoSolutions)
            {
                return new List<SourceMetadata> { new SourceMetadata { RootName = root, Language = lang, Type = SourceType.Solutions } };
            }

            return new List<SourceMetadata>();

        }
    }
}
