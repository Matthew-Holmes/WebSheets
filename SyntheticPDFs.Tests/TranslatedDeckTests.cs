using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // Translating a deck of starters, which is not the same job as translating a
    // worksheet.
    //
    // A worksheet that no longer fits takes another page. A slide cannot: whatever will
    // not fit on it is off the board rather than below it, and both translated versions
    // put a great deal more on a slide than the English put there - the parallel text a
    // second language, the tier 3 only version a translation raised above every subject
    // word. So a starter is split across three slides, and these are about the difference.
    [TestClass]
    public class TranslatedDeckTests
    {
        private const String Deck = "latex/starters/KS3/circlesArea.tex";

        private static readonly L2ColourOptions Colours = new();

        private static readonly VocabTerm[] Terms =
        {
            new VocabTerm
            {
                English = "radius", Definition = "the distance from the centre to the edge",
                Translation = "promien", TranslatedDefinition = "odleglosc od srodka do brzegu",
            },
        };

        // named by the form rather than handed one, since a test method has to be public
        // and the forms are not
        private static String PromptFor(String form, SheetArchetype archetype) =>
            form == "ParallelText"
                ? SourceGenerator.GenerateParallelTextPrompt(
                    Deck, Terms, TexFixtures.Polish, Colours, archetype)
                : SourceGenerator.GenerateTier3OnlyPrompt(
                    Deck, Terms, TexFixtures.Polish, Colours, archetype);

        #region What a deck is told and a worksheet is not

        [TestMethod]
        [DataRow("ParallelText")]
        [DataRow("Tier3Only")]
        public void BothTranslatedVersionsOfADeckSplitEachStarterAcrossThreeSlides(String form)
        {
            // both forms add to the slide, so both have the same problem with it
            String prompt = PromptFor(form, SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "becomes three slides");
            StringAssert.Contains(prompt, "as evenly between them as their number allows");
            StringAssert.Contains(prompt, "six questions become three slides of two");
        }

        [TestMethod]
        [DataRow("ParallelText")]
        [DataRow("Tier3Only")]
        public void AWorksheetIsToldNothingAboutSlides(String form)
        {
            // it has no slides to split, and it can run on to another page instead
            String prompt = PromptFor(form, SheetArchetypes.Worksheet);

            Assert.IsFalse(prompt.Contains("becomes three slides", StringComparison.Ordinal));
            StringAssert.Contains(prompt, "more pages than the",
                "a worksheet answers the same problem by growing");
        }

        [TestMethod]
        public void ASlideWithTooFewQuestionsToShareIsNotSplitIntoEmptyOnes()
        {
            String prompt = PromptFor("ParallelText", SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "two slides of one");
            StringAssert.Contains(prompt, "an empty slide is worse than a full one");
            StringAssert.Contains(prompt, "left as the one slide it is",
                "a title page or an answers slide asks nothing and is not split");
        }

        [TestMethod]
        public void TheQuestionsThemselvesAreNotAllowedToChange()
        {
            // the split is a rearrangement. a question that came back renumbered would
            // no longer match the worked solutions, the answers or the board
            String prompt = PromptFor("ParallelText", SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "never renumbered to 1");
            StringAssert.Contains(prompt, "Do not drop a question, do not repeat one");
        }

        [TestMethod]
        public void ADiagramFollowsTheQuestionThatRefersToIt()
        {
            String prompt = PromptFor("Tier3Only", SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "goes on the slide holding the question that refers to it");
            StringAssert.Contains(prompt, "put a copy on each",
                "two questions on different slides may want the same diagram");
        }

        [TestMethod]
        public void EachNewSlideStillHoldsItsAnswersBackToItsSecondOverlay()
        {
            // that is how a deck works, and the split must not separate an answer from
            // the question it belongs to
            String prompt = PromptFor("ParallelText", SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "answers on its second");
            StringAssert.Contains(prompt, "Never leave an answer on a slide its question is not on");
        }

        [TestMethod]
        public void TheNewSlidesKeepTheStartersTitleAndItsLink()
        {
            String prompt = PromptFor("ParallelText", SheetArchetypes.QuestionSlides);

            StringAssert.Contains(prompt, "Starter 3 (1 of 3)");
            StringAssert.Contains(prompt, @"\hypertarget");
            StringAssert.Contains(prompt, "on the first of the new slides and is not repeated");
        }

        #endregion

        #region Which files this dates

        [TestMethod]
        public void ADeckTranslatedBeforeItWasSplitIsOutOfDate()
        {
            // the rules live in a prompt, so nothing in the file can be read to tell
            // whether they were followed - what is recorded instead is which rules the
            // file was made under, and a file made before there were any records none
            String before = L2Macros.ProvenanceBlock(
                "title", Colours, TexFixtures.Polish, Deck, null);

            String after = L2Macros.ProvenanceBlock(
                "title", Colours, TexFixtures.Polish, Deck, null, sheetLayout: true);

            Assert.IsFalse(
                L2Macros.MatchesSettings(before, Colours, sheetLayout: true),
                "a deck translated before the split has to be made again");

            Assert.IsTrue(L2Macros.MatchesSettings(after, Colours, sheetLayout: true));
        }

        [TestMethod]
        public void SplittingADeckDoesNotDateEveryOtherTranslation()
        {
            // versioned apart from the shared macros for the same reason the key layout
            // is: a worksheet rebuilt over a rule about slides costs an API call and
            // comes back identical
            String worksheet = L2Macros.ProvenanceBlock(
                "title", Colours, TexFixtures.Polish, Deck, null);

            Assert.IsTrue(
                L2Macros.MatchesSettings(worksheet, Colours),
                "a worksheet says nothing about slide layout and does not need to");
        }

        [TestMethod]
        public void OnlyADeckRecordsHowItsSlidesWereLaidOut()
        {
            String deck = L2Macros.ProvenanceBlock(
                "title", Colours, TexFixtures.Polish, Deck, null, sheetLayout: true);

            String worksheet = L2Macros.ProvenanceBlock(
                "title", Colours, TexFixtures.Polish, Deck, null);

            StringAssert.Contains(deck, $"sheet layout        version {L2Macros.SheetLayoutVersion}",
                "written out in plain words, like everything else in the block");

            Assert.IsFalse(worksheet.Contains("sheet layout", StringComparison.Ordinal));
        }

        #endregion
    }
}
