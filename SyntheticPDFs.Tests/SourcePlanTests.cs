using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;

namespace SyntheticPDFs.Tests
{
    // the plan is the single statement of which files a root may have and what each is
    // derived from. staleness and batch selection are both read off it, so these tests
    // pin the shape both of them depend on
    [TestClass]
    public class SourcePlanTests
    {
        private const String Root = "latex/worksheets/sheet";

        private static readonly ISO639_3Code Pol = new("pol");
        private static readonly ISO639_3Code Ben = new("ben");

        // pol is eager, ben is configured but only made on request
        private static LanguageTable Table()
        {
            var options = new L2Options();

            options.Languages["pol"] = new LanguageOptions
                { Font = "Noto Serif", BabelName = "polish" };
            options.Languages["ben"] = new LanguageOptions
                { Font = "Noto Sans Bengali", BabelName = "bengali" };

            options.EagerLanguages.Add("pol");

            return new LanguageTable(options);
        }

        private static LanguageTable NoLanguages() => new LanguageTable(new L2Options());

        private static ContentKey Key(
            SheetPart type,
            SheetForm form = SheetForm.Original,
            ISO639_3Code? language = null) =>
            new(language ?? ISO639_3Code.eng, type, form);

        private static PlannedFile Find(
            IReadOnlyList<PlannedFile> plan, ContentKey key) =>
            plan.Single(p => p.Key.Equals(key));

        // ---- the English chain ----

        [TestMethod]
        [DataRow("Worksheet", 3)]
        [DataRow("QuestionSlides", 2)]
        [DataRow("Poster", 1)]
        public void EachArchetypeHasItsOwnSetOfTypes(String archetypeName, int expected)
        {
            var parts = SheetArchetypes.ByName(archetypeName)!.Parts;

            Assert.AreEqual(expected, parts.Count);
            Assert.AreEqual(SheetPart.Root, parts[0], "every archetype starts with its root");
        }

        [TestMethod]
        public void TheRootIsWrittenByAPersonAndNeverGenerated()
        {
            var plan = SheetArchetypes.Worksheet.Plan(NoLanguages());

            Assert.IsTrue(Find(plan, Key(SheetPart.Root)).Written);
            Assert.IsFalse(Find(plan, Key(SheetPart.WorkedSolutions)).Written);
        }

        [TestMethod]
        public void EachStepIsDerivedFromEverythingBeforeIt()
        {
            // the answer key depends on the root as well as the workings, so editing the
            // root invalidates the whole chain rather than only the next link
            var plan = SheetArchetypes.Worksheet.Plan(NoLanguages());

            CollectionAssert.AreEquivalent(
                new[] { Key(SheetPart.Root) },
                Find(plan, Key(SheetPart.WorkedSolutions)).DependsOn.ToArray());

            CollectionAssert.AreEquivalent(
                new[] { Key(SheetPart.Root), Key(SheetPart.WorkedSolutions) },
                Find(plan, Key(SheetPart.Solutions)).DependsOn.ToArray());
        }

        // ---- the vocabulary key ----

        [TestMethod]
        public void TheVocabularyKeyWaitsForEveryPartOfTheSheet()
        {
            // words a student needs may appear only in the answers, so the key cannot be
            // written until all of it exists
            var plan = SheetArchetypes.Worksheet.Plan(NoLanguages());

            CollectionAssert.AreEquivalent(
                new[] { Key(SheetPart.Root), Key(SheetPart.WorkedSolutions), Key(SheetPart.Solutions) },
                Find(plan, Key(SheetPart.Root, SheetForm.Glossary)).DependsOn.ToArray());
        }

        [TestMethod]
        [DataRow("Poster", 1)]
        [DataRow("QuestionSlides", 2)]
        public void TheVocabularyKeyOnlyWaitsForFilesThatArchetypeActuallyHas(
            String archetypeName, int expected)
        {
            // the caveat that matters: a poster has no worked solutions, so waiting for
            // them would mean its key was never written at all
            var archetype = SheetArchetypes.ByName(archetypeName)!;

            var plan = archetype.Plan(NoLanguages());

            Assert.AreEqual(
                expected,
                Find(plan, Key(SheetPart.Root, SheetForm.Glossary)).DependsOn.Count);
        }

