using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Services;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the helpers themselves, the markers that record what has been done to a deck, and what
    // each prompt is told. the bounded review loop that uses all this lives in
    // AnswerMacroReviewTests
    [TestClass]
    public class AnswerMacroTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

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

        [TestMethod]
        public async Task TheCheapCheckCostsNothingBeforeTheFirstFix()
        {
            // a deck with no definitions is settled without asking anyone, so the only review
            // is the one that looks at what the fixer produced
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.ReviewCallCount, "round 0 was settled for free");
            StringAssert.Contains(_llm.QuestionsSeen.Single(), TexFixtures.DefaultBody,
                "the one review that ran looked at the rewritten deck");
        }

        // ---- the markers ----

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

        [TestMethod]
        public void AReviewNoteIsRecognisedByItsMarkerComment()
        {
            Assert.IsTrue(AnswerMacros.HasReviewNote(TexFixtures.SlideDeckWithReviewNote()));
            Assert.IsFalse(AnswerMacros.HasReviewNote(TexFixtures.SlideDeckDefiningAnswerMacros()));
        }

        // ---- worked solutions that carry no record of a review ----

        [TestMethod]
        public async Task UnmarkedWorkedSolutionsAreStampedRatherThanRegenerated()
        {
            String handWritten = TexFixtures.WorkedSolutions("a person wrote these");

            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1, contents: handWritten);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            CollectionAssert.AreEqual(new[] { DeckWorked }, _git.LastCommit.Select(NameOf).ToArray());

            Assert.AreEqual(0, _llm.CallCount, "regenerating them would be paying twice for the same content");
            Assert.AreEqual(1, _llm.ReviewCallCount);

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
            Assert.AreEqual(0, _llm.ReviewCallCount, "the marker is there so we must not pay to ask again");
        }

        [TestMethod]
        public async Task StaleWorkedSolutionsBringTheReviewBack()
        {
            // this is the case where a person edits the deck and takes the helpers back out:
            // the worked solutions go stale, are removed, and the review comes back with them
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 5, contents: TexFixtures.VerifiedWorkedSolutions());

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(0, _llm.ReviewCallCount, "removal happens before anything is asked");

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(1, _llm.ReviewCallCount, "a fresh set of worked solutions means a fresh review");
        }

        [TestMethod]
        public async Task AnIndeterminateVerdictChangesNothing()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _llm.DefaultVerdict = null;

            Assert.AreEqual(Orchestrator.PassOutcome.GenerationFailed, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _git.CommitCalls.Count, "a shrug must not be read as either verdict");
            Assert.AreEqual(3, _llm.ReviewCallCount, "the model gets a few goes before we give up");
        }

        // ---- other archetypes are untouched by any of this ----

        [TestMethod]
        public async Task WorksheetsAreNeverReviewedForAnswerMacros()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(0, _llm.ReviewCallCount);
            Assert.IsFalse(
                AnswerMacros.IsMarkedVerified(_git.Contents["latex/worksheets/quadratics_workedSolutions.tex"]),
                "the marker is a statement about a slide deck, not a worksheet");
        }

        // ---- what each prompt is told ----

        private const String MathModeRule = @"\ablank{$3x^2$}";

        [TestMethod]
        public async Task TheRewriteIsToldToPutMathematicalAnswersInMathMode()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithoutAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.First(p => p.Contains(RewriteAsk)), MathModeRule);
        }

        [TestMethod]
        public async Task TheWorkedSolutionsPromptIsToldTheSameRules()
        {
            // these reproduce the questions with the helpers, so they can hit the same bug
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.PromptsSeen.Single(), MathModeRule);
        }

        [TestMethod]
        public async Task AWorksheetPromptIsNotBurdenedWithSlideRules()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);

            await _orchestrator.DoOnePassAsync();

            Assert.IsFalse(_llm.PromptsSeen.Single().Contains(MathModeRule, StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task TheReviewOnlyFailsAnAnswerThatWouldNotCompile()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.QuestionsSeen.Single(), "would not compile");
        }

        // every one of these was a way the review could reject a deck that was fine, and each
        // false rejection used to cost a full rewrite
        [TestMethod]
        [DataRow("commented out")]
        [DataRow("Only questions count")]
        [DataRow("revealed by a drawing")]
        [DataRow("several parts at once")]
        [DataRow("inside existing math mode")]
        [DataRow("Numbering questions by hand")]
        [DataRow("style, wording, spacing, layout")]
        public async Task TheReviewIsToldNotToRejectForThis(String exclusion)
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros());

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_llm.QuestionsSeen.Single(), exclusion);
        }

        // ---- splicing the note in ----

        private const String Body = "What is $2 + 2$? \ashow{$4$}";

        private static String DeckWithNote(String summary = "the review was not happy", bool atStart = false) =>
            AnswerMacros.AddReviewNote(TexFixtures.SlideDeckDefiningAnswerMacros(Body), summary, atStart)!;

        [TestMethod]
        public void ANoteAtTheEndSitsAfterTheQuestionsAndInsideTheDocument()
        {
            String deck = DeckWithNote();

            int note = deck.IndexOf(AnswerMacros.ReviewNoteMarker, StringComparison.Ordinal);

            Assert.IsTrue(note > deck.IndexOf(Body, StringComparison.Ordinal), "after the questions");
            Assert.IsTrue(note < deck.IndexOf(@"\end{document}", StringComparison.Ordinal), "but still inside the body");
        }

        [TestMethod]
        public void ANoteAtTheStartSitsBeforeTheQuestionsAndInsideTheDocument()
        {
            String deck = DeckWithNote(atStart: true);

            int note = deck.IndexOf(AnswerMacros.ReviewNoteMarker, StringComparison.Ordinal);

            Assert.IsTrue(note > deck.IndexOf(@"\begin{document}", StringComparison.Ordinal), "inside the body");
            Assert.IsTrue(note < deck.IndexOf(Body, StringComparison.Ordinal), "before the questions");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SplicingANoteLosesNothing(bool atStart)
        {
            String original = TexFixtures.SlideDeckDefiningAnswerMacros(Body);
            String deck = AnswerMacros.AddReviewNote(original, "a summary", atStart)!;

            StringAssert.Contains(deck, Body, "the questions are untouched");
            Assert.IsTrue(AnswerMacros.AreDefined(deck), "so is the preamble");
            Assert.IsTrue(SourceGenerator.IsValidTex(deck), $"the deck must still be valid:\n{deck}");
            Assert.IsTrue(deck.Length > original.Length);
        }

        [TestMethod]
        public void TheSummaryReachesTheSlide()
        {
            StringAssert.Contains(DeckWithNote("SOMETHING THE TEACHER SHOULD READ"), "SOMETHING THE TEACHER SHOULD READ");
        }

        [TestMethod]
        public void LatexSpecialsInTheSummaryAreNeutralised()
        {
            // a stray % would comment out the rest of the line, and an & look like a cell
            String deck = DeckWithNote(@"100% of & _ #$ ^~ {x} \y");

            Assert.IsTrue(SourceGenerator.IsValidTex(deck), $"specials broke the deck:\n{deck}");
            StringAssert.Contains(deck, @"100\% of \& \_ \#\$");
            StringAssert.Contains(deck, @"\textbackslash{}y");
            StringAssert.Contains(deck, @"\{x\}");
        }

        [TestMethod]
        public void SplicingANoteTwiceChangesNothingTheSecondTime()
        {
            String once = DeckWithNote();
            String twice = AnswerMacros.AddReviewNote(once, "a different summary", atStart: false)!;

            Assert.AreEqual(once, twice, "a deck that already carries a note must be left alone");
        }

        [TestMethod]
        public void ADeckWithNoDocumentBodyCannotBeNoted()
        {
            Assert.IsNull(AnswerMacros.AddReviewNote(@"\documentclass{beamer}", "a summary", atStart: false));
            Assert.IsNull(AnswerMacros.AddReviewNote(@"\documentclass{beamer}", "a summary", atStart: true));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TheDecksOwnLineEndingsAreKept(bool atStart)
        {
            String crlf = TexFixtures.SlideDeckDefiningAnswerMacros(Body).Replace("\n", "\r\n");

            String deck = AnswerMacros.AddReviewNote(crlf, "a summary\nover two lines", atStart)!;

            for (int i = 0; i < deck.Length; i++)
            {
                if (deck[i] == '\n')
                {
                    Assert.IsTrue(i > 0 && deck[i - 1] == '\r', $"a bare newline crept in at {i}");
                }
            }
        }

        // ---- reading a verdict out of a model that likes to talk ----

        [TestMethod]
        [DataRow("PASS", true)]
        [DataRow("pass", true)]
        [DataRow("Pass.", true)]
        [DataRow("**PASS**", true)]
        [DataRow("  \nYES\n", true)]
        [DataRow("FAIL", false)]
        [DataRow("fail.", false)]
        [DataRow("- NO", false)]
        public void AVerdictIsReadOutOfTheFirstWord(String response, bool expected)
        {
            Assert.AreEqual(expected, LLMService.ParseVerdict(response));
        }

        [TestMethod]
        [DataRow("maybe")]
        [DataRow("I think so")]
        [DataRow("")]
        [DataRow("1234")]
        public void AnythingElseIsNotAVerdict(String response)
        {
            Assert.IsNull(LLMService.ParseVerdict(response), "a non answer must not be read as a failure");
        }

        [TestMethod]
        public void TheReasonsAreEverythingAfterTheVerdict()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("FAIL\nquestion 2 has no answer\nquestion 5 either");

            Assert.IsNotNull(verdict);
            Assert.IsFalse(verdict!.Passed);
            StringAssert.Contains(verdict.Reasons, "question 2 has no answer");
            StringAssert.Contains(verdict.Reasons, "question 5 either");
        }

        [TestMethod]
        public void APassCarriesNoReasons()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("PASS\nlooks good to me");

            Assert.IsNotNull(verdict);
            Assert.IsTrue(verdict!.Passed);
            Assert.AreEqual(String.Empty, verdict.Reasons);
        }

        [TestMethod]
        public void ABareFailStillCountsAsAVerdict()
        {
            ReviewVerdict? verdict = LLMService.ParseReview("FAIL");

            Assert.IsNotNull(verdict);
            Assert.IsFalse(verdict!.Passed);
            Assert.AreNotEqual(String.Empty, verdict.Reasons, "the log needs something to show");
        }
    }
}
