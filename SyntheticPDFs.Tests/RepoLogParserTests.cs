using SyntheticPDFs.Git;

namespace SyntheticPDFs.Tests
{
    [TestClass]
    public class RepoLogParserTests
    {
        private const String Hash = "0123456789abcdef0123456789abcdef01234567";

        // git log --all --oneline --name-only --format="%H" puts the hash first,
        // then the files that commit touched. newest commit first.
        private const String Log = """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            latex/sheetB.tex
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            latex/sheetA.tex
            latex/deleted.tex
            cccccccccccccccccccccccccccccccccccccccc
            latex/sheetA.tex
            latex/oldest.tex
            """;

        // find ./ output, prefixed with ./ and including non-source paths
        private const String Live = """
            ./
            ./README.md
            ./latex
            ./latex/sheetA.tex
            ./latex/sheetB.tex
            ./latex/oldest.tex
            """;

        private static Dictionary<String, int> Parse()
            => RepoLogParser.Parse(Log, Live, Hash, "latex")
                .Contents.ToDictionary(tf => tf.FullPath, tf => tf.AgeCommits);

        [TestMethod]
        public void KeepsOnlyLiveSourceFiles()
        {
            var ages = Parse();

            CollectionAssert.AreEquivalent(
                new[] { "latex/sheetA.tex", "latex/sheetB.tex", "latex/oldest.tex" },
                ages.Keys.ToArray());
        }

        [TestMethod]
        public void DeletedFilesAreExcludedEvenThoughTheyAreInTheLog()
        {
            Assert.IsFalse(Parse().ContainsKey("latex/deleted.tex"));
        }

        [TestMethod]
        public void FilesOutsideTheSourceDirectoryAreExcluded()
        {
            Assert.IsFalse(Parse().ContainsKey("README.md"));
        }

        [TestMethod]
        public void MostRecentlyTouchedFileIsYoungest()
        {
            var ages = Parse();

            Assert.IsTrue(
                ages["latex/sheetB.tex"] < ages["latex/sheetA.tex"],
                $"sheetB ({ages["latex/sheetB.tex"]}) should be younger than sheetA ({ages["latex/sheetA.tex"]})");
        }

        [TestMethod]
        public void OnlyTheMostRecentEditCounts()
        {
            // sheetA appears in both the second and third commits; the younger wins
            var ages = Parse();

            Assert.AreEqual(2, ages["latex/sheetA.tex"]);
        }

        [TestMethod]
        public void FilesFromTheOldestCommitAreStillTracked()
        {
            // the final commit's files never hit a boundary line, so they used to be dropped
            // entirely - anything untouched since the initial commit was invisible
            var ages = Parse();

            Assert.IsTrue(
                ages.ContainsKey("latex/oldest.tex"),
                "a file last touched in the oldest commit must still appear in the model");
        }

        [TestMethod]
        public void EmptyRepoParsesToNothing()
        {
            var model = RepoLogParser.Parse("", "", Hash, "latex");

            Assert.AreEqual(0, model.Contents.Count);
            Assert.AreEqual(Hash, model.LastCommitHash);
        }
    }
}