        // ---- translations ----

        [TestMethod]
        public void NoLanguagesConfiguredMeansNoTranslatedFilesAreEvenPlanned()
        {
            var plan = SheetArchetypes.Worksheet.Plan(NoLanguages());

            Assert.IsFalse(
                plan.Any(p => p.Key.Language != ISO639_3Code.eng),
                "a translation of a language we cannot typeset must not be planned");
        }

        [TestMethod]
        public void TheTranslatedKeyIsDerivedFromTheEnglishVocabularyKey()
        {
            var plan = SheetArchetypes.Worksheet.Plan(Table());

            CollectionAssert.AreEquivalent(
                new[] { Key(SheetPart.Root, SheetForm.Glossary) },
                Find(plan, Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol)).DependsOn.ToArray());
        }

        [TestMethod]
        [DataRow("ParallelText")]
        [DataRow("Tier3Only")]
        public void ATranslatedFormNeedsBothTheKeyAndItsEnglishCounterpart(String formName)
        {
            var form = Enum.Parse<SheetForm>(formName);

            var plan = SheetArchetypes.Worksheet.Plan(Table());

            CollectionAssert.AreEquivalent(
                new[]
                {
                    Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol),
                    Key(SheetPart.WorkedSolutions),
                },
                Find(plan, Key(SheetPart.WorkedSolutions, form, Pol)).DependsOn.ToArray());
        }

        [TestMethod]
        public void ATranslationIsNotPlannedForATypeItsArchetypeLacks()
        {
            // slides have no answer key, so neither does any translation of them
            var plan = SheetArchetypes.QuestionSlides.Plan(Table());

            Assert.IsFalse(
                plan.Any(p => p.Key.Part == SheetPart.Solutions),
                "a deck has no answer key to translate");
        }

        // ---- what gets made without being asked ----

        [TestMethod]
        public void OnlyTheSheetItselfIsEagerInAnEagerLanguage()
        {
            // translating the worked solutions and answers for every language as well
            // would cost far more than it is worth, so those are made on request
            var plan = SheetArchetypes.Worksheet.Plan(Table());

            Assert.IsTrue(Find(plan, Key(SheetPart.Root, SheetForm.ParallelText, Pol)).Eager);
            Assert.IsTrue(Find(plan, Key(SheetPart.Root, SheetForm.Tier3Only, Pol)).Eager);
            Assert.IsTrue(Find(plan, Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol)).Eager,
                "the key seeds everything else in that language");

            Assert.IsFalse(
                Find(plan, Key(SheetPart.WorkedSolutions, SheetForm.ParallelText, Pol)).Eager);
            Assert.IsFalse(
                Find(plan, Key(SheetPart.Solutions, SheetForm.Tier3Only, Pol)).Eager);
        }

        [TestMethod]
        public void AConfiguredButNotEagerLanguageIsPlannedAndNeverMadeUnasked()
        {
            var plan = SheetArchetypes.Worksheet.Plan(Table());

            var bengali = plan.Where(p => p.Key.Language.Equals(Ben)).ToList();

            Assert.IsTrue(bengali.Count > 0, "it must still be planned, so it can be requested");
            Assert.IsFalse(bengali.Any(p => p.Eager), "but nothing in it is made unasked");
        }

        // ---- staleness read off the plan ----

        private static ContentFile TrackedFile(
            ContentKey key, int age, SheetArchetype? archetype = null)
        {
            var metadata = new SourceMetadata
            {
                RootName  = Root,
                Part      = key.Part,
                Form      = key.Form,
                Archetype = archetype ?? SheetArchetypes.Worksheet,
                Language  = key.Language,
            };

            return new ContentFile
            {
                SourceMetadata = metadata,
                TrackedFile = new TrackedFile
                {
                    FullPath   = metadata.FilePath,
                    AgeCommits = age,
                },
            };
        }

        private static SheetState State(
            SheetArchetype archetype,
            LanguageTable languages,
            params ContentFile[] files) =>
            new SheetState
            {
                RootName  = Root,
                Archetype = archetype,
                Files     = files.ToDictionary(f => f.Key),
            }
            .Judged(languages, includeGlossaries: true);

        [TestMethod]
        public void EditingTheEnglishSheetInvalidatesItsTranslations()
        {
            // the cross-language edge, which is the whole reason a root is judged as one
            // unit rather than one language at a time
            var state = State(SheetArchetypes.Worksheet, Table(),
                TrackedFile(Key(SheetPart.Root), 1),
                TrackedFile(Key(SheetPart.WorkedSolutions), 6),
                TrackedFile(Key(SheetPart.Solutions), 5),
                TrackedFile(Key(SheetPart.Root, SheetForm.Glossary), 4),
                TrackedFile(Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol), 3),
                TrackedFile(Key(SheetPart.Root, SheetForm.ParallelText, Pol), 2));

            // the root is younger than everything, so the whole tree below it has gone
            Assert.AreEqual(5, state.StaleFiles.Count,
                "staleness must reach the translations, not stop at the English files");
        }

        [TestMethod]
        public void StalenessIsTransitiveThroughTheVocabularyKey()
        {
            // the parallel text is younger than the key it came from, so only a
            // transitive rule catches it
            var state = State(SheetArchetypes.Poster, Table(),
                TrackedFile(Key(SheetPart.Root), 1, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.Root, SheetForm.Glossary), 9, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol), 2, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.Root, SheetForm.ParallelText, Pol), 2, SheetArchetypes.Poster));

            Assert.AreEqual(3, state.StaleFiles.Count,
                "the key is stale, so everything derived from it is too");
        }

        [TestMethod]
        public void AFileTheArchetypeMayNotHaveIsStale()
        {
            // left over from before posters stopped getting worked solutions. it is
            // younger than the root, so only the plan catches it
            var state = State(SheetArchetypes.Poster, NoLanguages(),
                TrackedFile(Key(SheetPart.Root), 5, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.WorkedSolutions), 1, SheetArchetypes.Poster));

            Assert.AreEqual(1, state.StaleFiles.Count);
            Assert.AreEqual(SheetPart.WorkedSolutions,
                state.StaleFiles.Single().SourceMetadata.Part);
        }

        [TestMethod]
        public void ATranslationIntoALanguageNoLongerConfiguredIsStale()
        {
            var state = State(SheetArchetypes.Worksheet, NoLanguages(),
                TrackedFile(Key(SheetPart.Root), 3),
                TrackedFile(Key(SheetPart.WorkedSolutions), 2),
                TrackedFile(Key(SheetPart.Solutions), 1),
                TrackedFile(Key(SheetPart.Root, SheetForm.ParallelText, Pol), 1));

            Assert.AreEqual(1, state.StaleFiles.Count);
            Assert.AreEqual(Pol, state.StaleFiles.Single().SourceMetadata.Language);
        }

        [TestMethod]
        public void NothingIsCreatableUntilWhatItComesFromIsThere()
        {
            var state = State(SheetArchetypes.Worksheet, Table(),
                TrackedFile(Key(SheetPart.Root), 1));

            var creatable = state.Creatable().Select(p => p.Key).ToList();

            CollectionAssert.AreEqual(
                new[] { Key(SheetPart.WorkedSolutions) },
                creatable.ToArray(),
                "only the next link in the chain is ready");
        }

        [TestMethod]
        public void TheTranslationsBecomeCreatableTogetherOnceTheirKeyExists()
        {
            // both forms depend on the key and on the English sheet, and on nothing
            // the other produces, so they are free to be generated in parallel
            var state = State(SheetArchetypes.Poster, Table(),
                TrackedFile(Key(SheetPart.Root), 4, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.Root, SheetForm.Glossary), 3, SheetArchetypes.Poster),
                TrackedFile(Key(SheetPart.Root, SheetForm.TranslatedGlossary, Pol), 2, SheetArchetypes.Poster));

            var creatable = state.Creatable()
                .Where(p => p.Key.Language.Equals(Pol))
                .Select(p => p.Key.Form)
                .ToList();

            CollectionAssert.AreEquivalent(
                new[] { SheetForm.ParallelText, SheetForm.Tier3Only },
                creatable.ToArray());
        }
    }
}
