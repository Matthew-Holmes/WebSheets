using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class NamingConventionTests
    {
        // SourceType and SourceArchetype are internal, so they can't be parameters on a public test method
        [TestMethod]
        [DataRow("quadratics", "Root", "Worksheet")]
        [DataRow("quadratics", "WorkedSolutions", "Worksheet")]
        [DataRow("quadratics", "Solutions", "Worksheet")]
        [DataRow("latex/worksheets/algebra/quadratics", "Root", "Worksheet")]
        [DataRow("latex/worksheets/algebra/quadratics", "WorkedSolutions", "Worksheet")]
        [DataRow("latex/worksheets/algebra/quadratics", "Solutions", "Worksheet")]
        [DataRow("latex/starters/targeted/KS3/circles/circlesArea", "Root", "QuestionSlides")]
        [DataRow("latex/starters/targeted/KS3/circles/circlesArea", "WorkedSolutions", "QuestionSlides")]
        [DataRow("latex/cheatSheets/trigIdentities", "Root", "Poster")]
        // root names containing underscores must survive the split/rejoin
        [DataRow("latex/worksheets/generated/test_document", "Root", "Worksheet")]
        [DataRow("latex/worksheets/generated/test_document", "WorkedSolutions", "Worksheet")]
        [DataRow("latex/worksheets/generated/test_document", "Solutions", "Worksheet")]
        public void FilenameRoundTripsThroughMetadata(String rootName, String typeName, String archetypeName)
        {
            var original = new Orchestrator.SourceMetadata
            {
                RootName = rootName,
                Type = Enum.Parse<SourceType>(typeName),
                Archetype = Enum.Parse<SourceArchetype>(archetypeName),
                Language = ISO639_3Code.eng,
            };

            String filename = Orchestrator.GetFilenameFromMetadata(original);

            Assert.IsTrue(filename.EndsWith(".tex"), $"expected a .tex name, got {filename}");

            var parsed = Orchestrator.ParseMetadataFromFilename(filename[..^".tex".Length]);

            Assert.AreEqual(original.RootName, parsed.RootName, "root name changed");
            Assert.AreEqual(original.Type, parsed.Type, "source type changed");
            Assert.AreEqual(original.Language, parsed.Language, "language changed");

            // the archetype is carried by the folder, not the filename, so it has to
            // survive a round trip that never writes it down
            Assert.AreEqual(original.Archetype, parsed.Archetype, "archetype changed");
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
            Orchestrator.ParseMetadataFromFilename("latex/starters/KS3/circlesArea", logger);
            Orchestrator.ParseMetadataFromFilename("latex/cheatSheets/trigIdentities", logger);
            Orchestrator.ParseMetadataFromFilename("latex/worksheets/generated/test_document", logger);

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
                Archetype = SourceArchetype.Worksheet,
                Language = ISO639_3Code.eng,
            };

            Assert.AreEqual("sheet.tex", Orchestrator.GetFilenameFromMetadata(metadata));
        }
    }
}
