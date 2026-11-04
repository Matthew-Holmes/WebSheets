using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared;
using SyntheticPDFs.Configuration;
using SyntheticPDFs.Logic;
using SyntheticPDFs.Rendering;
using SyntheticPDFs.Services;
using SyntheticPDFs.Tests.Fakes;

namespace SyntheticPDFs.Tests
{
    // What the status page is told: the pass running now, the passes before it, and the
    // forecast of what is left - which has to follow the same rules the passes do, or the
    // page promises work in an order that never happens.
    [TestClass]
    public class PipelineStatusTests
    {
        private const String Root = "latex/worksheets/algebra/quadratics";

        private const String Worked = "worksheets/algebra/quadratics_workedSolutions.tex";
        private const String Answers = "worksheets/algebra/quadratics_solutions.tex";

        private FakeGitRepoManager _git = null!;
        private FakeLLMService _llm = null!;
        private ManualClock _clock = null!;

        [TestInitialize]
        public void Setup()
        {
            _git = new FakeGitRepoManager();
            _llm = new FakeLLMService();
            _clock = new ManualClock();
        }

        private Orchestrator Build(ILLMService llm, int maxFilesPerRun = 30, L2Options? l2 = null) =>
            new Orchestrator(
                NullLogger<Orchestrator>.Instance,
                _git,
                llm,
                Options.Create(new GenerationOptions { MaxFilesPerRun = maxFilesPerRun }),
                l2 is null ? null : Options.Create(l2),
                time: _clock);

        private static L2Options PolishEagerly()
        {
            var options = new L2Options { GenerateVocabularyKeys = true };

            options.Languages["pol"] = new LanguageOptions { Font = "Noto Serif", BabelName = "polish" };
            options.Languages["ben"] = new LanguageOptions { Font = "Noto Sans Bengali", BabelName = "bengali" };

            options.EagerLanguages.Add("pol");

            return options;
        }

        // ---- at rest ----

        [TestMethod]
        public void AnIdleGeneratorSaysSoAndForecastsNothing()
        {
            var status = Build(_llm).Status();

            Assert.AreEqual(PipelineState.Idle, status.State);
            Assert.AreEqual("waiting for a ping", status.Activity);
            Assert.IsNull(status.CurrentPass);
            Assert.IsNull(status.Forecast);
            Assert.AreEqual(0, status.RecentPasses.Count);
            Assert.AreEqual(_clock.Now, status.RunningSince);
        }

        // ---- part way through a pass ----

        [TestMethod]
        public async Task APassPartWayThroughShowsItsFilesAndWhatIsLeft()
        {
            _git.AddFile(Root + ".tex", ageCommits: 1);

            var gated = new GatedLLMService(_llm) { Tokens = (1000, 200) };
            var orchestrator = Build(gated);

            Task<Orchestrator.PassOutcome> pass = orchestrator.DoOnePassAsync();

            await gated.Reached();

            _clock.Advance(TimeSpan.FromSeconds(30));
            gated.Tokens = (1500, 300);

            var status = orchestrator.Status();

            Assert.AreEqual(PipelineState.Working, status.State);
            Assert.AreEqual("making 1 file: English sheets", status.Activity);

            Assert.IsNotNull(status.CurrentPass);
            Assert.AreEqual(1, status.CurrentPass.Number);

            PassItem item = status.CurrentPass.Items.Single();

            Assert.AreEqual(Worked, item.File);
            Assert.AreEqual(PassItemState.Running, item.State);
            Assert.AreEqual(TimeSpan.FromSeconds(30), item.Elapsed);

            // the worked solutions now and the answers after them, each a pass of English work
            Assert.IsNotNull(status.Forecast);
            Assert.AreEqual(2, status.Forecast.Files);
            Assert.AreEqual(2, status.Forecast.Passes);

            ForecastStage stage = status.Forecast.Stages.Single();

            Assert.AreEqual("English sheets", stage.Work);
            Assert.AreEqual(2, stage.Passes);
            CollectionAssert.AreEqual(new[] { Worked, Answers }, stage.Jobs.ToArray());
            Assert.AreEqual(0, stage.TimedPasses, "nothing has been timed yet, so the figure is a typical one");

            // two typical English passes, less the thirty seconds this one has already run
            Assert.AreEqual(TimeSpan.FromSeconds(64 * 2 - 30), status.Forecast.Remaining);

            gated.Open();

            Assert.AreEqual(Orchestrator.PassOutcome.Generated, await pass);

            var after = orchestrator.Status();

            Assert.AreEqual(PipelineState.Idle, after.State);
            Assert.IsNull(after.CurrentPass);
            Assert.IsNull(after.Forecast, "a forecast is only shown while there is work going on");

            PassRecord record = after.RecentPasses.Single();

            Assert.AreEqual(PassResult.Generated, record.Result);
            Assert.AreEqual("English sheets", record.Work);
            Assert.AreEqual(1, record.Made);
            Assert.AreEqual(0, record.Failed);
            Assert.AreEqual(TimeSpan.FromSeconds(30), record.Took);
            Assert.AreEqual(500, record.PromptTokens, "what the pass cost, not the running total");
            Assert.AreEqual(100, record.CompletionTokens);

            Assert.AreEqual(1500, after.PromptTokens);
            Assert.AreEqual(300, after.CompletionTokens);
        }

