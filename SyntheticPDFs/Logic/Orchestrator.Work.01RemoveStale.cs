using SyntheticPDFs.Models;
using System.Collections.Frozen;
using System.Diagnostics;
using static SyntheticPDFs.Logic.Orchestrator;

namespace SyntheticPDFs.Logic
{
    using RootName = String;

    using VariantInfo = HashSet<TrackedFileWitMetadata>;

    using StratefiedVariantInfo = Dictionary<ISO639_3Code, HashSet<TrackedFileWitMetadata>>;

    using StalenessInformation = Dictionary<ISO639_3Code, StalenessInfo>;

    public partial class Orchestrator
    {

        private Dictionary<RootName, StalenessInformation> GetStalenessInformation(RepoModel repoModel, String ext)
        {
            Dictionary<RootName, VariantInfo> variantInfo = GetVariantInfo(repoModel, ext);

            Dictionary<RootName, StratefiedVariantInfo> variantInfoStratefied =
                variantInfo.Select(kvp => new KeyValuePair<RootName, StratefiedVariantInfo>
                (
                    key: kvp.Key,
                    value: StratifyByLanguage(kvp.Value)
                )).ToDictionary();

            Dictionary<RootName, StalenessInformation> stalenessInformation =
                variantInfoStratefied.Select(kvp => new KeyValuePair<RootName, StalenessInformation>
                (
                    key: kvp.Key,
                    value: kvp.Value.Select(kvp => new KeyValuePair<ISO639_3Code, StalenessInfo>
                        (
                            key: kvp.Key,
                            value: new CausalFileProcession(kvp.Value).GetStalenessInfo()
                        )).ToDictionary()
                )).ToDictionary();

            stalenessInformation = ApplyEnglishPriorityStaleness(stalenessInformation);

            return stalenessInformation;

        }

        private Dictionary<RootName, StalenessInformation> ApplyEnglishPriorityStaleness(Dictionary<RootName, StalenessInformation> stalenessInformation)
        {
            // require that all foreign language sheets be younger than the English sheets
            // TODO - decide if the generator should see both the english version and the preceding L2 sheet in the procession!
                // Maybe only if there has been a modification to the L2 precedent??
            if (stalenessInformation.Any(kvp => kvp.Value.Keys.Any(k => k != ISO639_3Code.eng)))
            {
                throw new NotImplementedException("need to write this function!");
            }

            return stalenessInformation;


        }

    }
}
