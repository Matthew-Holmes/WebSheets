using System.Collections.ObjectModel;
using System.Diagnostics;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;

namespace SyntheticPDFs.Tests
{
    // The layer between git and the pipeline: a list of paths in, sheets and dictionaries
    // out. Everything downstream reasons about what comes out of here rather than taking
    // filenames apart for itself, so this is where the naming rules are pinned.
    [TestClass]
    public class ContentModelTests
    {
        private static RepoModel Repo(params String[] paths) => new()
        {
            Contents = new ReadOnlyCollection<TrackedFile>(paths
                .Select((p, i) => new TrackedFile { FullPath = p, AgeCommits = i + 1 })
                .ToList()),
            LastCommitHash = "deadbeef",
        };

        #region The archetypes there are

        [TestMethod]
        public void EveryArchetypeClassIsFoundWithoutBeingRegistered()
        {
            // found by reflection, so that adding a kind of source is a matter of adding
            // its file rather than of remembering a list somewhere else
            var names = SheetArchetypes.All.Select(a => a.Name).ToList();

            CollectionAssert.AreEquivalent(
                new[] { "MathematicalDictionary", "Poster", "QuestionSlides", "Worksheet" },
                names.ToArray());
        }

        [TestMethod]
        public void EveryArchetypeHasAFolderOfItsOwnAndAtLeastARoot()
        {
            foreach (SheetArchetype archetype in SheetArchetypes.All)
            {
                Assert.IsFalse(String.IsNullOrWhiteSpace(archetype.Folder), archetype.Name);
                Assert.IsFalse(String.IsNullOrWhiteSpace(archetype.Description), archetype.Name);

                Assert.AreEqual(SheetPart.Root, archetype.Parts[0],
                    $"{archetype.Name} must start with the file a person writes");
            }

            Assert.AreEqual(
                SheetArchetypes.All.Count,
                SheetArchetypes.All.Select(a => a.Folder).Distinct().Count(),
                "two archetypes claiming one folder makes a file's meaning a matter of load order");
        }

        [TestMethod]
        public void TheArchetypesAreSingletons()
        {
            // metadata carries one about, and a record's equality has to keep working
            Assert.AreSame(SheetArchetypes.Worksheet, SheetArchetypes.ByFolder("worksheets"));
            Assert.AreSame(SheetArchetypes.Worksheet, SheetArchetypes.ByName("Worksheet"));
        }

        #endregion

        #region What each archetype says about itself

        [TestMethod]
        public void OnlySlidesOweAnAnswerMacroCheck()
        {
            // the check is work owed against a file that already exists, which no plan can
            // express - it is asked about separately, off this one property
            Assert.IsTrue(SheetArchetypes.QuestionSlides.RevealsItsOwnAnswers);
            Assert.IsFalse(SheetArchetypes.Worksheet.RevealsItsOwnAnswers);
            Assert.IsFalse(SheetArchetypes.Poster.RevealsItsOwnAnswers);
        }

        [TestMethod]
        public void OnlyTheDictionaryHasNoGlossary()
        {
            // it has none because it is one
            Assert.IsFalse(SheetArchetypes.SharedDictionary.HasGlossary);
            Assert.IsTrue(SheetArchetypes.Worksheet.HasGlossary);
            Assert.IsTrue(SheetArchetypes.Poster.HasGlossary);
        }

        [TestMethod]
        public void OnlySlidesCarryExtraInstructionsForTheirWorkedSolutions()
        {
            Assert.IsNotNull(SheetArchetypes.QuestionSlides.WorkedSolutionsInstructions);
            Assert.IsNull(SheetArchetypes.Worksheet.WorkedSolutionsInstructions);
        }

        #endregion

        #region The dictionary's own naming