        [TestMethod]
        public async Task APassTimedThisRunReplacesTheTypicalFigure()
        {
            _git.AddFile(Root + ".tex", ageCommits: 1);

            var gated = new GatedLLMService(_llm);
            var orchestrator = Build(gated);

            Task<Orchestrator.PassOutcome> first = orchestrator.DoOnePassAsync();
            await gated.Reached();
            _clock.Advance(TimeSpan.FromSeconds(100));
            gated.Open();
            await first;

            gated.Close();

            Task<Orchestrator.PassOutcome> second = orchestrator.DoOnePassAsync();
            await gated.Reached();

            ForecastStage stage = orchestrator.Status().Forecast!.Stages.Single();

            Assert.AreEqual(1, stage.Passes, "only the answers are left");
            Assert.AreEqual(1, stage.TimedPasses);
            Assert.AreEqual(TimeSpan.FromSeconds(100), stage.Estimated,
                "the English pass just timed, not the typical 64 seconds");

            gated.Open();
            await second;
        }

        // ---- the forecast follows the passes ----

        [TestMethod]
        public async Task TheForecastIsTheOrderThePassesActuallyRunIn()
        {
            // three sheets, two files a pass: the batch cap and the one-step-a-pass rule
            // both decide which file goes when
            foreach (String name in new[] { "a", "b", "c" })
            {
                _git.AddFile($"latex/worksheets/{name}.tex", ageCommits: 1);
            }

            var orchestrator = Build(_llm, maxFilesPerRun: 2);

            await orchestrator.DoOnePassAsync();

            List<String[]> forecast = orchestrator.Monitor.LatestForecast.Passes
                .Select(p => p.Jobs.Select(j => j.Description).ToArray())
                .ToList();

            while (await orchestrator.DoOnePassAsync() == Orchestrator.PassOutcome.Generated) { }

            List<String[]> actual = _git.CommitCalls
                .Select(commit => commit.Select(ts => ts.FileNameFullPath["latex/".Length..]).ToArray())
                .ToList();

            Assert.AreEqual(actual.Count, forecast.Count, "as many passes as were forecast");

            for (int i = 0; i < actual.Count; i++)
            {
                CollectionAssert.AreEquivalent(actual[i], forecast[i], $"pass {i + 1}");
            }
        }

        [TestMethod]
        public async Task StaleFilesAreForecastAsARemovalPassFirst()
        {
            // worked solutions older than the root they were made from
            _git.AddFile(Root + ".tex", ageCommits: 1);
            _git.AddFile(Root + "_workedSolutions.tex", ageCommits: 5);

            var orchestrator = Build(_llm);

            Assert.AreEqual(Orchestrator.PassOutcome.RemovedStaleFiles, await orchestrator.DoOnePassAsync());

            var passes = orchestrator.Monitor.LatestForecast.Passes;

            Assert.IsNull(passes[0].Work, "the removal comes first, and makes nothing");
            CollectionAssert.AreEqual(new[] { Worked }, passes[0].Jobs.Select(j => j.Description).ToArray());

            CollectionAssert.AreEqual(
                new[] { Worked, Answers },
                passes.Skip(1).SelectMany(p => p.Jobs).Select(j => j.Description).ToArray(),
                "and then the worked solutions are made again, with the answers after them");

            PassRecord record = orchestrator.Status().RecentPasses.Single();

            Assert.AreEqual(PassResult.RemovedStaleFiles, record.Result);
            Assert.AreEqual(1, record.Removed);
        }

        [TestMethod]
        public async Task VocabularyKeysBringTheDictionaryPassesTheyUsuallyNeed()
        {
            _git.AddFile("latex/dictionary/mathematicalDictionary.tex", ageCommits: 9,
                contents: @"\dictentry{numerator}{the number above the line in a fraction}");
            _git.AddFile(Root + ".tex", ageCommits: 1);

            var orchestrator = Build(_llm, l2: PolishEagerly());

            await orchestrator.DoOnePassAsync();

            var passes = orchestrator.Monitor.LatestForecast.Passes;

            CollectionAssert.AreEqual(
                new[]
                {
                    Orchestrator.GenerationPriority.English,              // worked solutions
                    Orchestrator.GenerationPriority.English,              // answers
                    Orchestrator.GenerationPriority.EnglishGlossary,
                    Orchestrator.GenerationPriority.SharedDictionary,     // the new words, expected
                    Orchestrator.GenerationPriority.TranslatedDictionary,
                    Orchestrator.GenerationPriority.TranslatedGlossary,
                    Orchestrator.GenerationPriority.Translated,           // parallel text and key words only
                },
                passes.Select(p => p.Work!.Value).ToArray());

            StringAssert.EndsWith(passes[3].Jobs.Single().Description, "(expected)");
            Assert.AreEqual(2, passes[6].Jobs.Count);
            Assert.AreEqual(0, orchestrator.Monitor.LatestForecast.Waiting);
        }

