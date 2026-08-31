using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the review and the fixer are two models that used to be able to disagree forever. these
    // pin the bounded loop that replaced that: at most two rounds of fixing, and every path
    // ends with the worked solutions marked, so a deck is never reviewed twice over the same
    // bytes. where they never agree, a person gets told on a slide
    [TestClass]
    public class AnswerMacroReviewTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

        private const String RewriteAsk = "Rewrite it so that";

        // bodies that make each round's deck tell itself apart in a scripted prompt
        private const String BodyOne = "ROUND ONE BODY \\ashow{$1$}";
        private const String BodyTwo = "ROUND TWO BODY \\ashow{$2$}";
        private const String BodyThree = "ROUND THREE BODY \\ashow{$3$}";

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

        private void AddDeck(String body) =>
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckDefiningAnswerMacros(body));

        private String[] Committed() => _git.LastCommit.Select(NameOf).ToArray();

        // where the note landed relative to the questions it is about
        private static bool NoteComesBefore(String deck, String body) =>
            deck.IndexOf(AnswerMacros.ReviewNoteMarker, StringComparison.Ordinal)
            < deck.IndexOf(body, StringComparison.Ordinal);

        // ---- the happy path ----

        [TestMethod]
        public async Task ADeckThePassesFirstTimeIsNeverHandedToTheFixer()
        {
            AddDeck(BodyOne);
            _llm.DefaultVerdict = FakeLLMService.Pass;

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(1, _llm.ReviewCallCount);
            CollectionAssert.AreEqual(new[] { DeckWorked }, Committed());
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
            Assert.IsFalse(_llm.PromptsSeen.Any(p => p.Contains(RewriteAsk)), "nothing to fix");
        }

        [TestMethod]
        public async Task ADeckThatPassesAfterOneFixIsCommittedWithItsWorkedSolutions()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyTwo));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail());
            _llm.ReviewSequence.Enqueue(FakeLLMService.Pass);

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            // the deck and what was derived from it land in the same commit
            CollectionAssert.AreEquivalent(new[] { Deck, DeckWorked }, Committed());
            StringAssert.Contains(_git.Contents[Deck], BodyTwo);
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
            Assert.IsFalse(AnswerMacros.HasReviewNote(_git.Contents[Deck]), "it was settled, so no note");
        }

        // ---- case 1: the fixer changes nothing, so they fundamentally disagree ----

        [TestMethod]
        public async Task WhenTheFixerChangesNothingTheDeckIsNotedAtTheEnd()
        {
            AddDeck(BodyOne);

            // the fixer hands back exactly what it was given
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail("question 3 has no answer");

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(2, _llm.ReviewCallCount, "one round of fixing, then stop");
            Assert.IsTrue(AnswerMacros.HasReviewNote(_git.Contents[Deck]));
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]),
                "the marker still goes on, so the deck is never reviewed again over the same bytes");
        }

        [TestMethod]
        public async Task AFixerThatProducesNothingUsableCountsAsChangingNothing()
        {
            AddDeck(BodyOne);

            // a rewrite that drops the helper definitions is refused, so the round yields nothing
            _llm.When(p => p.Contains(RewriteAsk), FakeLLMService.ValidTex("no helpers in here"));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.IsTrue(AnswerMacros.HasReviewNote(_git.Contents[Deck]));
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        // ---- case 2: the fixer keeps changing things ----

        [TestMethod]
        public async Task WhenTheFixerStopsChangingOnTheSecondRoundTheNoteGoesAtTheEnd()
        {
            AddDeck(BodyOne);

            // round one moves the deck on, round two hands the same thing straight back
            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyOne),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyTwo));
            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyTwo),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyTwo));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(3, _llm.ReviewCallCount, "two rounds of fixing, then stop");

            String deck = _git.Contents[Deck];
            StringAssert.Contains(deck, BodyTwo, "the deck the fixer settled on is kept");
            Assert.IsTrue(AnswerMacros.HasReviewNote(deck));
            Assert.IsFalse(NoteComesBefore(deck, BodyTwo), "a mere deadlock is noted at the end");
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        [TestMethod]
        public async Task ADeckStillChangingAfterTwoRoundsIsNotedAtTheStart()
        {
            AddDeck(BodyOne);

            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyOne),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyTwo));
            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyTwo),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyThree));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(3, _llm.ReviewCallCount, "the loop stops after two rounds either way");

            String deck = _git.Contents[Deck];
            StringAssert.Contains(deck, BodyThree, "the latest deck is the one kept");
            Assert.IsTrue(AnswerMacros.HasReviewNote(deck));

            // this one is potentially more fundamental, so it is said before the lesson starts
            Assert.IsTrue(NoteComesBefore(deck, BodyThree));
        }

        [TestMethod]
        public async Task TheNoteGoesAtTheEndWhenTheyMerelyDeadlock()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            await _orchestrator.DoOnePassAsync();

            Assert.IsFalse(NoteComesBefore(_git.Contents[Deck], BodyOne));
        }

        // ---- what the note is built from ----

        [TestMethod]
        public async Task EveryRoundsReasonsReachTheSummariser()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("FIRST REASON"));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("SECOND REASON"));

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(1, _llm.SummaryCallCount);
            String prompt = _llm.SummaryPromptsSeen.Single();
            StringAssert.Contains(prompt, "FIRST REASON");
            StringAssert.Contains(prompt, "SECOND REASON", "the older reasons are kept alongside the new");
        }

        [TestMethod]
        public async Task TheSummaryIsWhatEndsUpOnTheSlide()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail();
            _llm.SummaryResponse = "THE SUMMARY FOR THE TEACHER";

            await _orchestrator.DoOnePassAsync();

            StringAssert.Contains(_git.Contents[Deck], "THE SUMMARY FOR THE TEACHER");
        }

        [TestMethod]
        public async Task ADeckThatAlreadyCarriesANoteDoesNotGetASecondOne()
        {
            _git.AddFile(Deck, ageCommits: 1, contents: TexFixtures.SlideDeckWithReviewNote(BodyOne));
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckWithReviewNote(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(0, _llm.SummaryCallCount, "nothing new to tell them");
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        [TestMethod]
        public async Task ASummariserThatFallsOverStillGetsTheReasonsOntoTheSlide()
        {
            // the reasons matter more than the prose, so they arrive unpolished rather than
            // not at all
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("RAW REASON ONE"));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("RAW REASON TWO"));
            _llm.SummaryResponse = "   ";

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            String deck = _git.Contents[Deck];
            Assert.IsTrue(AnswerMacros.HasReviewNote(deck));
            StringAssert.Contains(deck, "RAW REASON ONE");
            StringAssert.Contains(deck, "RAW REASON TWO");
            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
        }

        [TestMethod]
        public async Task ADeckWithNoDocumentBodyIsStillSettled()
        {
            // nowhere to splice a frame, but it must not go back into the loop
            _git.AddFile(Deck, ageCommits: 1,
                contents: AnswerMacros.Definitions + "\n\\documentclass{beamer}\n");
            _llm.When(p => p.Contains(RewriteAsk), AnswerMacros.Definitions + "\n\\documentclass{beamer}\n");
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            Assert.IsTrue(AnswerMacros.IsMarkedVerified(_git.Contents[DeckWorked]));
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
        }

        // ---- the loop terminates, which is the whole point ----

        [TestMethod]
        public async Task AnIrreconcilableDeckSettlesInOnePassAndStaysSettled()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await _orchestrator.DoOnePassAsync());

            int reviewsAfterFirstPass = _llm.ReviewCallCount;

            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());
            Assert.AreEqual(Orchestrator.PassOutcome.NothingToDo, await _orchestrator.DoOnePassAsync());

            Assert.AreEqual(reviewsAfterFirstPass, _llm.ReviewCallCount,
                "a settled deck must never be reviewed again");
        }

        [TestMethod]
        public async Task TheFixerIsNeverRunMoreThanTwice()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyOne),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyTwo));
            _llm.When(p => p.Contains(RewriteAsk) && p.Contains(BodyTwo),
                TexFixtures.SlideDeckDefiningAnswerMacros(BodyThree));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            await _orchestrator.DoOnePassAsync();

            Assert.AreEqual(2, _llm.PromptsSeen.Count(p => p.Contains(RewriteAsk)));
        }

        // ---- the fixer is judged on its own brief, not the review's ----

        [TestMethod]
        public async Task TheFixerIsNotToldWhatTheReviewObjectedTo()
        {
            // a review inventing work shouldn't get to drag the deck around with it
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail("SOME INVENTED OBJECTION");

            await _orchestrator.DoOnePassAsync();

            foreach (String prompt in _llm.PromptsSeen.Where(p => p.Contains(RewriteAsk)))
            {
                Assert.IsFalse(
                    prompt.Contains("SOME INVENTED OBJECTION", StringComparison.Ordinal),
                    "the fixer only resolves what its own prompt asks for");
            }
        }

        // ---- logging ----

        [TestMethod]
        public async Task EveryRejectionIsLoggedWithItsReasons()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("REASON ALPHA"));
            _llm.ReviewSequence.Enqueue(FakeLLMService.Fail("REASON BETA"));

            await _orchestrator.DoOnePassAsync();

            Assert.IsTrue(_llm.Logged.Any(m => m.Contains("REASON ALPHA")), "round 0 reasons must be logged");
            Assert.IsTrue(_llm.Logged.Any(m => m.Contains("REASON BETA")), "round 1 reasons must be logged");
            Assert.IsTrue(_llm.Logged.Any(m => m.Contains(Deck)), "and which deck they were about");
        }

        [TestMethod]
        public async Task TheLogNamesTheDeckButNeverQuotesIt()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail("a reason");

            await _orchestrator.DoOnePassAsync();

            foreach (String message in _llm.Logged)
            {
                Assert.IsFalse(
                    message.Contains(BodyOne, StringComparison.Ordinal),
                    $"file contents must not reach the log: {message}");
                Assert.IsFalse(
                    message.Contains("\\documentclass", StringComparison.Ordinal),
                    $"file contents must not reach the log: {message}");
            }
        }

        [TestMethod]
        public async Task APassIsLoggedToo()
        {
            AddDeck(BodyOne);
            _llm.DefaultVerdict = FakeLLMService.Pass;

            await _orchestrator.DoOnePassAsync();

            Assert.IsTrue(_llm.Logged.Any(m => m.Contains("PASS") && m.Contains(Deck)));
        }

        [TestMethod]
        public async Task AnUnresolvedReviewIsLoggedAsAWarning()
        {
            AddDeck(BodyOne);
            _llm.When(p => p.Contains(RewriteAsk), TexFixtures.SlideDeckDefiningAnswerMacros(BodyOne));
            _llm.DefaultVerdict = FakeLLMService.Fail();

            await _orchestrator.DoOnePassAsync();

            Assert.IsTrue(
                _llm.LogEntries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("disagree")),
                "a deck nobody can settle is worth a warning, not just an info line");
        }
    }
}
