using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using System.Collections.ObjectModel;

namespace SyntheticPDFs.Tests
{
    // The causal chain: a file derived from another must be no older than it, or what it
    // says no longer follows from what it was derived from.
    //
    // These go through ContentModel rather than around it, so they pin the whole path a
    // real pass takes - a list of paths and commit ages in, a judgement out.
    [TestClass]
    public class StalenessTests
    {
        private const String Root = "latex/worksheets/sheet";

        // lower age == younger, since age counts commits back from HEAD
        private static TrackedFile File(SheetPart part, int age, String root = Root)
        {
            var metadata = new SourceMetadata
            {
                RootName  = root,
                Part      = part,
                Archetype = SheetArchetypes.Worksheet,
                Language  = ISO639_3Code.eng,
            };

            return new TrackedFile { FullPath = metadata.FilePath, AgeCommits = age };
        }

        private static SheetState State(params TrackedFile[] files)
        {
            var repo = new RepoModel
            {
                Contents       = new ReadOnlyCollection<TrackedFile>(files),
                LastCommitHash = "deadbeef",
            };

            return ContentModel.From(repo)
                .Judged(new LanguageTable(new SyntheticPDFs.Configuration.L2Options()),
                        includeGlossaries: false)
                .Sheets[Root];
        }

        private static bool IsStale(SheetState state, SheetPart part) =>
            state.StaleFiles.Any(f => f.SourceMetadata.Part == part);

        private static bool IsMissing(SheetState state, SheetPart part) =>
            state.IsMissing(new ContentKey(ISO639_3Code.eng, part, SheetForm.Original));

        [TestMethod]
        public void CorrectlyOrderedSetIsNotStale()
        {
            var state = State(
                File(SheetPart.Root, 3),
                File(SheetPart.WorkedSolutions, 2),
                File(SheetPart.Solutions, 1));

            Assert.AreEqual(0, state.StaleFiles.Count);
        }

        [TestMethod]
        public void RootOnlyIsCleanButIncomplete()
        {
            var state = State(File(SheetPart.Root, 1));

            Assert.AreEqual(0, state.StaleFiles.Count);
            Assert.IsFalse(IsMissing(state, SheetPart.Root));
            Assert.IsTrue(IsMissing(state, SheetPart.WorkedSolutions));
            Assert.IsTrue(IsMissing(state, SheetPart.Solutions));
        }

        [TestMethod]
        public void MissingRootOrphansItsChildren()
        {
            var state = State(
                File(SheetPart.WorkedSolutions, 2),
                File(SheetPart.Solutions, 1));

            Assert.IsTrue(IsMissing(state, SheetPart.Root));
            Assert.IsTrue(IsStale(state, SheetPart.WorkedSolutions));
            Assert.IsTrue(IsStale(state, SheetPart.Solutions));
            Assert.AreEqual(2, state.StaleFiles.Count);
        }

        [TestMethod]
        public void SolutionsWithoutWorkedSolutionsAreStale()
        {
            var state = State(
                File(SheetPart.Root, 2),
                File(SheetPart.Solutions, 1));

            Assert.IsTrue(IsMissing(state, SheetPart.WorkedSolutions));
            Assert.IsTrue(IsStale(state, SheetPart.Solutions));
            Assert.AreEqual(1, state.StaleFiles.Count);
        }

        [TestMethod]
        public void EditingTheRootMakesEverythingBelowItStale()
        {
            // root edited most recently, so it is younger than its derivatives
            var state = State(
                File(SheetPart.Root, 1),
                File(SheetPart.WorkedSolutions, 5),
                File(SheetPart.Solutions, 4));

            Assert.IsTrue(IsStale(state, SheetPart.WorkedSolutions));
            Assert.IsTrue(IsStale(state, SheetPart.Solutions));
            Assert.AreEqual(2, state.StaleFiles.Count);
        }

        [TestMethod]
        public void EditingWorkedSolutionsMakesOnlyTheAnswersStale()
        {
            var state = State(
                File(SheetPart.Root, 5),
                File(SheetPart.WorkedSolutions, 1),
                File(SheetPart.Solutions, 3));

            Assert.IsFalse(IsStale(state, SheetPart.WorkedSolutions),
                "worked solutions are still younger than the root");
            Assert.IsTrue(IsStale(state, SheetPart.Solutions));
            Assert.AreEqual(1, state.StaleFiles.Count);
        }

        [TestMethod]
        public void SameCommitIsTreatedAsCausallyValid()
        {
            // batches land in one commit, so equal ages must not count as stale
            var state = State(
                File(SheetPart.Root, 2),
                File(SheetPart.WorkedSolutions, 2),
                File(SheetPart.Solutions, 2));

            Assert.AreEqual(0, state.StaleFiles.Count);
        }

        [TestMethod]
        public void EverySheetKnowsWhichArchetypeItIs()
        {
            var repo = new RepoModel
            {
                Contents = new ReadOnlyCollection<TrackedFile>(new[]
                {
                    new TrackedFile { FullPath = "latex/starters/deck.tex", AgeCommits = 2 },
                    new TrackedFile { FullPath = "latex/cheatSheets/poster.tex", AgeCommits = 2 },
                    new TrackedFile { FullPath = "latex/worksheets/sheet.tex", AgeCommits = 2 },
                }),
                LastCommitHash = "deadbeef",
            };

            var model = ContentModel.From(repo);

            Assert.AreEqual(SheetArchetypes.QuestionSlides,
                model.Sheets["latex/starters/deck"].Archetype);
            Assert.AreEqual(SheetArchetypes.Poster,
                model.Sheets["latex/cheatSheets/poster"].Archetype);
            Assert.AreEqual(SheetArchetypes.Worksheet,
                model.Sheets["latex/worksheets/sheet"].Archetype);
        }

        [TestMethod]
        public void TheDictionaryIsHeldApartFromTheSheets()
        {
            // it is a source of definitions rather than a worksheet, and treating it as a
            // root would have the pipeline try to write worked solutions for a word list
            var repo = new RepoModel
            {
                Contents = new ReadOnlyCollection<TrackedFile>(new[]
                {
                    new TrackedFile
                    {
                        FullPath   = "latex/dictionary/mathematicalDictionary.tex",
                        AgeCommits = 2,
                    },
                }),
                LastCommitHash = "deadbeef",
            };

            var model = ContentModel.From(repo);

            Assert.AreEqual(0, model.Sheets.Count);
            Assert.IsTrue(model.DictionaryAt("latex/dictionary/mathematicalDictionary.tex").Exists);
        }
    }
}
