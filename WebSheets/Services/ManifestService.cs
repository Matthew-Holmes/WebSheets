using Microsoft.Extensions.Options;
using WebSheets.Configuration;
using WebSheets.Models;

namespace WebSheets.Services
{
    public class ManifestService
    {
        private readonly ILogger<ManifestService> _logger;


        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _manifestUrl;
        private FileNode? _cachedTree;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _innerLock = new(1, 1);

        public ManifestService(IHttpClientFactory httpClientFactory, ILogger<ManifestService> logger, IOptions<WorksheetSourceOptions> options)
        {
            _httpClientFactory = httpClientFactory;

            // manifest.txt is public, served by the same anonymous endpoint as the
            // PDFs themselves, so this needs no credentials and no S3 client.
            _manifestUrl = $"{options.Value.PublicDownloadBaseUrl.TrimEnd('/')}/manifest.txt";

            // Start background hourly refresh
            _ = Task.Run(UpdateCachePeriodically);
            _logger = logger;
        }

        public async Task<FileNode> GetTreeAsync()
        {
            var now = DateTime.UtcNow;

            if (_cachedTree == null)
            {
                await _lock.WaitAsync();

                if (_cachedTree is not null)
                {
                    _logger.LogInformation("cache updated in time it took to aquire semaphore lock!");
                    _lock.Release();
                    return _cachedTree;
                }

                try
                {
                    _logger.LogInformation("no cached tree... refreshing");
                    await RefreshCacheAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                }
                finally
                {
                    _lock.Release();
                }
            }
            else if ((now - _lastRequest).TotalMinutes >= 1 /* serve and refresh tree in background */ )
            {
                _logger.LogInformation("updating tree, but serving old");
                Task.Run(RefreshCacheAsync);
            }

            return _cachedTree!;

        }

        private async Task RefreshCacheAsync()
        {
            await _innerLock.WaitAsync();

            _logger.LogInformation("refresh cache acquired lock");

            Exception? rethrow = null;

            try
            {
                var manifest = await _httpClientFactory.CreateClient().GetStringAsync(_manifestUrl);

                _lastRequest = DateTime.UtcNow;

                _cachedTree = BuildTree(manifest);

                _logger.LogInformation("refreshed cached tree");

            } 
            catch (Exception e)
            {
                rethrow = e;
            } 
            finally
            {
                _innerLock.Release();

                if (rethrow is not null)
                {
                    throw rethrow!;
                }
            }
            
        }

        private async Task UpdateCachePeriodically()
        {
            while (true)
            {
                // Wait 1 hour before next refresh

                await Task.Delay(TimeSpan.FromHours(1));

                _logger.LogInformation("background tree cache refresh commenced");

                try
                {
                    await _lock.WaitAsync();
                    await RefreshCacheAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                }
                finally
                {
                    _lock.Release();
                }

            }
        }

        private FileNode BuildTree(string manifest)
        {
            var root = new FileNode
            {
                Name = "",
                IsDirectory = true,
                Parent = null,
            };

            foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line == "manifest.txt") continue;

                var parts = line.Trim().Split('/');
                var current = root;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    var isDir = i < parts.Length - 1;

                    if (!current.Children.TryGetValue(part, out var node))
                    {
                        node = new FileNode
                        {
                            Name = part,
                            IsDirectory = isDir,
                            Parent = current,
                        };
                        current.Children[part] = node;
                    }

                    current = node;
                }
            }

            return root;
        }
    }
}
