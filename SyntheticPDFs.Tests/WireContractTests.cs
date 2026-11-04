using Shared;
using System.Text.Json;

namespace SyntheticPDFs.Tests
{
    // Every reply the generator sends the website, written and read back the way the
    // two of them do it: Results.Ok on one side, ReadFromJsonAsync on the other, both
    // with the web defaults.
    //
    // A type that writes but will not read fails only in production. The generator
    // answers, the work goes ahead, and the website then throws reading the answer -
    // which is how every ping from the content repository came back as a bare 500 while
    // the generator carried on regardless. Two public constructors on PingResult were
    // enough to cause it: System.Text.Json will not choose between them.
    [TestClass]
    public class WireContractTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static T RoundTrip<T>(T value)
        {
            String json = JsonSerializer.Serialize(value, Web);

            T? read = JsonSerializer.Deserialize<T>(json, Web);

            Assert.IsNotNull(read, $"{typeof(T).Name} read back as null from {json}");

            return read;
        }

        [TestMethod]
        public void APingResultSurvivesTheTrip()
        {
            var sent = new PingResult(
                PingOutcome.Queued, IsRunning: true, IsQueued: true,
                Problems: new List<String> { "latex/dictionary/mathematicalDictionary.tex: line 4" });

            var read = RoundTrip(sent);

            Assert.AreEqual(sent.Outcome, read.Outcome);
            Assert.AreEqual(sent.IsRunning, read.IsRunning);
            Assert.AreEqual(sent.IsQueued, read.IsQueued);
            CollectionAssert.AreEqual(sent.Problems.ToList(), read.Problems.ToList());
        }

        [TestMethod]
        public void AGenerateResultSurvivesTheTrip()
        {
            var sent = new GenerateResult(
                GenerateOutcome.Queued, "queued", new List<String> { "a.tex", "b.tex" });

            var read = RoundTrip(sent);

            Assert.AreEqual(sent.Outcome, read.Outcome);
            Assert.AreEqual(sent.Message, read.Message);
            CollectionAssert.AreEqual(sent.Queued.ToList(), read.Queued.ToList());
        }

        [TestMethod]
        public void APurgeResultSurvivesTheTrip()
        {
            var sent = new PurgeResult(true, "removed 2", new List<String> { "a.tex", "b.tex" });

            var read = RoundTrip(sent);

            Assert.AreEqual(sent.Removed, read.Removed);
            Assert.AreEqual(sent.Message, read.Message);
            CollectionAssert.AreEqual(sent.Files.ToList(), read.Files.ToList());
        }

        [TestMethod]
        public void TheLanguageListSurvivesTheTrip()
        {
            var sent = new List<LanguageInfo>
            {
                new("zht", "Chinese (Traditional)", "chinesetraditional", false, false),
                new("urd", "Urdu", "urdu", true, true),
            };

            var read = RoundTrip(sent);

            CollectionAssert.AreEqual(sent, read);
        }
    }
}
