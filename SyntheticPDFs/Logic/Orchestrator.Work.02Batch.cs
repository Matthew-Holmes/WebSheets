using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Logic
{
    public partial class Orchestrator
    {
        // Forms the generator can actually produce. A plan describes the whole space so
        // that staleness covers it, but asking for a file nothing knows how to write
        // would fail the whole batch, so selection is held to what is implemented.
        private static readonly HashSet<SheetForm> ImplementedForms = new()
        {
            SheetForm.Original,
            SheetForm.Glossary,
            SheetForm.TranslatedGlossary,
            SheetForm.ParallelText,
            SheetForm.Tier3Only,
            SheetForm.RetrieveAndConnect,
        };

        // Gathers everything the whole repository could generate now, then lets the
        // priority gate decide which kind of work this pass does. Nothing is ready until
        // everything it derives from is settled, so a sheet advances one step per pass on
        // its own without needing to be told to.
        private List<GenerationRequest> GetCreationBatch(ContentModel model, int maxBatch)
        {
            List<Candidate> candidates = new();

            int rootOrder = 0;

            foreach (SheetState sheet in model.Sheets.Values)
            {
                candidates.AddRange(CandidatesFor(sheet, rootOrder));

                rootOrder++;
            }

            candidates.AddRange(DictionaryCandidates(model, rootOrder));

            return Select(candidates, maxBatch);
        }

        private IEnumerable<Candidate> CandidatesFor(SheetState sheet, int rootOrder)
        {
            if (sheet.StaleFiles.Count > 0)
            {
                // these should have been removed first!
                throw new ArgumentException("can't generate files while stale files exist!");
            }

            // Work owed on a file that already exists comes before anything derived from
            // it. A deck whose answer overlays have not been checked may still be
            // rewritten by that check, and a variant made from it first would be made
            // from bytes that are about to change - then thrown away as stale and made
            // again from the ones that replaced them.
            List<GenerationRequest> owed = OutstandingChecks(sheet);

            if (owed.Count > 0)
            {
                return owed.Select(request => new Candidate
                {
                    Request   = request,
                    Priority  = GenerationPriority.English,
                    RootOrder = rootOrder,
                    Sequence  = 0,
                });
            }

            List<Candidate> ret = new();

            long order = 0;

            foreach (PlannedFile planned in sheet.Creatable())
            {
                if (!ImplementedForms.Contains(planned.Key.Form)) { continue; }

                long? requestedAt = RequestedAt(sheet.RootName, planned.Key);

                // anything not eager is made only when it has been asked for, and once
                // removed as stale is not rebuilt
                if (!planned.Eager && requestedAt is null) { continue; }

                ret.Add(new Candidate
                {
                    Request   = Request(sheet.RootName, planned.Key, sheet.Archetype),
                    Priority  = requestedAt is null
                        ? PriorityOf(planned.Key)
                        : GenerationPriority.Requested,
                    RootOrder = rootOrder,
                    Sequence  = requestedAt ?? order++,
                });
            }

            return ret;
        }

        // Work owed on a file that already exists, which the plan has nothing to say
        // about - it describes which files there should be, not what has been done to
        // them. Only a deck that reveals its own answers has any: its worked solutions
        // carry a record that the deck was checked, and one without that record still
        // owes the check.
        private List<GenerationRequest> OutstandingChecks(SheetState sheet)
        {
            if (!sheet.Archetype.RevealsItsOwnAnswers)
            {
                return new List<GenerationRequest>();
            }

            ContentKey worked = new(
                ISO639_3Code.eng, SheetPart.WorkedSolutions, SheetForm.Original);

            ContentFile? file = sheet.File(worked);

            if (file is null) { return new List<GenerationRequest>(); }

            if (WorkedSolutionsRecordAnAnswerMacroCheck(file))
            {
                return new List<GenerationRequest>();
            }

            // no record of a check on these worked solutions - either a human wrote them,
            // or the macros were taken back out of the deck since, so go and look
            return new List<GenerationRequest>
            {
                Request(sheet.RootName, worked, sheet.Archetype, GenerationJob.CheckAnswerMacros)
            };
        }

        // every request is for one file belonging to one root, so this saves spelling
        // the metadata out at each of the call sites
        private static GenerationRequest Request(
            String root,
            ContentKey key,
            SheetArchetype archetype,
            GenerationJob job = GenerationJob.CreateSource)
        {
            return new GenerationRequest
            {
                Target = new SourceMetadata
                {
                    RootName  = root,
                    Language  = key.Language,
                    Part      = key.Part,
                    Form      = key.Form,
                    Archetype = archetype,
                },
                Job = job,
            };
        }

        // the record is a line inside the worked solutions, so this has to read the file.
        // that is disk, not network, and it stops the check being paid for every pass
        private bool WorkedSolutionsRecordAnAnswerMacroCheck(ContentFile worked)
        {
            String? source = TryRead(worked.FullPath);

            // since we can't tell whether a check is owed, the cheap answer is the one
            // that spends nothing
            return source is null || AnswerMacros.IsMarkedVerified(source);
        }
    }
}
