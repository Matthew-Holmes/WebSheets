using SyntheticPDFs.Configuration;
using SyntheticPDFs.Models.Content;
using SyntheticPDFs.Rendering;
using System.Text;

namespace SyntheticPDFs.Tests
{
    // The font probe is generated from the shipped configuration but lives in the
    // content repository, where nothing regenerates it. Without these it goes quietly
    // stale the first time a language is added, and the run that was meant to prove the
    // container has every font stops mentioning the one that is missing.
    [TestClass]
    public class FontProbeTests
    {
        private const String WriteVariable = "WEBSHEETS_WRITE_FONTPROBE";

        [TestMethod]
        public void EveryConfiguredFontHasALetterToPrint()
        {
            // a font the probe cannot print anything in is the empty-subset crash again:
            // fatal, in the build of the content repository, naming only a font file
            CollectionAssert.AreEqual(
                Array.Empty<String>(),
                FontProbe.FontsWithNoSample(ConfiguredLanguagesTests.Shipped()).ToArray(),
                "every configured font needs a sample letter in FontProbe.Samples");
        }

        [TestMethod]
        public void TheFontProbeSaysWhatTheConfigurationSays()
        {
            String path     = Location();
            String expected = FontProbe.Render(ConfiguredLanguagesTests.Shipped());

            if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
            {
                File.WriteAllText(path, expected, new UTF8Encoding(false));
            }

            // written with newlines, so compare that way whatever git left on disk
            String actual = File.ReadAllText(path).Replace("\r\n", "\n");

            Assert.AreEqual(expected, actual,
                $"{path} no longer matches the configuration it was generated from. "
                + $"Set {WriteVariable}=1 and run this test again to rewrite it, then "
                + "copy it into the content repository.");
        }

        // the test runs from bin/, so walk up to the repository - see the same walk in
        // ConfiguredLanguagesTests for why this is not a copied file
        private static String Location()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                String candidate = Path.Combine(
                    directory.FullName,
                    "docs", "contentRepo", "latex", "test", "eal", "fontProbe.tex");

                if (File.Exists(candidate)) { return candidate; }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "could not find docs/contentRepo/latex/test/eal/fontProbe.tex above "
                + AppContext.BaseDirectory);
        }
    }
}
