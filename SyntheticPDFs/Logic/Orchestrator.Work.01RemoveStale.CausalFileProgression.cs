namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {

        private static bool IsYounger(TrackedFileWitMetadata A, TrackedFileWitMetadata ThanB)
        {
            // use leq since we may add in batches, and be optimistic that the adder
            // has respected causality!

            return A.TrackedFile.AgeCommits <= ThanB.TrackedFile.AgeCommits;
        }


        private record StalenessInfo
        {
            internal required bool StaleWorkedSolutions { get; init; }
            internal required bool StaleSolutions { get; init; }

            internal required bool NoRoot { get; init; }
            internal required bool NoWorkedSolutions { get; init; }
            internal required bool NoSolutions { get; init; }

            internal required List<TrackedFileWitMetadata> StaleFiles { get; init; }

        }


        private class CausalFileProcession
        {

            internal TrackedFileWitMetadata? Root { get; set; }
            internal TrackedFileWitMetadata? WorkedSolutions { get; set; }
            internal TrackedFileWitMetadata? Solutions { get; set; }


            internal CausalFileProcession(IEnumerable<TrackedFileWitMetadata> files)
            {
                // arg checking

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

                // populate properties

                foreach (TrackedFileWitMetadata tfwm in files)
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


            // ensures that the files follow a causal chain 
            // a necessary condition for them to be correct and not stale
            internal StalenessInfo GetStalenessInfo()
            {
                var stale = new HashSet<TrackedFileWitMetadata>();

                bool staleWorkedSolutions = false, staleSolutions = false;

                bool noRoot = Root is null;
                bool noWorkedSolutions = WorkedSolutions is null;
                bool noSolutions = Solutions is null;


                // handle missing parents

                if (Root is null)
                {
                    if (WorkedSolutions is not null) { staleWorkedSolutions = true; stale.Add(WorkedSolutions); }
                    if (Solutions is not null) { staleSolutions = true; stale.Add(Solutions); }
                }

                if (WorkedSolutions is null)
                {
                    if (Solutions is not null) { staleSolutions = true; stale.Add(Solutions); }
                }

                // handle out of date
                // require that worked solutions be younger than root
                // and require that solutions be younger than worked solutions
                if (Root is not null && WorkedSolutions is not null)
                {
                    if (!IsYounger(WorkedSolutions, Root))
                    {
                        staleWorkedSolutions = true; stale.Add(WorkedSolutions);

                        if (Solutions is not null) { staleSolutions = true; stale.Add(Solutions); }
                    }
                }

                if (Root is not null && Solutions is not null)
                {
                    if (!IsYounger(Solutions, Root))
                    {
                        staleSolutions = true;
                        stale.Add(Solutions);
                    }
                }

                if (WorkedSolutions is not null && Solutions is not null)
                {
                    if (!IsYounger(Solutions, WorkedSolutions))
                    {
                        staleSolutions = true;
                        stale.Add(Solutions);
                    }
                }

                return new StalenessInfo
                {
                    NoRoot = noRoot,
                    NoWorkedSolutions = noWorkedSolutions,
                    NoSolutions = noSolutions,
                    StaleWorkedSolutions = staleWorkedSolutions,
                    StaleSolutions = staleSolutions,
                    StaleFiles = stale.ToList()
                };

            }
        }
    }
}
