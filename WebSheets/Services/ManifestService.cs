using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using WebSheets.Configuration;
using WebSheets.Models;

namespace WebSheets.Services
{
    public class ManifestService
    {
        private readonly ILogger<ManifestService> _logger;


        private readonly IAmazonS3 _s3;
        private readonly string _bucketName;
        private FileNode? _cachedTree;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly SemaphoreSlim _innerLock = new(1, 1);

        public ManifestService(IAmazonS3 s3, ILogger<ManifestService> logger, IOptions<WorksheetSourceOptions> options)
        {
            _s3 = s3;
            _bucketName = options.Value.ObjectStoreBucketName;

            // Start background hourly refresh
            _ = Task.Run(UpdateCachePeriodically);
            _logger = logger;
        }

        /// <summary>
        /// Builds a short-lived, signed download URL for an object in the store,
        /// so a browser can fetch a private object directly without holding the
        /// access key itself. Signing is a local computation, not a network call.
        /// </summary>
        public string GetPresignedFileUrl(string key, TimeSpan? expiresIn = null)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(15)),
            };

            return _s3.GetPreSignedURL(request);
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
                using var response = await _s3.GetObjectAsync(_bucketName, "manifest.txt");
                using var reader = new StreamReader(response.ResponseStream);
                var manifest = await reader.ReadToEndAsync();

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
