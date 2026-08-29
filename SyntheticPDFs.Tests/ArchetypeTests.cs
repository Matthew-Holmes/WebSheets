using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the archetype is read off the folder a source lives in, and decides which derived
    // files the pipeline is allowed to make for it
    [TestClass]
    public class ArchetypeTests
    {
        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private Orchestrator _orchestrator = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
            _orchestrator = new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        // a distinctive phrase from QuestionSlidesWorkedSolutionRequirements
        private const String SlideOnlyInstruction = @"\beamergotobutton";

        // ---- parsing ----

        // SourceArchetype is internal, so it cannot be a parameter on a public test method
        [TestMethod]
        [DataRow("latex/worksheets/quadratics", "Worksheet")]
        [DataRow("latex/worksheets/algebra/KS4/quadratics", "Worksheet")]
        [DataRow("latex/starters/circlesArea", "QuestionSlides")]
        [DataRow("latex/starters/targeted/KS3/circles/circlesAreaIdeasStarters", "QuestionSlides")]
        [DataRow("latex/cheatSheets/trigIdentities", "Poster")]
        public void ArchetypeComesFromTheFolder(String path, String expected)
        {
            var parsed = Orchestrator.ParseMetadataFromFilename(path);

            Assert.AreEqual(Enum.Parse<SourceArchetype>(expected), parsed.Archetype);
        }

        [TestMethod]
        [DataRow("latex/starters/KS3/circlesArea_workedSolutions")]
        [DataRow("latex/starters/KS3/circlesArea_solutions")]
        public void DerivedFilesKeepTheArchetypeOfTheirRoot(String path)
        {
            // the suffix decides the type, the folder still decides the archetype
            var parsed = Orchestrator.ParseMetadataFromFilename(path);

            Assert.AreEqual(SourceArchetype.QuestionSlides, parsed.Archetype);
            Assert.AreEqual("latex/starters/KS3/circlesArea", parsed.RootName);
        }

        [TestMethod]
        public void UnknownFolderFallsBackToWorksheetAndWarns()
        {
            var logger = new RecordingLogger();

            var parsed = Orchestrator.ParseMetadataFromFilename("latex/somethingNew/sheet", logger);

            Assert.AreEqual(SourceArchetype.Worksheet, parsed.Archetype);
            StringAssert.Contains(logger.Warnings.Single(), "somethingNew");
        }

        [TestMethod]
        public void PathWithNoArchetypeFolderFallsBackToWorksheetAndWarns()
        {
            // latex/atype/name is the shortest path that carries an archetype
            var logger = new RecordingLogger();

            var parsed = Orchestrator.ParseMetadataFromFilename("latex/looseSheet", logger);

            Assert.AreEqual(SourceArchetype.Worksheet, parsed.Archetype);
            StringAssert.Contains(logger.Warnings.Single(), "looseSheet");
        }

        [TestMethod]
        public void PathOutsideTheSourceDirectoryIsWarnedAboutRatherThanFatal()
        {
            // one odd path must not take a whole pass down with it
            var logger = new RecordingLogger();

            var parsed = Orchestrator.ParseMetadataFromFilename("elsewhere/worksheets/sheet", logger);

            Assert.AreEqual(SourceArchetype.Worksheet, parsed.Archetype);
            Assert.AreEqual("elsewhere/worksheets/sheet", parsed.RootName);
            StringAssert.Contains(logger.Warnings.Single(), "elsewhere/worksheets/sheet");
        }

        // ---- what each archetype is allowed to generate ----

        [TestMethod]
        public async Task WorksheetsGetWorkedSolutionsThenAnAnswerKey()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { "latex/worksheets/quadratics_workedSolutions.tex" },
                _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { "latex/worksheets/quadratics_solutions.tex" },
                _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task SlidesGetWorkedSolutionsButNeverAnAnswerKey()
        {
            // the answers belong in the deck itself, so a separate key would be redundant
            _git.AddFile("latex/starters/KS3/circlesArea.tex", ageCommits: 1,
                contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { "latex/starters/KS3/circlesArea_workedSolutions.tex" },
                _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            CollectionAssert.DoesNotContain(
                _git.Files.Keys.ToArray(),
                "latex/starters/KS3/circlesArea_solutions.tex");
        }

        [TestMethod]
        public async Task PostersGetNothingGeneratedAtAll()
        {
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _llm.CallCount, "a poster needs no derived source");
            Assert.AreEqual(0, _git.CommitCalls.Count);
        }

        [TestMethod]
        public async Task StalePosterDerivativesAreRemovedAndNotRebuilt()
        {
            // left over from before posters stopped getting worked solutions
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);
            _git.AddFile("latex/cheatSheets/trigIdentities_workedSolutions.tex", ageCommits: 5);

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(
                new[] { "latex/cheatSheets/trigIdentities.tex" },
                _git.Files.Keys.ToArray());
        }

        [TestMethod]
        public async Task EachArchetypeFollowsItsOwnRulesInOnePass()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);
            _git.AddFile("latex/starters/circlesArea.tex", ageCommits: 1,
                contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "latex/worksheets/quadratics_workedSolutions.tex",
                    "latex/starters/circlesArea_workedSolutions.tex",
                },
                _git.LastCommit.Select(NameOf).ToArray());

            // only the worksheet still owes an answer key
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(
                new[] { "latex/worksheets/quadratics_solutions.tex" },
                _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        // ---- archetype specific prompting ----

        [TestMethod]
        public async Task SlidesPromptAsksForOneSolutionPerSlide()
        {
            _git.AddFile("latex/starters/KS3/circlesArea.tex", ageCommits: 1,
                contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.CallCount);
            StringAssert.Contains(
                _llm.PromptsSeen[0],
                SlideOnlyInstruction,
                "a deck of questions needs the slide specific instructions");
        }

        [TestMethod]
        public async Task WorksheetPromptDoesNotAskForSlides()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.CallCount);
            Assert.IsFalse(
                _llm.PromptsSeen[0].Contains(SlideOnlyInstruction, StringComparison.Ordinal),
                "a worksheet must not be asked for a contents slide");
        }

        [TestMethod]
        public async Task UnclassifiedFolderIsPromptedAsAWorksheet()
        {
            // unknown folders fall back to Worksheet, so they must prompt like one
            _git.AddFile("latex/somethingNew/sheet.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.CallCount);
            Assert.IsFalse(
                _llm.PromptsSeen[0].Contains(SlideOnlyInstruction, StringComparison.Ordinal));
        }
    }
}
