using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Services;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // question slides reveal their answers on a second overlay instead of in a separate
    // solutions pdf. these pin the check that a deck really does that, and what happens
    // to a deck that doesn't
    [TestClass]
    public class AnswerMacroTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

        // a substring unique to the rewrite prompt, for scripting the fake
        private const String RewriteAsk = "Rewrite it so that";

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

            // a rewrite comes back with a deck that does define the helpers
            _llm.ScriptedResponses.Add((RewriteAsk, TexFixtures.RewrittenSlideDeck()));
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        // ---- the definitions themselves ----

        [TestMethod]
        public void DefinitionsAreTheAgreedText()
        {
            // spelled out here rather than reused from the constant, so that changing the
            // constant has to be a deliberate act with this test updated alongside it
            String expected = String.Join('\n',
                "% ================================================================",
                "% Answer-overlay helpers",
                "% ================================================================",
                @"\newcommand{\ablank}[1]{%",
                @"  \alt<2>{\textcolor{red}{\underline{#1}}}{\underline{\phantom{#1}}}%",
                "}",
                @"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}",
                @"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}");

            Assert.AreEqual(expected, AnswerMacros.Definitions);
        }

        [TestMethod]
        public void ADeckDefiningTheHelpersPassesTheCheapCheck()
        {
            Assert.IsTrue(AnswerMacros.AreDefined(TexFixtures.SlideDeckDefiningAnswerMacros()));
        }

        [TestMethod]
        public void WindowsLineEndingsAreForgiven()
        {
            String crlf = TexFixtures.SlideDeckDefiningAnswerMacros().Replace("\n", "\r\n");

            Assert.IsTrue(AnswerMacros.AreDefined(crlf), "line endings are not a real difference");
        }

        [TestMethod]
        [DataRow(@"\newcommand{\ablank}[1]{%")]
        [DataRow(@"\newcommand{\ashow}[1]{\uncover<2->{\textcolor{red}{\small #1}}}")]
        [DataRow(@"\newcommand{\ashowq}[1]{\alt<2->{\textcolor{red}{\small #1}}{?}}")]
        public void ADeckMissingAnyOneHelperFailsTheCheapCheck(String helperStart)
        {
            String deck = TexFixtures.SlideDeckDefiningAnswerMacros();

            String withoutIt = String.Join('\n',
                deck.Split('\n').Where(l => !l.StartsWith(helperStart, StringComparison.Ordinal)));

            Assert.IsFalse(AnswerMacros.AreDefined(withoutIt));
        }

        [TestMethod]
        public void ARetypedHelperFailsTheCheapCheck()
        {
            // being wrong this way round only costs a rewrite, the other way ships a deck
            // whose overlays don't behave as the rest of the pipeline assumes
            String altered = TexFixtures.SlideDeckDefiningAnswerMacros()
                .Replace(@"\textcolor{red}{\small #1}", @"\textcolor{blue}{\small #1}");

            Assert.IsFalse(AnswerMacros.AreDefined(altered));
        }

        [TestMethod]
        public void ADeckWithNoHelpersAtAllFailsTheCheapCheck()
        {
            Assert.IsFalse(AnswerMacros.AreDefined(TexFixtures.SlideDeckWithoutAnswerMacros()));
        }

        // ---- the marker on the worked solutions ----

        [TestMethod]
        public void MarkingIsIdempotentAndLeavesValidTex()
        {
            String once = AnswerMacros.AddVerifiedMarker(TexFixtures.WorkedSolutions());
            String twice = AnswerMacros.AddVerifiedMarker(once);

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(once));
            Assert.AreEqual(once, twice, "marking an already marked file must change nothing");
            Assert.IsTrue(SourceGenerator.IsValidTex(once), $"marker broke the tex:\n{once}");
            Assert.IsTrue(once.StartsWith("%"), "the marker goes first so an append can't lose it");
        }

        [TestMethod]
        public void UnmarkedWorkedSolutionsAreRecognisedAsSuch()
        {
            Assert.IsFalse(AnswerMacros.IsMarkedVerified(TexFixtures.WorkedSolutions()));
        }

        [TestMethod]
        public void TheMarkerIsNotMatchedInsideASentence()
        {
            // it is a whole line or it is nothing
            String prose = TexFixtures.WorkedSolutions(
                "We note that the Root file used ashow macros here and there.");

            Assert.IsFalse(AnswerMacros.IsMarkedVerified(prose));
        }

        // ---- deciding whether a deck needs fixing ----

        [TestMethod]
        public async Task ADeckWithoutTheHelpersIsRewrittenBeforeAnythingIsDerived()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(
                new[] { Deck },
                _git.LastCommit.Select(NameOf).ToArray(),
                "the deck itself is what gets written, not its worked solutions");

            Assert.IsTrue(AnswerMacros.AreDefined(_git.Contents[Deck]));
        }

        [TestMethod]
        public async Task TheCheapCheckSettlesABareDeckWithoutAskingTheModel()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(0, _llm.YesNoCallCount, "no definitions means there is nothing to ask about");
        }

        [TestMethod]
        public async Task ARewrittenDeckThenGetsItsWorkedSolutions()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEquivalent(new[] { Deck, DeckWorked }, _git.Files.Keys.ToArray());
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        [TestMethod]
        public async Task ADeckThatAlreadyUsesTheHelpersIsLeftAlone()
        {
            String original = TexFixtures.SlideDeckDefiningAnswerMacros();
            _git.AddFile(Deck, ageCommits: 1, contents: original);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(new[] { DeckWorked }, _git.LastCommit.Select(NameOf).ToArray());
            Assert.AreEqual(original, _git.Contents[Deck], "the deck was fine, so it must not be touched");
            Assert.AreEqual(1, _llm.YesNoCallCount);
        }

        [TestMethod]
        public async Task HelpersDefinedButNotUsedStillTriggersARewrite()
        {
            // the definitions are necessary, not sufficient - this is what the model is for
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _llm.DefaultYesNo = false;

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(new[] { Deck }, _git.LastCommit.Select(NameOf).ToArray());
            StringAssert.Contains(_llm.PromptsSeen.Single(), RewriteAsk);
        }

        [TestMethod]
        public async Task AnIndeterminateAnswerChangesNothing()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _llm.DefaultYesNo = null;

            Assert.AreEqual(Orchestrator.PassOutcome.GenerationFailed, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _git.CommitCalls.Count, "a shrug must not be read as either answer");
            Assert.AreEqual(3, _llm.YesNoCallCount, "the model gets a few goes before we give up");
        }

        [TestMethod]
        public async Task ARewriteThatStillLacksTheHelpersIsRefused()
        {
            // otherwise it lands in the repo and the next pass asks for the same rewrite
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            _llm.ScriptedResponses.Clear();
            _llm.ScriptedResponses.Add((RewriteAsk, FakeLLMService.ValidTex("still no helpers here")));

            Assert.AreEqual(Orchestrator.PassOutcome.GenerationFailed, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _git.CommitCalls.Count);
        }

        // ---- worked solutions that carry no record of a check ----

        [TestMethod]
        public async Task UnmarkedWorkedSolutionsAreStampedRatherThanRegenerated()
        {
            String handWritten = TexFixtures.WorkedSolutions("a person wrote these");

            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: handWritten);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(new[] { DeckWorked }, _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(0, _llm.CallCount, "regenerating them would be paying twice for the same content");
            Assert.AreEqual(1, _llm.YesNoCallCount);

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
            StringAssert.Contains(_git.Contents[DeckWorked], "a person wrote these", "the content must survive");

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        [TestMethod]
        public async Task MarkedWorkedSolutionsAreNotCheckedAgain()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: TexFixtures.VerifiedWorkedSolutions());

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _llm.CallCount);
            Assert.AreEqual(0, _llm.YesNoCallCount, "the marker is there so we must not pay to ask again");
        }

        [TestMethod]
        public async Task UnmarkedWorkedSolutionsOverABadDeckFixTheDeckFirst()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckWithoutAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: TexFixtures.WorkedSolutions());

            // the deck is rewritten, which makes the worked solutions stale, so they go and
            // come back marked
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            CollectionAssert.AreEqual(new[] { Deck }, _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        [TestMethod]
        public async Task StaleWorkedSolutionsBringTheCheckBack()
        {
            // this is the case where a person edits the deck and takes the helpers back out:
            // the worked solutions go stale, are removed, and the check comes back with them
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 5, contents: TexFixtures.VerifiedWorkedSolutions());

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _llm.YesNoCallCount, "removal happens before anything is asked");

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(1, _llm.YesNoCallCount, "a fresh set of worked solutions means a fresh check");
        }

        // ---- other archetypes are untouched by any of this ----

        [TestMethod]
        public async Task WorksheetsAreNeverCheckedForAnswerMacros()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(0, _llm.YesNoCallCount);
            Assert.IsFalse(
                AnswerMacros.IsMarkedVerified(_git.Contents["latex/worksheets/quadratics_workedSolutions.tex"]),
                "the marker is a statement about a slide deck, not a worksheet");
        }

        // ---- reading a verdict out of a model that likes to talk ----

        [TestMethod]
        [DataRow("YES", true)]
        [DataRow("yes", true)]
        [DataRow("Yes.", true)]
        [DataRow("**YES**", true)]
        [DataRow("  \nYES\n", true)]
        [DataRow("NO", false)]
        [DataRow("no.", false)]
        [DataRow("- No", false)]
        public void AVerdictIsReadOutOfTheFirstWord(String response, bool expected)
        {
            Assert.AreEqual(expected, LLMService.ParseYesNo(response));
        }

        [TestMethod]
        [DataRow("maybe")]
        [DataRow("I think so")]
        [DataRow("")]
        [DataRow("1234")]
        public void AnythingElseIsNotAVerdict(String response)
        {
            Assert.IsNull(LLMService.ParseYesNo(response), "a non answer must not be read as no");
        }
    }
}
