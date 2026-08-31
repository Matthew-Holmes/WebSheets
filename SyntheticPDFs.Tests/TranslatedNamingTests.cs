using SyntheticPDFs.Logic;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // the language lives in a folder and the language's English name lives in the
    // filename, so that a served pdf is readable by someone who does not know ISO codes
    [TestClass]
    public class TranslatedNamingTests
    {
        private const String Root = "latex/worksheets/algebra/quadratics";

        private static SourceMetadata Meta(
            String code, String typeName, String formName, String root = Root) =>
            new SourceMetadata
            {
                RootName  = root,
                Part      = Enum.Parse<SheetPart>(typeName),
                Archetype = SheetArchetypes.Worksheet,
                Language  = new ISO639_3Code(code),
                Form      = Enum.Parse<SheetForm>(formName),
            };

        [TestMethod]
        [DataRow("pol", "Root", "TranslatedGlossary",
            "latex/worksheets/algebra/quadratics/L2/pol/quadratics_polishKey.tex")]
        [DataRow("pol", "Root", "ParallelText",
            "latex/worksheets/algebra/quadratics/L2/pol/quadratics_polishParallelText.tex")]
        [DataRow("urd", "Root", "Tier3Only",
            "latex/worksheets/algebra/quadratics/L2/urd/quadratics_urduTier3Only.tex")]
        [DataRow("ben", "WorkedSolutions", "ParallelText",
            "latex/worksheets/algebra/quadratics/L2/ben/quadratics_workedSolutions_bengaliParallelText.tex")]
        [DataRow("ara", "Solutions", "Tier3Only",
            "latex/worksheets/algebra/quadratics/L2/ara/quadratics_solutions_arabicTier3Only.tex")]
        public void TranslatedFilesAreNamedByFolderAndLanguageName(
            String code, String typeName, String formName, String expected)
        {
            Assert.AreEqual(expected, (Meta(code, typeName, formName)).FilePath);
        }

        [TestMethod]
        [DataRow("pol", "Root", "TranslatedGlossary")]
        [DataRow("pol", "Root", "ParallelText")]
        [DataRow("pol", "Root", "Tier3Only")]
        [DataRow("urd", "WorkedSolutions", "ParallelText")]
        [DataRow("ben", "WorkedSolutions", "Tier3Only")]
        [DataRow("ara", "Solutions", "ParallelText")]
        [DataRow("pan", "Solutions", "Tier3Only")]
        public void TranslatedNamesRoundTrip(String code, String typeName, String formName)
        {
            var original = Meta(code, typeName, formName);

            String filename = original.FilePath;

            var parsed = SheetArchetypes.Parse(filename[..^".tex".Length]);

            Assert.AreEqual(original.RootName,  parsed.RootName,  "root name changed");
            Assert.AreEqual(original.Part,      parsed.Part,      "source type changed");
            Assert.AreEqual(original.Language,  parsed.Language,  "language changed");
            Assert.AreEqual(original.Form, parsed.Form, "form changed");
        }

        [TestMethod]
        public void TranslatedFilesKeepTheArchetypeOfTheirRoot()
        {
            // the archetype is read off the folder after "latex", which the L2 nesting
            // sits well below, so a translated slide deck is still a slide deck
            var parsed = SheetArchetypes.Parse(
                "latex/starters/KS3/circlesArea/L2/pol/circlesArea_polishParallelText");

            Assert.AreEqual(SheetArchetypes.QuestionSlides, parsed.Archetype);
            Assert.AreEqual("latex/starters/KS3/circlesArea", parsed.RootName);
        }

        [TestMethod]
        public void VocabKeyRoundTrips()
        {
            var original = new SourceMetadata
            {
                RootName  = Root,
                Part      = SheetPart.Root,
                Archetype = SheetArchetypes.Worksheet,
                Language  = ISO639_3Code.eng,
                Form      = SheetForm.Glossary,
            };

            String filename = original.FilePath;

            Assert.AreEqual("latex/worksheets/algebra/quadratics_vocab.tex", filename);

            var parsed = SheetArchetypes.Parse(filename[..^".tex".Length]);

            Assert.AreEqual(Root, parsed.RootName);
            Assert.AreEqual(SheetForm.Glossary, parsed.Form);
            Assert.AreEqual(ISO639_3Code.eng, parsed.Language);
        }

        [TestMethod]
        public void EnglishOriginalsAreUnchangedByTheFormAxis()
        {
            // the form defaults to Original, so every name the pipeline already
            // produced has to come out byte for byte the same
            var metadata = new SourceMetadata
            {
                RootName  = Root,
                Part      = SheetPart.WorkedSolutions,
                Archetype = SheetArchetypes.Worksheet,
                Language  = ISO639_3Code.eng,
            };

            Assert.AreEqual(
                "latex/worksheets/algebra/quadratics_workedSolutions.tex",
                metadata.FilePath);
        }

        [TestMethod]
        public void AnUnnameableLanguageIsRefusedRatherThanGuessedAt()
        {
            var metadata = Meta("zzz", "Root", "ParallelText");

            Assert.ThrowsException<ArgumentException>(
                () => metadata.FilePath);
        }

        // ---- paths that are not ours ----

        [TestMethod]
        [DataRow("latex/worksheets/quadratics/L2/zzz/quadratics_klingonKey", "zzz")]
        [DataRow("latex/worksheets/quadratics/L2/pol/quadratics_polishSomethingElse", "SomethingElse")]
        [DataRow("latex/worksheets/quadratics/L2/pol/quadratics_frenchKey", "polish")]
        public void AnL2PathWeCannotReadIsWarnedAboutRatherThanFatal(String path, String mentioned)
        {
            var logger = new RecordingLogger();

            var parsed = SheetArchetypes.Parse(path, logger);

            Assert.AreEqual(ISO639_3Code.eng, parsed.Language, "an unreadable name must not claim a language");
            StringAssert.Contains(logger.Warnings.Single(), mentioned);
        }

        [TestMethod]
        public void WellFormedTranslatedNamesProduceNoWarnings()
        {
            var logger = new RecordingLogger();

            SheetArchetypes.Parse(
                "latex/worksheets/quadratics/L2/pol/quadratics_polishKey", logger);
            SheetArchetypes.Parse(
                "latex/worksheets/quadratics/L2/urd/quadratics_workedSolutions_urduParallelText", logger);
            SheetArchetypes.Parse(
                "latex/worksheets/quadratics_vocab", logger);

            CollectionAssert.AreEqual(
                Array.Empty<String>(),
                logger.Warnings.ToArray(),
                "well-formed names must not be noisy");
        }

        [TestMethod]
        public void ANameEndingInALanguageCodeIsStillAnEnglishRoot()
        {
            // the old convention put the code in the name. nothing in the repository uses
            // it, so such a name is now either a leftover or a coincidence - either way
            // the folder is what decides, and this is a root
            var logger = new RecordingLogger();

            var parsed = SheetArchetypes.Parse("latex/worksheets/sheet_fra", logger);

            Assert.AreEqual(ISO639_3Code.eng, parsed.Language);
            Assert.AreEqual("latex/worksheets/sheet_fra", parsed.RootName);
            StringAssert.Contains(logger.Warnings.Single(), "fra");
        }
    }
}
