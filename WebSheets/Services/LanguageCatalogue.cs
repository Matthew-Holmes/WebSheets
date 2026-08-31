using Shared;

namespace WebSheets.Services
{
    // The languages the generator can produce, read from it rather than kept here.
    //
    // One list means the site cannot offer a language that would fail the moment
    // someone asked for it. The generator listens on loopback, so this call never
    // leaves the machine and needs no key.
    //
    // Reading it never blocks a page. Whatever is cached is served straight away and a
    // refresh happens behind it, the same way ManifestService serves its tree - a page
    // that waited on the generator would hang browsing on a service that has nothing to
    // do with it, and the worst a stale list can do is leave a language off a menu for
    // a few minutes.
    public class LanguageCatalogue : IDisposable
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LanguageCatalogue> _logger;

        private readonly CancellationTokenSource _stopping = new();

        // written whole, never mutated, so a reader always sees a complete list without
        // needing a lock of its own
        private volatile IReadOnlyList<LanguageInfo> _cached = Array.Empty<LanguageInfo>();

        private DateTime _fetched = DateTime.MinValue;
        private DateTime _retryAfter = DateTime.MinValue;

        // 1 while a refresh is in flight, so a burst of page views starts one, not twenty
        private int _refreshing;

        // the list changes only when the generator is reconfigured and restarted, so
        // this is about not calling it on every page view rather than about freshness
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

        // a failure has to be remembered as well as a success, or a generator that is
        // down is retried on every render of every page
        private static readonly TimeSpan RetryAfterFailureIn = TimeSpan.FromSeconds(30);

        // this is a call to loopback. if it has not answered in this long something is
        // wrong with it, and nothing should be waiting on it anyway
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public LanguageCatalogue(IHttpClientFactory httpClientFactory, ILogger<LanguageCatalogue> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            // fill it before anyone asks, so the first person to browse sees a complete
            // page rather than one missing its translations
            _ = Task.Run(RefreshPeriodicallyAsync);
        }

        // Never waits. Returns what is known now, and starts a refresh behind it if what
        // is known has gone stale.
        public IReadOnlyList<LanguageInfo> Get()
        {
            if (WorthFetching()) { StartRefresh(); }

            return _cached;
        }

        public LanguageInfo? Find(string code) =>
            Get().FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        private bool WorthFetching()
        {
            DateTime now = DateTime.UtcNow;

            if (now < _retryAfter) { return false; }

            return now - _fetched >= CacheFor;
        }

        private void StartRefresh()
        {
            // only one at a time - the guard is released by RefreshAsync itself
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) { return; }

            _ = Task.Run(RefreshAsync);
        }

        private async Task RefreshAsync()
        {
            try
            {
                var http = _httpClientFactory.CreateClient("SyntheticPDFsAPI");

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);

                deadline.CancelAfter(Timeout);

                var languages = await http.GetFromJsonAsync<List<LanguageInfo>>(
                    "languages", deadline.Token);

                if (languages is not null)
                {
                    _cached = languages;
                    _fetched = DateTime.UtcNow;
                    _retryAfter = DateTime.MinValue;

                    _logger.LogInformation(
                        "read {Count} language(s) from the generator", languages.Count);
                }
            }
            catch (Exception e)
            {
                // the generator being down must not show as an error to someone looking
                // for a worksheet. whatever was last known is kept and nothing tries
                // again until the backoff is up
                _logger.LogWarning(
                    "could not read the language list from the generator ({Message}) - "
                    + "serving what was last known for the next {Seconds}s",
                    e.Message, RetryAfterFailureIn.TotalSeconds);

                _retryAfter = DateTime.UtcNow + RetryAfterFailureIn;
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        // a slow trickle in the background, so the list stays current on a site nobody
        // happens to be browsing at the moment it changes
        private async Task RefreshPeriodicallyAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                StartRefresh();

                try
                {
                    await Task.Delay(CacheFor, _stopping.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _stopping.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