        // ---- requests and failures ----

        [TestMethod]
        public async Task ARequestShowsUntilItIsMadeAndIsForecastFirst()
        {
            const String A = "latex/worksheets/a";

            _git.AddFile(A + ".tex", 4);
            _git.AddFile(A + "_workedSolutions.tex", 3);
            _git.AddFile(A + "_solutions.tex", 2);
            _git.AddFile(A + "_vocab.tex", 1, TexFixtures.VocabularyKey(A, new[]
            {
                new VocabTerm
                {
                    English = "fraction", Definition = "part of a whole",
                    Translation = "ulamek", TranslatedDefinition = "czesc calosci",
                },
            }));

            var gated = new GatedLLMService(_llm);
            var orchestrator = Build(gated, l2: PolishEagerly());

            // asking starts a pass of its own, which the gate holds at its first call
            orchestrator.RequestGeneration(new GenerateRequest
            {
                RootName = A, Language = "ben", Part = "Root", Form = "ParallelText",
            });

            try
            {
                var asked = orchestrator.Status();

                RequestedFile parallel = asked.Requests.Single(r => r.File.EndsWith("a_bengaliParallelText.tex"));

                Assert.AreEqual("worksheets/a/L2/ben/a_bengaliParallelText.tex", parallel.File);
                Assert.AreEqual(_clock.Now, parallel.AskedAt);

                await gated.Reached();

                var working = orchestrator.Status();

                Assert.AreEqual("files asked for on the website", working.CurrentPass?.Work);
                Assert.AreEqual("files asked for on the website", working.Forecast?.Stages[0].Work);
            }
            finally
            {
                gated.Open();
            }
        }

        [TestMethod]
        public async Task APassThatMakesNothingStopsAndSaysWhy()
        {
            _git.AddFile(Root + ".tex", ageCommits: 1);
            _llm.DefaultResponse = "not tex at all, just prose";

            var orchestrator = Build(_llm);

            Assert.AreEqual(Orchestrator.PassOutcome.GenerationFailed, await orchestrator.DoOnePassAsync());

            var status = orchestrator.Status();

            Assert.AreEqual(PipelineState.Stopped, status.State);
            StringAssert.Contains(status.Activity, "stopped");

            PassRecord record = status.RecentPasses.Single();

            Assert.AreEqual(PassResult.AllFailed, record.Result);
            Assert.AreEqual(0, record.Made);
            Assert.AreEqual(1, record.Failed);
        }

        // ---- test doubles ----

        private sealed class ManualClock : TimeProvider
        {
            internal DateTimeOffset Now { get; private set; } =
                new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;

            internal void Advance(TimeSpan by) => Now += by;
        }

        // holds every call to the model until the test lets it go, so that a pass can be
        // looked at part way through
        private sealed class GatedLLMService : ILLMService
        {
            private readonly FakeLLMService _inner;

            private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal GatedLLMService(FakeLLMService inner) => _inner = inner;

            internal (long Prompt, long Completion) Tokens { get; set; }

            public (long Prompt, long Completion) TokensUsed => Tokens;

            // once a pass has asked the model for anything
            internal async Task Reached()
            {
                Task first = await Task.WhenAny(_entered.Task, Task.Delay(TimeSpan.FromSeconds(10)));

                Assert.AreSame(_entered.Task, first, "the pass never asked the model for anything");
            }

            internal void Open() => _gate.TrySetResult();

            // ready to hold the next pass
            internal void Close()
            {
                _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private async Task<T> Held<T>(Func<Task<T>> call)
            {
                _entered.TrySetResult();

                await _gate.Task;

                return await call();
            }

            public Task<String> GetResponse(String prompt) => Held(() => _inner.GetResponse(prompt));

            public Task<ReviewVerdict?> GetReviewResponse(String question) =>
                Held(() => _inner.GetReviewResponse(question));

            public Task<String> GetSummaryResponse(String prompt) =>
                Held(() => _inner.GetSummaryResponse(prompt));

            public Task<String> GetStructuredResponse(String prompt) =>
                Held(() => _inner.GetStructuredResponse(prompt));

            public void Log(LogLevel lvl, String message) => _inner.Log(lvl, message);
        }
    }
}
