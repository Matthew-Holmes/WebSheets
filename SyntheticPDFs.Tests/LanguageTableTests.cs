using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // a language needs a name it can be filed under and a font it can be typeset in.
    // one without both is dropped with a warning, since the English pipeline does not
    // depend on any of it and taking the service down would be out of proportion
    [TestClass]
    public class LanguageTableTests
    {
        private static L2Options Options(params (String Code, String Font, String Babel)[] languages)
        {
            var options = new L2Options();

            foreach (var (code, font, babel) in languages)
            {
                options.Languages[code] = new LanguageOptions { Font = font, BabelName = babel };
            }

            return options;
        }

        [TestMethod]
        public void AFullyConfiguredLanguageResolves()
        {
            var table = new LanguageTable(Options(("pol", "Noto Serif", "polish")));

            var profile = table.Get(new ISO639_3Code("pol"));

            Assert.IsNotNull(profile);
            Assert.AreEqual("polish", profile.EnglishName);
            Assert.AreEqual("Polish", profile.TitleName, "the provenance title capitalises it");
            Assert.AreEqual("Noto Serif", profile.Font);
            Assert.IsFalse(profile.RightToLeft);
            Assert.AreEqual("left to right", profile.DirectionDescription);
        }

        [TestMethod]
        public void ARightToLeftLanguageSaysSo()
        {
            var options = Options(("urd", "Noto Nastaliq Urdu", "urdu"));
            options.Languages["urd"].RightToLeft = true;

            var profile = new LanguageTable(options).Get(new ISO639_3Code("urd"));

            Assert.IsNotNull(profile);
            Assert.IsTrue(profile.RightToLeft);
            Assert.AreEqual("right to left", profile.DirectionDescription);
        }

        [TestMethod]
        public void ALanguageWithNoNameIsDroppedAndWarnedAbout()
        {
            // no English name means no filename can be built for it
            var logger = new RecordingLogger();

            var table = new LanguageTable(Options(("zzz", "Some Font", "klingon")), logger);

            Assert.IsFalse(table.CanGenerate(new ISO639_3Code("zzz")));
            StringAssert.Contains(logger.Warnings.Single(), "zzz");
        }

        [TestMethod]
        [DataRow("", "polish")]
        [DataRow("Noto Serif", "")]
        public void ALanguageMissingItsTypesettingHalfIsDroppedAndWarnedAbout(String font, String babel)
        {
            var logger = new RecordingLogger();

            var table = new LanguageTable(Options(("pol", font, babel)), logger);

            Assert.IsFalse(table.CanGenerate(new ISO639_3Code("pol")));
            StringAssert.Contains(logger.Warnings.Single(), "pol");
        }

        [TestMethod]
        public void AnEagerLanguageWithNoUsableEntryIsWarnedAboutAndNotGenerated()
        {
            // the quiet failure this catches is asking for a language eagerly and
            // getting nothing, with no indication why
            var logger = new RecordingLogger();

            var options = Options(("pol", "Noto Serif", "polish"));
            options.EagerLanguages.Add("pol");
            options.EagerLanguages.Add("ben");

            var table = new LanguageTable(options, logger);

            CollectionAssert.AreEqual(
                new[] { new ISO639_3Code("pol") },
                table.EagerLanguages.ToArray());

            StringAssert.Contains(logger.Warnings.Single(), "ben");
        }

        [TestMethod]
        public void EagerLanguagesKeepTheirConfiguredOrder()
        {
            var options = Options(
                ("pol", "Noto Serif", "polish"),
                ("urd", "Noto Nastaliq Urdu", "urdu"),
                ("ben", "Noto Sans Bengali", "bengali"));

            options.EagerLanguages.AddRange(new[] { "ben", "pol", "urd" });

            CollectionAssert.AreEqual(
                new[] { new ISO639_3Code("ben"), new ISO639_3Code("pol"), new ISO639_3Code("urd") },
                new LanguageTable(options).EagerLanguages.ToArray());
        }

        [TestMethod]
        public void AnEmptyConfigurationGeneratesNothingAndSaysNothing()
        {
            // the English pipeline runs with no L2 configuration at all, so this must
            // be quiet rather than warning on every pass
            var logger = new RecordingLogger();

            var table = new LanguageTable(new L2Options(), logger);

            Assert.AreEqual(0, table.All.Count);
            Assert.AreEqual(0, table.EagerLanguages.Count);
            CollectionAssert.AreEqual(Array.Empty<String>(), logger.Warnings.ToArray());
        }
    }
}
