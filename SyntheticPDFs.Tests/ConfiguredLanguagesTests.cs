using Microsoft.Extensions.Configuration;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using System.Text.RegularExpressions;

namespace SyntheticPDFs.Tests
{
    // The shipped appsettings.json, checked against the code that reads it.
    //
    // Every one of these is a mistake that costs nothing at startup and shows up much
    // later as a language quietly missing from the site, or as a file that will not
    // compile the first time somebody asks for it. There are fifty-odd entries in that
    // file now and no other way to notice a typo in one of them.
    [TestClass]
    public class ConfiguredLanguagesTests
    {
        internal static L2Options Shipped()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(FindAppSettings(), optional: false)
                .Build();

            L2Options? options = configuration
                .GetSection(L2Options.SectionName)
                .Get<L2Options>();

            Assert.IsNotNull(options, $"appsettings.json has no usable {L2Options.SectionName} section");

            return options;
        }

        // the test runs from bin/, so walk up to the repository rather than copying the
        // file into the output - a copy would be the thing under test going stale
        private static String FindAppSettings()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                String candidate = Path.Combine(
                    directory.FullName, "SyntheticPDFs", "appsettings.json");

                if (File.Exists(candidate)) { return candidate; }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "could not find SyntheticPDFs/appsettings.json above " + AppContext.BaseDirectory);
        }

        [TestMethod]
        public void EveryConfiguredLanguageCanActuallyBeNamed()
        {
            // a code the names table does not know is dropped by LanguageTable with a
            // warning nobody reads, and the language simply never appears on the site
            foreach (String code in Shipped().Languages.Keys)
            {
                Assert.IsTrue(LanguageNames.IsKnown(code),
                    $"'{code}' is configured but LanguageNames cannot name it");
            }
        }

        [TestMethod]
        public void EveryConfiguredLanguageCanActuallyBeTypeset()
        {
            // the other half of the same silent drop: no font or no babel name and the
            // entry is ignored
            foreach (var (code, language) in Shipped().Languages)
            {
                Assert.IsFalse(String.IsNullOrWhiteSpace(language.Font),
                    $"'{code}' has no font");
                Assert.IsFalse(String.IsNullOrWhiteSpace(language.BabelName),
                    $"'{code}' has no babel name");
            }
        }

        [TestMethod]
        public void NothingConfiguredIsSilentlyDropped()
        {
            // the two checks above, made once more through the class that does the
            // dropping, so that a rule added there is caught here too
            L2Options options = Shipped();

            var table = new LanguageTable(options);

            CollectionAssert.AreEquivalent(
                options.Languages.Keys.ToList(),
                table.All.Select(c => c.Code).ToList(),
                "every configured language must survive into the table the site reads");
        }

        [TestMethod]
        public void EveryEagerLanguageIsOneWeCanProduce()
        {
            // an eager language that cannot be produced is worse than a missing one:
            // the plan asks for it on every root, on every pass, for ever
            L2Options options = Shipped();

            var table = new LanguageTable(options);

            foreach (String code in options.EagerLanguages)
            {
                Assert.IsTrue(table.CanGenerate(new ISO639_3Code(code)),
                    $"'{code}' is eager but is not configured");
            }

            Assert.AreEqual(options.EagerLanguages.Count, table.EagerLanguages.Count);
        }

        [TestMethod]
        public void EveryCodeAndNameFitsAFilename()
        {
            // the code becomes a directory and the name is joined onto a camelCase
            // filename, and the parser reads both back out again
            foreach (String code in Shipped().Languages.Keys)
            {
                Assert.AreEqual(3, code.Length, $"'{code}' is not a three letter code");

                String name = LanguageNames.EnglishNameOf(code)!;

                Assert.IsTrue(Regex.IsMatch(name, "^[a-z]+$"),
                    $"'{name}' cannot go into a filename - lower case letters only");
            }
        }

        [TestMethod]
        public void TheSiteIsOfferingTheWholeList()
        {
            // a blunt guard against the list being cut down by accident. it is meant to
            // be about fifty languages; if it is deliberately fewer, change the number
            Assert.IsTrue(Shipped().Languages.Count >= 50,
                "the site is meant to offer around fifty languages");
        }
    }
}
