namespace SyntheticPDFs.Logic
{
    // Put the actual orchestration process in here
    // queuing etc. boilerplate is in the main class file

    public partial class Orchestrator
    {
        private async Task DoWorkAsync()
        {

            _logger.LogInformation("Work commencing");
            // Simulate real work
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}