        [TestMethod]
        public void ATranslatedDictionarySitsBesideTheEnglishOneRatherThanUnderIt()
        {
            var metadata = new SourceMetadata
            {
                RootName  = "latex/dictionary/mathematicalDictionary",
                Archetype = SheetArchetypes.SharedDictionary,
                Language  = new ISO639_3Code("pol"),
                Part      = SheetPart.Root,
                Form      = SheetForm.ParallelText,
            };

            Assert.AreEqual(
                "latex/dictionary/L2/pol/mathematicalDictionary_polish.tex", metadata.FilePath);
        }

        [TestMethod]
        public void ATranslatedDictionaryReadsBackAsTheDictionaryItTranslates()
        {
            var parsed = SheetArchetypes.Parse(
                "latex/dictionary/L2/pol/mathematicalDictionary_polish");

            Assert.AreEqual(SheetArchetypes.SharedDictionary, parsed.Archetype);
            Assert.AreEqual("latex/dictionary/mathematicalDictionary", parsed.RootName);
            Assert.AreEqual("pol", parsed.Language.Code);
        }

        [TestMethod]
        public void TheEnglishDictionaryIsNamedExactlyWhereItLives()
        {
            // it is the file a person edits, so the pipeline must not rename it
            var parsed = SheetArchetypes.Parse("latex/dictionary/mathematicalDictionary");

            Assert.AreEqual(ISO639_3Code.eng, parsed.Language);
            Assert.AreEqual("latex/dictionary/mathematicalDictionary.tex", parsed.FilePath);
        }

        [TestMethod]
        public void ADictionaryAndItsTranslationsAreCollectedTogether()
        {
            var model = ContentModel.From(Repo(
                "latex/dictionary/mathematicalDictionary.tex",
                "latex/dictionary/L2/pol/mathematicalDictionary_polish.tex",
                "latex/dictionary/L2/urd/mathematicalDictionary_urdu.tex"));

            var state = model.DictionaryAt("latex/dictionary/mathematicalDictionary.tex");

            Assert.IsTrue(state.Exists);
            Assert.AreEqual(2, state.Translations.Count);
            Assert.AreEqual(0, model.Sheets.Count, "none of it is a sheet");
        }

        #endregion

        #region What it costs

        [TestMethod]
        public void BuildingAndJudgingTheWholeRepositoryIsFastEnoughToDoEveryPass()
        {
            // The model is rebuilt from scratch on every pass, and every sheet is judged
            // against a plan that has an entry per language per form. That is a lot of
            // small objects, and worth knowing the cost of - the readable version is
            // wanted either way, but not at the price of a pass that takes minutes.
            //
            // The budget is deliberately loose. It is here to catch an accident that makes
            // this quadratic, not to police a few milliseconds.
            const int sheets = 2000;

            var paths = new List<String>();

            for (int i = 0; i < sheets; i++)
            {
                paths.Add($"latex/worksheets/topic{i}/sheet{i}.tex");
                paths.Add($"latex/worksheets/topic{i}/sheet{i}_workedSolutions.tex");
                paths.Add($"latex/worksheets/topic{i}/sheet{i}_solutions.tex");
            }

            RepoModel repo = Repo(paths.ToArray());

            LanguageTable languages = FiftyLanguages();

            var clock = Stopwatch.StartNew();

            ContentModel model = ContentModel.From(repo).Judged(languages, includeGlossaries: true);

            clock.Stop();

            Assert.AreEqual(sheets, model.Sheets.Count);

            Console.WriteLine(
                $"{sheets} sheets, {languages.All.Count} languages: "
                + $"{clock.ElapsedMilliseconds} ms to build and judge the whole repository");

            Assert.IsTrue(clock.ElapsedMilliseconds < 20000,
                $"building the content model took {clock.ElapsedMilliseconds} ms, which is long "
                + "enough to suspect something has gone quadratic");
        }

        private static LanguageTable FiftyLanguages()
        {
            var options = new L2Options();

            foreach (String code in LanguageNames.AllCodes.Take(50))
            {
                options.Languages[code] = new LanguageOptions
                    { Font = "Noto Serif", BabelName = "english" };
            }

            return new LanguageTable(options);
        }

        #endregion
    }
}
