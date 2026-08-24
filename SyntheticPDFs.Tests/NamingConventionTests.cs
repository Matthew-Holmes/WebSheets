using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class NamingConventionTests
    {
        // SourceType is internal, so it can't be a parameter on a public test method
        [TestMethod]
        [DataRow("quadratics", "Root")]
        [DataRow("quadratics", "WorkedSolutions")]
        [DataRow("quadratics", "Solutions")]
        [DataRow("latex/worksheets/algebra/quadratics", "Root")]
        [DataRow("latex/worksheets/algebra/quadratics", "WorkedSolutions")]
        [DataRow("latex/worksheets/algebra/quadratics", "Solutions")]
        // root names containing underscores must survive the split/rejoin
        [DataRow("latex/test/generated/test_document", "Root")]
        [DataRow("latex/test/generated/test_document", "WorkedSolutions")]
        [DataRow("latex/test/generated/test_document", "Solutions")]
        public void FilenameRoundTripsThroughMetadata(String rootName, String typeName)
        {
            var original = new Orchestrator.SourceMetadata
            {
                RootName = rootName,
                Type = Enum.Parse<SourceType>(typeName),
                Language = ISO639_3Code.eng,
            };

            String filename = Orchestrator.GetFilenameFromMetadata(original);

            Assert.IsTrue(filename.EndsWith(".tex"), $"expected a .tex name, got {filename}");

            var parsed = Orchestrator.ParseMetadataFromFilename(filename[..^".tex".Length]);

            Assert.AreEqual(original.RootName, parsed.RootName, "root name changed");
            Assert.AreEqual(original.Type, parsed.Type, "source type changed");
            Assert.AreEqual(original.Language, parsed.Language, "language changed");
        }

        [TestMethod]
        public void RealRepoNamesParseAsExpected()
        {
            var root = Orchestrator.ParseMetadataFromFilename(
                "latex/worksheets/arithmetic/calculationsRevision");
            Assert.AreEqual(SourceType.Root, root.Type);
            Assert.AreEqual("latex/worksheets/arithmetic/calculationsRevision", root.RootName);

            var worked = Orchestrator.ParseMetadataFromFilename(
                "latex/worksheets/arithmetic/calculationsRevision_workedSolutions");
            Assert.AreEqual(SourceType.WorkedSolutions, worked.Type);
            Assert.AreEqual("latex/worksheets/arithmetic/calculationsRevision", worked.RootName);

            var solutions = Orchestrator.ParseMetadataFromFilename(
                "latex/worksheets/arithmetic/calculationsRevision_solutions");
            Assert.AreEqual(SourceType.Solutions, solutions.Type);
            Assert.AreEqual("latex/worksheets/arithmetic/calculationsRevision", solutions.RootName);
        }

        [TestMethod]
        [DataRow("latex/worksheets/foo_abc")]
        [DataRow("latex/worksheets/worksheet_v01")]
        [DataRow("latex/worksheets/sheet_set")]
        public void UnrecognisedThreeCharSuffixIsPartOfTheRootName(String name)
        {
            // these used to throw NotImplementedException and take the service down
            var parsed = Orchestrator.ParseMetadataFromFilename(name);

            Assert.AreEqual(SourceType.Root, parsed.Type);
            Assert.AreEqual(ISO639_3Code.eng, parsed.Language);
            Assert.AreEqual(name, parsed.RootName, "the suffix should stay in the root name");
        }

        [TestMethod]
        public void UnrecognisedThreeCharSuffixIsWarnedAbout()
        {
            var logger = new RecordingLogger();

            Orchestrator.ParseMetadataFromFilename("latex/worksheets/sheet_fra", logger);

            Assert.AreEqual(1, logger.Warnings.Count(), "expected exactly one warning");

            String warning = logger.Warnings.Single();
            StringAssert.Contains(warning, "sheet_fra", "should name the offending file");
            StringAssert.Contains(warning, "fra", "should name the unrecognised suffix");
        }

        [TestMethod]
        public void OrdinaryNamesProduceNoWarnings()
        {
            var logger = new RecordingLogger();

            Orchestrator.ParseMetadataFromFilename("latex/worksheets/quadratics", logger);
            Orchestrator.ParseMetadataFromFilename("latex/worksheets/quadratics_solutions", logger);
            Orchestrator.ParseMetadataFromFilename("latex/worksheets/quadratics_workedSolutions", logger);
            Orchestrator.ParseMetadataFromFilename("latex/test/generated/test_document", logger);

            CollectionAssert.AreEqual(
                Array.Empty<String>(),
                logger.Warnings.ToArray(),
                "well-formed names must not be noisy");
        }

        [TestMethod]
        public void ParsingWithoutALoggerStillWorks()
        {
            // the logger is optional, so this must not throw
            var parsed = Orchestrator.ParseMetadataFromFilename("latex/worksheets/sheet_fra");

            Assert.AreEqual("latex/worksheets/sheet_fra", parsed.RootName);
        }

        [TestMethod]
        public void EnglishNamesCarryNoLanguageSuffix()
        {
            var metadata = new Orchestrator.SourceMetadata
            {
                RootName = "sheet",
                Type = SourceType.Root,
                Language = ISO639_3Code.eng,
            };

            Assert.AreEqual("sheet.tex", Orchestrator.GetFilenameFromMetadata(metadata));
        }
    }
}
