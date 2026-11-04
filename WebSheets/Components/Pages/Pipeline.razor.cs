using Microsoft.AspNetCore.Components;
using Shared;

namespace WebSheets.Components.Pages;

// What the generator is doing, what it has left and roughly how long that will take, for
// whoever runs the site. Not on the menu, like the coverage page: it answers questions
// only the person running the pipeline asks.
//
// Read from the generator over loopback every few seconds while the page is open. Every
// figure is worked out by the generator, so this page only lays them out.
public partial class Pipeline : ComponentBase, IDisposable
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public ILogger<Pipeline> Logger { get; set; } = default!;

    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);

    // a call to loopback that has not answered in this long is not going to
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    protected PipelineStatus? Status;

    // why the last read failed, when it did - the last good one stays on show beneath it
    protected string? Unreachable;

    protected bool Loaded;

    private readonly CancellationTokenSource _closing = new();

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // only once the page is live in a browser - a prerendered page has nobody to update
        if (firstRender) { _ = KeepRefreshingAsync(); }
    }

    private async Task KeepRefreshingAsync()
    {
        using var timer = new PeriodicTimer(RefreshEvery);

        try
        {
            while (await timer.WaitForNextTickAsync(_closing.Token))
            {
                await RefreshAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // the page was closed
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);

            deadline.CancelAfter(Timeout);

            var http = HttpClientFactory.CreateClient("SyntheticPDFsAPI");

            Status = await http.GetFromJsonAsync<PipelineStatus>("status", deadline.Token);
            Unreachable = null;
        }
        catch (Exception e) when (!_closing.IsCancellationRequested)
        {
            Logger.LogDebug("could not read the pipeline status: {Message}", e.Message);

            Unreachable = e is OperationCanceledException
                ? "It did not answer within a few seconds."
                : e.Message;
        }

        Loaded = true;
    }

    public void Dispose() => _closing.Cancel();

    #region Words

    protected static string StateName(PipelineState state) => state switch
    {
        PipelineState.Working        => "Working",
        PipelineState.WaitingToRetry => "Waiting to retry",
        PipelineState.Stopped        => "Stopped",
        _                            => "Idle",
    };

    protected static string BadgeClass(PipelineState state) => state switch
    {
        PipelineState.Working        => "bg-primary",
        PipelineState.WaitingToRetry => "bg-warning text-dark",
        PipelineState.Stopped        => "bg-danger",
        _                            => "bg-secondary",
    };

    protected static string ItemName(PassItemState state) => state switch
    {
        PassItemState.Done   => "done",
        PassItemState.Failed => "failed",
        _                    => "working",
    };

    protected static string ItemBadge(PassItemState state) => state switch
    {
        PassItemState.Done   => "bg-success",
        PassItemState.Failed => "bg-danger",
        _                    => "bg-light text-dark",
    };

    protected static string Outcome(PassRecord pass) => pass.Result switch
    {
        PassResult.Generated when pass.Failed == 0
            => $"made {Count(pass.Made, "file")}",

        PassResult.Generated
            => $"made {Count(pass.Made, "file")}, {pass.Failed} failed",

        PassResult.RemovedStaleFiles
            => $"removed {Count(pass.Removed, "out-of-date file")}",

        PassResult.NothingToDo
            => "nothing left to do",

        PassResult.RepositoryMoved
            => "the repository moved under it, so it backed off to retry",

        PassResult.AllFailed
            => $"all {Count(pass.Failed, "file")} failed, so it stopped",

        _   => $"crashed: {pass.Error}",
    };

    protected static string RowClass(PassResult result) => result switch
    {
        PassResult.AllFailed or PassResult.Crashed => "table-danger",
        PassResult.RepositoryMoved                 => "table-warning",
        _                                          => "",
    };

    // whether the forecast's times were measured on this run or are typical figures
    protected static string Basis(PipelineForecast forecast)
    {
        int timed = forecast.Stages.Count(s => s.TimedPasses > 0);

        if (timed == forecast.Stages.Count) { return "timed from this run's own passes"; }

        if (timed == 0) { return "from typical pass times, since nothing like it has run since the generator started"; }

        return "partly from this run's own passes, partly from typical pass times";
    }

    protected static string TimedFrom(ForecastStage stage) =>
        stage.TimedPasses > 0 ? $"the last {Count(stage.TimedPasses, "pass")}" : "a typical pass";

    protected static string Count(int count, string thing) =>
        count == 1 ? $"1 {thing}" : $"{count:N0} {Plural(thing)}";

    private static string Plural(string thing) =>
        thing.EndsWith("ss") ? thing + "es" : thing + "s";

    // "40 s", "12 min", "2 h 5 min" - no finer than the figures deserve
    protected static string Rough(TimeSpan span)
    {
        if (span < TimeSpan.Zero) { span = TimeSpan.Zero; }

        if (span.TotalSeconds < 60) { return $"{(int)span.TotalSeconds} s"; }

        if (span.TotalMinutes < 60) { return $"{(int)span.TotalMinutes} min"; }

        int hours = (int)span.TotalHours;

        return span.Minutes == 0 ? $"{hours} h" : $"{hours} h {span.Minutes} min";
    }

    protected string Ago(DateTimeOffset when) => Rough(Status!.At - when) + " ago";

    // an integer, so that a culture with a decimal comma cannot break the CSS width
    protected static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);

    #endregion
}
