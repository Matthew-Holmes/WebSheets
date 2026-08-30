using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Models;

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

        private static SourceKey Key(
            SourceType type,
            SourceRendition rendition = SourceRendition.Original,
            ISO639_3Code? language = null) =>
            new(language ?? ISO639_3Code.eng, type, rendition);

        private static PlannedSource Find(
            IReadOnlyList<PlannedSource> plan, SourceKey key) =>
            plan.Single(p => p.Key.Equals(key));

        // ---- the English chain ----

        [TestMethod]
        [DataRow("Worksheet", 3)]
        [DataRow("QuestionSlides", 2)]
        [DataRow("Poster", 1)]
        public void EachArchetypeHasItsOwnSetOfTypes(String archetypeName, int expected)
        {
            var types = SourcePlan.TypesFor(Enum.Parse<SourceArchetype>(archetypeName));

            Assert.AreEqual(expected, types.Count);
            Assert.AreEqual(SourceType.Root, types[0], "every archetype starts with its root");
        }

        [TestMethod]
        public void TheRootIsWrittenByAPersonAndNeverGenerated()
        {
            var plan = SourcePlan.For(SourceArchetype.Worksheet, NoLanguages());

            Assert.IsTrue(Find(plan, Key(SourceType.Root)).Written);
            Assert.IsFalse(Find(plan, Key(SourceType.WorkedSolutions)).Written);
        }

        [TestMethod]
        public void EachStepIsDerivedFromEverythingBeforeIt()
        {
            // the answer key depends on the root as well as the workings, so editing the
            // root invalidates the whole chain rather than only the next link
            var plan = SourcePlan.For(SourceArchetype.Worksheet, NoLanguages());

            CollectionAssert.AreEquivalent(
                new[] { Key(SourceType.Root) },
                Find(plan, Key(SourceType.WorkedSolutions)).DependsOn.ToArray());

            CollectionAssert.AreEquivalent(
                new[] { Key(SourceType.Root), Key(SourceType.WorkedSolutions) },
                Find(plan, Key(SourceType.Solutions)).DependsOn.ToArray());
        }

        // ---- the vocabulary key ----

        [TestMethod]
        public void TheVocabularyKeyWaitsForEveryPartOfTheSheet()
        {
            // words a student needs may appear only in the answers, so the key cannot be
            // written until all of it exists
            var plan = SourcePlan.For(SourceArchetype.Worksheet, NoLanguages());

            CollectionAssert.AreEquivalent(
                new[] { Key(SourceType.Root), Key(SourceType.WorkedSolutions), Key(SourceType.Solutions) },
                Find(plan, Key(SourceType.Root, SourceRendition.VocabKey)).DependsOn.ToArray());
        }

        [TestMethod]
        [DataRow("Poster", 1)]
        [DataRow("QuestionSlides", 2)]
        public void TheVocabularyKeyOnlyWaitsForFilesThatArchetypeActuallyHas(
            String archetypeName, int expected)
        {
            // the caveat that matters: a poster has no worked solutions, so waiting for
            // them would mean its key was never written at all
            var archetype = Enum.Parse<SourceArchetype>(archetypeName);

            var plan = SourcePlan.For(archetype, NoLanguages());

            Assert.AreEqual(
                expected,
                Find(plan, Key(SourceType.Root, SourceRendition.VocabKey)).DependsOn.Count);
        }

        // ---- translations ----

        [TestMethod]
        public void NoLanguagesConfiguredMeansNoTranslatedFilesAreEvenPlanned()
        {
            var plan = SourcePlan.For(SourceArchetype.Worksheet, NoLanguages());

            Assert.IsFalse(
                plan.Any(p => p.Key.Language != ISO639_3Code.eng),
                "a translation of a language we cannot typeset must not be planned");
        }

        [TestMethod]
        public void TheTranslatedKeyIsDerivedFromTheEnglishVocabularyKey()
        {
            var plan = SourcePlan.For(SourceArchetype.Worksheet, Table());

            CollectionAssert.AreEquivalent(
                new[] { Key(SourceType.Root, SourceRendition.VocabKey) },
                Find(plan, Key(SourceType.Root, SourceRendition.L2Key, Pol)).DependsOn.ToArray());
        }

        [TestMethod]
        [DataRow("ParallelText")]
        [DataRow("Tier3Only")]
        public void ATranslatedRenditionNeedsBothTheKeyAndItsEnglishCounterpart(String renditionName)
        {
            var rendition = Enum.Parse<SourceRendition>(renditionName);

            var plan = SourcePlan.For(SourceArchetype.Worksheet, Table());

            CollectionAssert.AreEquivalent(
                new[]
                {
                    Key(SourceType.Root, SourceRendition.L2Key, Pol),
                    Key(SourceType.WorkedSolutions),
                },
                Find(plan, Key(SourceType.WorkedSolutions, rendition, Pol)).DependsOn.ToArray());
        }

        [TestMethod]
        public void ATranslationIsNotPlannedForATypeItsArchetypeLacks()
        {
            // slides have no answer key, so neither does any translation of them
            var plan = SourcePlan.For(SourceArchetype.QuestionSlides, Table());

            Assert.IsFalse(
                plan.Any(p => p.Key.Type == SourceType.Solutions),
                "a deck has no answer key to translate");
        }

        // ---- what gets made without being asked ----

        [TestMethod]
        public void OnlyTheSheetItselfIsEagerInAnEagerLanguage()
        {
            // translating the worked solutions and answers for every language as well
            // would cost far more than it is worth, so those are made on request
            var plan = SourcePlan.For(SourceArchetype.Worksheet, Table());

            Assert.IsTrue(Find(plan, Key(SourceType.Root, SourceRendition.ParallelText, Pol)).Eager);
            Assert.IsTrue(Find(plan, Key(SourceType.Root, SourceRendition.Tier3Only, Pol)).Eager);
            Assert.IsTrue(Find(plan, Key(SourceType.Root, SourceRendition.L2Key, Pol)).Eager,
                "the key seeds everything else in that language");

            Assert.IsFalse(
                Find(plan, Key(SourceType.WorkedSolutions, SourceRendition.ParallelText, Pol)).Eager);
            Assert.IsFalse(
                Find(plan, Key(SourceType.Solutions, SourceRendition.Tier3Only, Pol)).Eager);
        }

        [TestMethod]
        public void AConfiguredButNotEagerLanguageIsPlannedAndNeverMadeUnasked()
        {
            var plan = SourcePlan.For(SourceArchetype.Worksheet, Table());

            var bengali = plan.Where(p => p.Key.Language.Equals(Ben)).ToList();

            Assert.IsTrue(bengali.Count > 0, "it must still be planned, so it can be requested");
            Assert.IsFalse(bengali.Any(p => p.Eager), "but nothing in it is made unasked");
        }

        // ---- staleness read off the plan ----

        private static Orchestrator.TrackedFileWithMetadata TrackedFile(
            SourceKey key, int age, SourceArchetype archetype = SourceArchetype.Worksheet)
        {
            var metadata = new Orchestrator.SourceMetadata
            {
                RootName  = Root,
                Type      = key.Type,
                Rendition = key.Rendition,
                Archetype = archetype,
                Language  = key.Language,
            };

            return new Orchestrator.TrackedFileWithMetadata
            {
                SourceMetadata = metadata,
                TrackedFile = new TrackedFile
                {
                    FullPath   = Orchestrator.GetFilenameFromMetadata(metadata),
                    AgeCommits = age,
                },
            };
        }

        private static Orchestrator.RootPlanState State(
            SourceArchetype archetype,
            LanguageTable languages,
            params Orchestrator.TrackedFileWithMetadata[] files) =>
            Orchestrator.BuildPlanState(
                Root, archetype, files, SourcePlan.For(archetype, languages));

        [TestMethod]
        public void EditingTheEnglishSheetInvalidatesItsTranslations()
        {
            // the cross-language edge, which is the whole reason a root is judged as one
            // unit rather than one language at a time
            var state = State(SourceArchetype.Worksheet, Table(),
                TrackedFile(Key(SourceType.Root), 1),
                TrackedFile(Key(SourceType.WorkedSolutions), 6),
                TrackedFile(Key(SourceType.Solutions), 5),
                TrackedFile(Key(SourceType.Root, SourceRendition.VocabKey), 4),
                TrackedFile(Key(SourceType.Root, SourceRendition.L2Key, Pol), 3),
                TrackedFile(Key(SourceType.Root, SourceRendition.ParallelText, Pol), 2));

            // the root is younger than everything, so the whole tree below it has gone
            Assert.AreEqual(5, state.StaleFiles.Count,
                "staleness must reach the translations, not stop at the English files");
        }

        [TestMethod]
        public void StalenessIsTransitiveThroughTheVocabularyKey()
        {
            // the parallel text is younger than the key it came from, so only a
            // transitive rule catches it
            var state = State(SourceArchetype.Poster, Table(),
                TrackedFile(Key(SourceType.Root), 1, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.Root, SourceRendition.VocabKey), 9, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.Root, SourceRendition.L2Key, Pol), 2, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.Root, SourceRendition.ParallelText, Pol), 2, SourceArchetype.Poster));

            Assert.AreEqual(3, state.StaleFiles.Count,
                "the key is stale, so everything derived from it is too");
        }

        [TestMethod]
        public void AFileTheArchetypeMayNotHaveIsStale()
        {
            // left over from before posters stopped getting worked solutions. it is
            // younger than the root, so only the plan catches it
            var state = State(SourceArchetype.Poster, NoLanguages(),
                TrackedFile(Key(SourceType.Root), 5, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.WorkedSolutions), 1, SourceArchetype.Poster));

            Assert.AreEqual(1, state.StaleFiles.Count);
            Assert.AreEqual(SourceType.WorkedSolutions,
                state.StaleFiles.Single().SourceMetadata.Type);
        }

        [TestMethod]
        public void ATranslationIntoALanguageNoLongerConfiguredIsStale()
        {
            var state = State(SourceArchetype.Worksheet, NoLanguages(),
                TrackedFile(Key(SourceType.Root), 3),
                TrackedFile(Key(SourceType.WorkedSolutions), 2),
                TrackedFile(Key(SourceType.Solutions), 1),
                TrackedFile(Key(SourceType.Root, SourceRendition.ParallelText, Pol), 1));

            Assert.AreEqual(1, state.StaleFiles.Count);
            Assert.AreEqual(Pol, state.StaleFiles.Single().SourceMetadata.Language);
        }

        [TestMethod]
        public void NothingIsCreatableUntilWhatItComesFromIsThere()
        {
            var state = State(SourceArchetype.Worksheet, Table(),
                TrackedFile(Key(SourceType.Root), 1));

            var creatable = state.Creatable().Select(p => p.Key).ToList();

            CollectionAssert.AreEqual(
                new[] { Key(SourceType.WorkedSolutions) },
                creatable.ToArray(),
                "only the next link in the chain is ready");
        }

        [TestMethod]
        public void TheTranslationsBecomeCreatableTogetherOnceTheirKeyExists()
        {
            // both renditions depend on the key and on the English sheet, and on nothing
            // the other produces, so they are free to be generated in parallel
            var state = State(SourceArchetype.Poster, Table(),
                TrackedFile(Key(SourceType.Root), 4, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.Root, SourceRendition.VocabKey), 3, SourceArchetype.Poster),
                TrackedFile(Key(SourceType.Root, SourceRendition.L2Key, Pol), 2, SourceArchetype.Poster));

            var creatable = state.Creatable()
                .Where(p => p.Key.Language.Equals(Pol))
                .Select(p => p.Key.Rendition)
                .ToList();

            CollectionAssert.AreEquivalent(
                new[] { SourceRendition.ParallelText, SourceRendition.Tier3Only },
                creatable.ToArray());
        }
    }
}
