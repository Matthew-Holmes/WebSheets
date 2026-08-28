using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class OrchestratorPassTests
    {
        private const String Root = "latex/worksheets/algebra/quadratics.tex";
        private const String Worked = "latex/worksheets/algebra/quadratics_workedSolutions.tex";
        private const String Answers = "latex/worksheets/algebra/quadratics_solutions.tex";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private Orchestrator _orchestrator = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
            _orchestrator = Build(maxFilesPerRun: 30);
        }

        private Orchestrator Build(int maxFilesPerRun) => new Orchestrator(
            NullLogger<Orchestrator>.Instance,
            _git,
            _llm,
            Options.Create(new GenerationOptions { MaxFilesPerRun = maxFilesPerRun }));

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        [TestMethod]
        public async Task EmptyRepoHasNothingToDo()
        {
            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, outcome);
            Assert.AreEqual(0, _git.CommitCalls.Count);
            Assert.AreEqual(0, _llm.CallCount);
        }

        [TestMethod]
        public async Task RootOnlyGeneratesWorkedSolutions()
        {
            _git.AddFile(Root, ageCommits: 1);

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, outcome);
            Assert.AreEqual(1, _llm.CallCount, "should have asked the LLM exactly once");

            CollectionAssert.AreEqual(
                new[] { Worked },
                _git.LastCommit.Select(NameOf).ToArray());
        }

        [TestMethod]
        public async Task RootAndWorkedSolutionsGeneratesTheAnswerKey()
        {
            _git.AddFile(Root, ageCommits: 2);
            _git.AddFile(Worked, ageCommits: 1);

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, outcome);

            CollectionAssert.AreEqual(
                new[] { Answers },
                _git.LastCommit.Select(NameOf).ToArray());
        }

        [TestMethod]
        public async Task CompleteWorksheetNeedsNothing()
        {
            _git.AddFile(Root, ageCommits: 3);
            _git.AddFile(Worked, ageCommits: 2);
            _git.AddFile(Answers, ageCommits: 1);

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task SuccessivePassesFillInTheWholeWorksheet()
        {
            _git.AddFile(Root, ageCommits: 1);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEquivalent(
                new[] { Root, Worked, Answers },
                _git.Files.Keys.ToArray());
        }

        [TestMethod]
        public async Task StaleFilesAreRemovedAndNothingIsGenerated()
        {
            // worked solutions older than the root, so they no longer match it
            _git.AddFile(Root, ageCommits: 1);
            _git.AddFile(Worked, ageCommits: 5);

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, outcome);
            Assert.AreEqual(1, _git.RemoveFilesCalls.Count);
            CollectionAssert.AreEqual(new[] { Worked }, _git.RemoveFilesCalls[0].ToArray());

            Assert.AreEqual(0, _git.CommitCalls.Count, "must not generate in the same pass as a removal");
            Assert.AreEqual(0, _llm.CallCount);
        }

        [TestMethod]
        public async Task OrphanedSolutionsAreRemoved()
        {
            _git.AddFile(Answers, ageCommits: 1);

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, outcome);
            CollectionAssert.AreEqual(new[] { Answers }, _git.RemoveFilesCalls[0].ToArray());
        }

        [TestMethod]
        public async Task HashMismatchOnRemovalReportsAConflict()
        {
            _git.AddFile(Root, ageCommits: 1);
            _git.AddFile(Worked, ageCommits: 5);
            _git.SimulateConflict = true;

            Assert.AreEqual(Orchestrator.PassOutcome.GitConflict, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task HashMismatchOnPushReportsAConflict()
        {
            _git.AddFile(Root, ageCommits: 1);
            _git.SimulateConflict = true;

            Assert.AreEqual(Orchestrator.PassOutcome.GitConflict, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task OneFailedFileDoesNotCostTheRestOfTheBatch()
        {
            _git.AddFile("latex/worksheets/a.tex", ageCommits: 1);
            _git.AddFile("latex/worksheets/b.tex", ageCommits: 1);
            _git.AddFile("latex/worksheets/c.tex", ageCommits: 1);

            // the prompt embeds the source, so tag b's contents to target it
            _git.Contents["latex/worksheets/b.tex"] = "\\documentclass{article} % POISON";
            _llm.ScriptedResponses.Add(("POISON", "not tex at all, just prose"));

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, outcome);

            var committed = _git.LastCommit.Select(NameOf).ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "latex/worksheets/a_workedSolutions.tex", "latex/worksheets/c_workedSolutions.tex" },
                committed);
            CollectionAssert.DoesNotContain(committed, "latex/worksheets/b_workedSolutions.tex");
        }

        [TestMethod]
        public async Task BatchFailingEntirelyCommitsNothing()
        {
            _git.AddFile(Root, ageCommits: 1);
            _llm.DefaultResponse = "not tex at all, just prose";

            var outcome = await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(Orchestrator.PassOutcome.GenerationFailed, outcome);
            Assert.AreEqual(0, _git.CommitCalls.Count, "nothing should be pushed");
        }

        [TestMethod]
        public async Task BatchSizeIsRespected()
        {
            for (int i = 0; i < 10; i++)
            {
                _git.AddFile($"latex/worksheets/sheet{i}.tex", ageCommits: 1);
            }

            var orchestrator = Build(maxFilesPerRun: 4);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());
            Assert.AreEqual(4, _git.LastCommit.Count);
        }

        [TestMethod]
        public async Task EachRootAdvancesAtMostOneStepPerPass()
        {
            _git.AddFile("latex/worksheets/a.tex", ageCommits: 1);
            _git.AddFile("latex/worksheets/b.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            // two roots, so two files - never the answer keys as well in the same pass
            Assert.AreEqual(2, _git.LastCommit.Count);
            CollectionAssert.AreEquivalent(
                new[] { "latex/worksheets/a_workedSolutions.tex", "latex/worksheets/b_workedSolutions.tex" },
                _git.LastCommit.Select(NameOf).ToArray());
        }

        [TestMethod]
        public async Task GeneratedSolutionsAreDerivedFromTheWorkedSolutions()
        {
            _git.AddFile(Root, ageCommits: 2, contents: "\\documentclass{article} % THE QUESTIONS");
            _git.AddFile(Worked, ageCommits: 1, contents: "\\documentclass{article} % THE WORKINGS");

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.CallCount);
            StringAssert.Contains(
                _llm.PromptsSeen[0],
                "THE WORKINGS",
                "the answer-key prompt should be built from the worked solutions");
        }
    }
}
