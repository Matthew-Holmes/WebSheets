namespace SyntheticPDFs.Logic
{
    // Put the actual orchestration process in here
    // queuing etc. boilerplate is in the main class file

    public partial class Orchestrator
    {
        private async Task DoWorkAsync()
        {

            _logger.LogInformation("Work commencing");


            // pull git (use git repo manager)
            // get repo model (from git repo manager?)
            // decide what to generate (get this in separate file for business logic clarity)
            // create synthetic Tex source
            // pull git again - check nothing has changed
            // commit and push if can
            // backoff if push failure


            // Simulate real work
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}
