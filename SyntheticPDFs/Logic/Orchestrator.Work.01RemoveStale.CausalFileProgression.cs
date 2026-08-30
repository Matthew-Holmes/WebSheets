using System.Diagnostics.CodeAnalysis;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        internal record StalenessInfo
        {
            internal required bool StaleWorkedSolutions { get; init; }
            internal required bool StaleSolutions { get; init; }

            internal required bool NoRoot { get; init; }
            internal required bool NoWorkedSolutions { get; init; }
            internal required bool NoSolutions { get; init; }

            internal required List<TrackedFileWithMetadata> StaleFiles { get; init; }

            internal required CausalFileProcession fileProcession { get; init; }
        }


        // The chain of original files for one root in one language - the questions, the
        // workings derived from them, and the answers derived from those. This is the
        // English view of a root; translated files have more than one file per type and
        // so are held by a RootPlanState instead.
        internal class CausalFileProcession
        {

            internal TrackedFileWithMetadata? Root { get; set; }
            internal TrackedFileWithMetadata? WorkedSolutions { get; set; }
            internal TrackedFileWithMetadata? Solutions { get; set; }

            internal required SourceArchetype Archetype { get; init; }

            private readonly List<TrackedFileWithMetadata> _files;

            private ISO639_3Code _language;


            [SetsRequiredMembers]
            internal CausalFileProcession(IEnumerable<TrackedFileWithMetadata> files)
            {
                // arg checking
                if (files.Count() == 0)
                {
                    throw new ArgumentException("No files!");
                }

                var distinctLangs = files.Select(tfwm => tfwm.SourceMetadata.Language).ToHashSet();

                if (distinctLangs.Count > 1)
                {
                    throw new ArgumentException("all files must be the same language");
                }

                var distinctTypes = files.Select(tfwm => tfwm.SourceMetadata.Type).ToHashSet();

                if (distinctTypes.Count != files.Count())
                {
                    throw new ArgumentException("each file must be a distinct type!");
                }

                var distinctArchetypes = files.Select(tfwm => tfwm.SourceMetadata.Archetype).ToHashSet();

                if (distinctArchetypes.Count != 1)
                {
                    throw new ArgumentException("each file must be the same archetype!");
                }

                Archetype = distinctArchetypes.First();

                _language = distinctLangs.First();

                _files = files.ToList();

                // populate properties

                foreach (TrackedFileWithMetadata tfwm in files)
                {
                    switch (tfwm.SourceMetadata.Type)
                    {
                        case SourceType.Root:
                            Root = tfwm;
                            break;
                        case SourceType.WorkedSolutions:
                            WorkedSolutions = tfwm;
                            break;
                        case SourceType.Solutions:
                            Solutions = tfwm;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }


            // ensures that the files follow a causal chain, a necessary condition for
            // them to be correct and not stale.
            //
            // The rules themselves live in SourcePlan and are applied by BuildPlanState,
            // so that an archetype's shape is stated once rather than here and again in
            // batch selection. This maps that result back on to the three named slots.
            internal StalenessInfo GetStalenessInfo()
            {
                RootPlanState state = BuildPlanState(
                    Root?.SourceMetadata.RootName ?? String.Empty,
                    Archetype,
                    _files,
                    SourcePlan.OriginalChain(Archetype, _language));

                bool IsStale(TrackedFileWithMetadata? file) =>
                    file is not null && state.StaleFiles.Contains(file);

                return new StalenessInfo
                {
                    NoRoot               = Root is null,
                    NoWorkedSolutions    = WorkedSolutions is null,
                    NoSolutions          = Solutions is null,
                    StaleWorkedSolutions = IsStale(WorkedSolutions),
                    StaleSolutions       = IsStale(Solutions),
                    StaleFiles           = state.StaleFiles.ToList(),
                    fileProcession       = this,
                };
            }
        }
    }
}
