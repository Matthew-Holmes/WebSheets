using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class StalenessTests
    {
        private const String Root = "latex/worksheets/sheet";

        // lower age == younger, since age counts commits back from HEAD
        private static Orchestrator.TrackedFileWithMetadata File(
            SourceType type,
            int age,
            SourceArchetype archetype = SourceArchetype.Worksheet,
            String root = Root)
        {
            var metadata = new Orchestrator.SourceMetadata
            {
                RootName = root,
                Type = type,
                Archetype = archetype,
                Language = ISO639_3Code.eng,
            };

            return new Orchestrator.TrackedFileWithMetadata
            {
                SourceMetadata = metadata,
                TrackedFile = new TrackedFile
                {
                    FullPath = Orchestrator.GetFilenameFromMetadata(metadata),
                    AgeCommits = age,
                },
            };
        }

        private static Orchestrator.StalenessInfo Info(params Orchestrator.TrackedFileWithMetadata[] files)
            => new Orchestrator.CausalFileProcession(files).GetStalenessInfo();

        [TestMethod]
        public void CorrectlyOrderedSetIsNotStale()
        {
            var info = Info(
                File(SourceType.Root, 3),
                File(SourceType.WorkedSolutions, 2),
                File(SourceType.Solutions, 1));

            Assert.AreEqual(0, info.StaleFiles.Count);
            Assert.IsFalse(info.StaleWorkedSolutions);
            Assert.IsFalse(info.StaleSolutions);
            Assert.IsFalse(info.NoRoot);
        }

        [TestMethod]
        public void RootOnlyIsCleanButIncomplete()
        {
            var info = Info(File(SourceType.Root, 1));

            Assert.AreEqual(0, info.StaleFiles.Count);
            Assert.IsFalse(info.NoRoot);
            Assert.IsTrue(info.NoWorkedSolutions);
            Assert.IsTrue(info.NoSolutions);
        }

        [TestMethod]
        public void MissingRootOrphansItsChildren()
        {
            var info = Info(
                File(SourceType.WorkedSolutions, 2),
                File(SourceType.Solutions, 1));

            Assert.IsTrue(info.NoRoot);
            Assert.IsTrue(info.StaleWorkedSolutions);
            Assert.IsTrue(info.StaleSolutions);
            Assert.AreEqual(2, info.StaleFiles.Count);
        }

        [TestMethod]
        public void SolutionsWithoutWorkedSolutionsAreStale()
        {
            var info = Info(
                File(SourceType.Root, 2),
                File(SourceType.Solutions, 1));

            Assert.IsTrue(info.NoWorkedSolutions);
            Assert.IsTrue(info.StaleSolutions);
            Assert.AreEqual(1, info.StaleFiles.Count);
        }

        [TestMethod]
        public void EditingTheRootMakesEverythingBelowItStale()
        {
            // root edited most recently, so it is younger than its derivatives
            var info = Info(
                File(SourceType.Root, 1),
                File(SourceType.WorkedSolutions, 5),
                File(SourceType.Solutions, 4));

            Assert.IsTrue(info.StaleWorkedSolutions);
            Assert.IsTrue(info.StaleSolutions);
            Assert.AreEqual(2, info.StaleFiles.Count);
        }

        [TestMethod]
        public void EditingWorkedSolutionsMakesOnlyTheAnswersStale()
        {
            var info = Info(
                File(SourceType.Root, 5),
                File(SourceType.WorkedSolutions, 1),
                File(SourceType.Solutions, 3));

            Assert.IsFalse(info.StaleWorkedSolutions, "worked solutions are still younger than the root");
            Assert.IsTrue(info.StaleSolutions);
            Assert.AreEqual(1, info.StaleFiles.Count);
        }

        [TestMethod]
        public void SameCommitIsTreatedAsCausallyValid()
        {
            // batches land in one commit, so equal ages must not count as stale
            var info = Info(
                File(SourceType.Root, 2),
                File(SourceType.WorkedSolutions, 2),
                File(SourceType.Solutions, 2));

            Assert.AreEqual(0, info.StaleFiles.Count);
        }

        [TestMethod]
        public void ProcessionKnowsItsArchetype()
        {
            var procession = new Orchestrator.CausalFileProcession(new[]
            {
                File(SourceType.Root, 2, SourceArchetype.QuestionSlides),
                File(SourceType.WorkedSolutions, 1, SourceArchetype.QuestionSlides),
            });

            Assert.AreEqual(SourceArchetype.QuestionSlides, procession.Archetype);
        }

        [TestMethod]
        public void MixedArchetypesAreRejected()
        {
            // the archetype comes from the folder, so files sharing a root name must agree
            Assert.ThrowsException<ArgumentException>(() => Info(
                File(SourceType.Root, 2, SourceArchetype.Worksheet),
                File(SourceType.WorkedSolutions, 1, SourceArchetype.Poster)));
        }

        [TestMethod]
        public void AnEmptyProcessionIsRejected()
        {
            // there is no archetype to read off nothing
            Assert.ThrowsException<ArgumentException>(
                () => new Orchestrator.CausalFileProcession(
                    Array.Empty<Orchestrator.TrackedFileWithMetadata>()));
        }
    }
}
