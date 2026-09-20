using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // The version of a deck of starters made to be printed and handed out.
    //
    // It is a layout rather than a rewrite, and these are mostly about the difference:
    // that every slide is asked for at its first step so no answer is on the page, that
    // each one is written out four times so a page cuts into four, and that no model is
    // asked to have an opinion about a deck it could damage.
    [TestClass]
    public class ForPrintingTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";
        private const String DeckWorked = "latex/starters/KS3/circlesArea_workedSolutions.tex";

        private const String DeckVariant =
            "latex/starters/KS3/circlesArea_retrieveAndConnect.tex";

        private const String DeckPrintable =
            "latex/starters/KS3/circlesArea_forPrinting.tex";

        private const String VariantPrintable =
            "latex/starters/KS3/circlesArea_retrieveAndConnect_forPrinting.tex";

        private const String WorkedPrintable =
            "latex/starters/KS3/circlesArea_workedSolutions_forPrinting.tex";

        #region Laying one deck out

        private static String ADeckOf(params String[] frames) =>
            "\\documentclass{beamer}\n"
            + "\\usepackage{tikz}\n"
            + AnswerMacros.Definitions + "\n"
            + "\\begin{document}\n"
            + String.Join("\n", frames) + "\n"
            + "\\end{document}\n";

        private static String AFrame(String title, String body) =>
            "\\begin{frame}{" + title + "}\n" + body + "\n\\end{frame}";

        private static int Occurrences(String haystack, String needle)
        {
            int count = 0;
            int at = 0;

            while (true)
            {
                int found = haystack.IndexOf(needle, at, StringComparison.Ordinal);

                if (found < 0) { return count; }

                count++;
                at = found + needle.Length;
            }
        }

        // The one thing that keeps the answers off a printed question sheet. A deck
        // reveals its answers on its second overlay, so a slide asked for at its first
        // step is the questions and nothing else - whatever the deck used to hide them.
        [TestMethod]
        public void EverySlideIsAskedForAtItsFirstStep()
        {
            String printable = ForPrinting.Render(
                ADeckOf(AFrame("Starter 1", "What is $2+2$? \\ashow{4}")), Deck)!;

            Assert.AreEqual(0, Occurrences(printable, "\\begin{frame}{"),
                "no frame may be left asking for all of itself");

            Assert.AreEqual(
                ForPrinting.Copies, Occurrences(printable, "\\begin{frame}<1>{Starter 1}"));
        }

        [TestMethod]
        public void EachSlideIsWrittenOutFourTimes()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    AFrame("Starter 1", "first question"),
                    AFrame("Starter 2", "second question")),
                Deck)!;

            Assert.AreEqual(2 * ForPrinting.Copies, Occurrences(printable, "\\begin{frame}<1>"));
            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "first question"));
            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "second question"));
        }

        // The four copies of one slide have to be next to each other, or a page is four
        // different questions rather than four of the same one.
        [TestMethod]
        public void TheFourCopiesOfASlideAreConsecutive()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    AFrame("Starter 1", "first question"),
                    AFrame("Starter 2", "second question")),
                Deck)!;

            int lastOfTheFirst = printable.LastIndexOf("first question", StringComparison.Ordinal);
            int firstOfTheSecond = printable.IndexOf("second question", StringComparison.Ordinal);

            Assert.IsTrue(lastOfTheFirst < firstOfTheSecond,
                "the second question interleaved with the first");
        }

        [TestMethod]
        public void FourGoOnAPageInAGridThatCanBeCutUp()
        {
            String printable = ForPrinting.Render(ADeckOf(AFrame("Starter 1", "q")), Deck)!;

            StringAssert.Contains(printable, "\\usepackage{pgfpages}");
            StringAssert.Contains(printable, "\\pgfpagesuselayout{4 on 1}");
            StringAssert.Contains(printable, "border code=\\pgfusepath{stroke}");
        }

        // These are cut up and handed out, so the sheet wants filling: paper spent on a
        // border is paper not spent on the question. Pinned here because it is the one
        // number in the layout a person would otherwise have to open a pdf to check.
        [TestMethod]
        public void TheSlidesAreGivenBarelyAnyMargin()
        {
            String printable = ForPrinting.Render(ADeckOf(AFrame("Starter 1", "q")), Deck)!;

            Assert.AreEqual("1mm", ForPrinting.Margin);

            StringAssert.Contains(printable, "border shrink=" + ForPrinting.Margin + "]");
        }

        // Everything the layout needs is a package and some settings, so it has to land
        // in the preamble - put in the body it would be an error rather than a layout.
        [TestMethod]
        public void TheLayoutIsAddedToThePreamble()
        {
            String printable = ForPrinting.Render(ADeckOf(AFrame("Starter 1", "q")), Deck)!;

            int layout = printable.IndexOf("\\usepackage{pgfpages}", StringComparison.Ordinal);
            int opens = printable.IndexOf("\\begin{document}", StringComparison.Ordinal);

            Assert.IsTrue(layout > 0 && layout < opens);
        }

        // The point of laying a deck out rather than asking for it again: the deck's own
        // definitions, pictures and wording come through byte for byte.
        [TestMethod]
        public void NothingTheDeckSaysIsChanged()
        {
            String deck = ADeckOf(AFrame("Starter 1", "What is $2+2$? \\ashow{4}"));

            String printable = ForPrinting.Render(deck, Deck)!;

            StringAssert.Contains(printable, AnswerMacros.Definitions,
                "the helpers the deck defines must survive exactly");

            StringAssert.Contains(printable, "What is $2+2$? \\ashow{4}",
                "and so must every question");
        }

        #endregion

        #region Every slide, and one page each

        // The deck's own title page is written this way in every real deck here, and it
        // is the whole reason the grid used to slip: a slide reaching the page without
        // being repeated puts every page after it half a starter out of step.
        [TestMethod]
        public void ATitlePageWrittenAsACommandIsRepeatedLikeAnythingElse()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    "\\frame{\\titlepage}",
                    AFrame("Starter 1", "a real question")),
                Deck)!;

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "\\frame<1>{\\titlepage}"));
            Assert.AreEqual(0, Occurrences(printable, "\\frame{\\titlepage}"),
                "the untouched one would be a fifth page nobody asked for");

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "a real question"));
        }

        // Nothing is judged on what it contains any more. A deck without a title page must
        // not lose its first starter to a rule guessing which slides are worth printing.
        [TestMethod]
        [DataRow("\\titlepage")]
        [DataRow("\\maketitle")]
        [DataRow("\\tableofcontents")]
        public void NoSlideIsLeftOutForWhatIsOnIt(String macro)
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    "\\begin{frame}\n" + macro + "\n\\end{frame}",
                    AFrame("Starter 1", "a real question")),
                Deck)!;

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, macro));
            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "a real question"));
        }

        // The promise the whole layout rests on: four copies to a page means one page is
        // one slide, so a teacher printing pages 2 and 3 gets starters 2 and 3 whole.
        [TestMethod]
        public void EverySlideIsRepeatedTheSameNumberOfTimes()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    "\\frame{\\titlepage}",
                    AFrame("Starter 1", "first question"),
                    AFrame("Starter 2", "second question")),
                Deck)!;

            int steps = Occurrences(printable, "\\begin{frame}<1>")
                + Occurrences(printable, "\\frame<1>");

            Assert.AreEqual(3 * ForPrinting.Copies, steps);

            Assert.AreEqual(0, steps % ForPrinting.Copies,
                "a page that is not four copies of one slide shifts every page after it");
        }

        // A maintenance note is a slide like any other now. Left in and repeated it costs
        // one page; left in and not repeated it would cost the alignment of every page
        // after it, which is the trade this whole class is built around.
        [TestMethod]
        public void ANoteFromTheAnswerCheckIsRepeatedRatherThanDropped()
        {
            String deck = AnswerMacros.AddReviewNote(
                ADeckOf(AFrame("Starter 1", "a real question")),
                "the answers did not settle",
                atStart: false)!;

            String printable = ForPrinting.Render(deck, Deck)!;

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "Automated check"));
            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "a real question"));
        }

        [TestMethod]
        public void FrameTitleAndFriendsAreNotTheFrameCommand()
        {
            // \frametitle, \framesubtitle and \framebox all start with \frame
            String printable = ForPrinting.Render(
                ADeckOf(
                    "\\begin{frame}\n\\frametitle{Starter 1}\n\\framebox{a box}\n\\end{frame}"),
                Deck)!;

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "\\begin{frame}<1>"));
            Assert.AreEqual(0, Occurrences(printable, "\\frametitle<1>"));
            Assert.AreEqual(0, Occurrences(printable, "\\framebox<1>"));
        }

        [TestMethod]
        public void ACommentedOutSlideIsNotASlide()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    "% \\begin{frame}{An old starter}\n% dropped last year\n% \\end{frame}",
                    AFrame("Starter 1", "a real question")),
                Deck)!;

            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "\\begin{frame}<1>"));
            Assert.AreEqual(1, Occurrences(printable, "dropped last year"),
                "the comment stays where it was, but it is not a slide to repeat");
        }

        // A step the deck asked for itself cannot stand: it might be the step the answers
        // arrive on, and a question sheet with the answers on it is worse than one showing
        // a slide its author meant to build up to.
        [TestMethod]
        public void AFrameThatAlreadyCarriesAStepHasItReplaced()
        {
            String printable = ForPrinting.Render(
                ADeckOf("\\begin{frame}<2->{Starter 1}\nanswers\n\\end{frame}"), Deck)!;

            Assert.AreEqual(0, Occurrences(printable, "egin{frame}<2->"));
            Assert.AreEqual(ForPrinting.Copies, Occurrences(printable, "\\begin{frame}<1>{Starter 1}"));
        }

        #endregion

        #region When there is nothing to lay out

        [TestMethod]
        public void ADeckWithNoSlidesInItCannotBeLaidOut()
        {
            Assert.IsNull(ForPrinting.Render(
                "\\documentclass{beamer}\n\\begin{document}\nnot a frame\n\\end{document}", Deck));

            Assert.IsNull(ForPrinting.Render("\\documentclass{beamer}", Deck),
                "a file with no document body has nothing to lay out either");
        }

        // A copy is better than no file at all: whoever clicks it gets the lesson they
        // were after, and it says plainly why it is not laid out. It has to carry the
        // version line as well, or every pass would judge it out of date and write it
        // again for ever.
        [TestMethod]
        public void ADeckThatCannotBeLaidOutIsCopiedAndSaysSo()
        {
            String deck = "\\documentclass{beamer}\n\\begin{document}\n\\end{document}";

            String copied = ForPrinting.Unchanged(deck, Deck);

            StringAssert.Contains(copied, "\\documentclass{beamer}");
            StringAssert.Contains(copied, "none were found");
            Assert.IsTrue(ForPrinting.MatchesCurrentRules(copied));
        }

        #endregion

        #region What it says about itself

        [TestMethod]
        public void APrintableDeckSaysWhichRulesMadeItAndWhatFrom()
        {
            String printable = ForPrinting.Render(ADeckOf(AFrame("Starter 1", "q")), Deck)!;

            Assert.IsTrue(ForPrinting.MatchesCurrentRules(printable));

            Assert.IsFalse(ForPrinting.MatchesCurrentRules(ADeckOf(AFrame("Starter 1", "q"))),
                "a file that says nothing about how it was made cannot be shown to be current");

            StringAssert.Contains(printable, Deck, "and what it was made from");
        }

        // Whoever opens the file is a teacher, not a compiler, so what it says about
        // itself has to be readable - and the count is what tells them which pages to
        // send to the printer.
        [TestMethod]
        public void TheHeaderSaysHowManyPagesThereAreAndThatOneIsOneSlide()
        {
            String printable = ForPrinting.Render(
                ADeckOf(
                    "\\frame{\\titlepage}",
                    AFrame("Starter 1", "one"),
                    AFrame("Starter 2", "two")),
                Deck)!;

            StringAssert.Contains(printable, "pages on the sheet  3, one per slide of the deck");
            StringAssert.Contains(printable, "nothing was left out");
        }

        #endregion

        #region A whole pass

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
        }

        private Orchestrator Build() =>
            new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                _llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = 30 }),
                Options.Create(new L2Options { GenerateVocabularyKeys = false }));

        private void ASettledDeck()
        {
            _git.AddFile(Deck, ageCommits: 2, contents: TexFixtures.SlideDeckDefiningAnswerMacros());
            _git.AddFile(DeckWorked, ageCommits: 1,
                contents: TexFixtures.VerifiedSlideWorkedSolutions());
        }

        private static String NameOf(SyntheticPDFs.Models.TexSourceModel ts) => ts.FileNameFullPath;

        [TestMethod]
        public async Task TheDeckAndTheRetitledDeckEachGetOne()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            // the deck's own printable version comes with the retitling, both being made
            // from the deck itself
            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            CollectionAssert.Contains(_git.LastCommit.Select(NameOf).ToArray(), DeckPrintable);

            // and the retitled deck's follows once that deck exists
            Assert.AreEqual(
                Orchestrator.PassOutcome.Generated, await orchestrator.DoOnePassAsync());

            CollectionAssert.Contains(_git.LastCommit.Select(NameOf).ToArray(), VariantPrintable);

            StringAssert.Contains(_git.Contents[VariantPrintable], "Retrieve and Connect",
                "it is made from the retitled deck, so it carries the school's own name");

            Assert.AreEqual(
                Orchestrator.PassOutcome.NothingToDo, await orchestrator.DoOnePassAsync());
        }

        // The whole reason it is laid out here rather than asked for: a repository full of
        // decks costs nothing to make printable versions of.
        [TestMethod]
        public async Task NoModelIsEverAskedToLayADeckOut()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 4; pass++) { await orchestrator.DoOnePassAsync(); }

            Assert.AreEqual(0, _llm.CallCount);
            Assert.AreEqual(0, _llm.ReviewCallCount);
        }

        // Only the questions. The worked solutions are a deck to talk through, not a
        // sheet to hand out, and printing them would be handing out the answers.
        [TestMethod]
        public async Task TheWorkedSolutionsGetNoPrintableVersion()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 4; pass++) { await orchestrator.DoOnePassAsync(); }

            CollectionAssert.DoesNotContain(_git.Files.Keys.ToArray(), WorkedPrintable);
        }

        [TestMethod]
        public async Task OnlyADeckOfStartersGetsOne()
        {
            _git.AddFile("latex/worksheets/quadratics.tex", ageCommits: 1);
            _git.AddFile("latex/cheatSheets/trigIdentities.tex", ageCommits: 1);

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 4; pass++) { await orchestrator.DoOnePassAsync(); }

            Assert.IsFalse(
                _git.Files.Keys.Any(f => f.Contains("forPrinting", StringComparison.Ordinal)),
                "a worksheet is printed as it stands and a poster goes on a wall");
        }

        // Editing the deck is what invalidates its printable version, and editing the
        // retitled deck is what invalidates the printable version of that.
        [TestMethod]
        public async Task EditingTheDeckRebuildsBothPrintableVersions()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 3; pass++) { await orchestrator.DoOnePassAsync(); }

            foreach (String file in new[] { DeckWorked, DeckVariant, DeckPrintable, VariantPrintable })
            {
                _git.Files[file] = 1;
            }

            _git.AddFile(Deck, ageCommits: 0,
                contents: TexFixtures.SlideDeckDefiningAnswerMacros("a corrected question"));

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await orchestrator.DoOnePassAsync());

            String[] removed = _git.RemoveFilesCalls.Single().ToArray();

            CollectionAssert.Contains(removed, DeckPrintable);
            CollectionAssert.Contains(removed, VariantPrintable);
        }

        // A printable deck built by rules that have since changed is not one to keep: the
        // whole point of the version line is that changing the layout rebuilds the files
        // the old ones made.
        [TestMethod]
        public async Task OneBuiltByOlderRulesIsRemoved()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 3; pass++) { await orchestrator.DoOnePassAsync(); }

            _git.Contents[DeckPrintable] =
                _git.Contents[DeckPrintable].Replace(
                    $"printing rules      version {ForPrinting.Version}",
                    "printing rules      version 0");

            Assert.AreEqual(
                Orchestrator.PassOutcome.RemovedStaleFiles, await orchestrator.DoOnePassAsync());

            CollectionAssert.Contains(_git.RemoveFilesCalls.Single().ToArray(), DeckPrintable);
        }

        [TestMethod]
        public async Task APurgeOfTheTranslationsLeavesItAlone()
        {
            ASettledDeck();

            Orchestrator orchestrator = Build();

            for (int pass = 0; pass < 3; pass++) { await orchestrator.DoOnePassAsync(); }

            await orchestrator.PurgeAsync(Shared.PurgeScope.TranslationsAndVocabulary);

            CollectionAssert.DoesNotContain(
                _git.RemoveFilesCalls.SelectMany(c => c).ToArray(), DeckPrintable,
                "it is English, and clearing it would only mean writing it again");
        }

        #endregion
    }
}
