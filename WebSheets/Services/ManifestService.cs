using System.Net.Http.Json;
using WebSheets.Models;


namespace WebSheets.Services
{

    public class ManifestService
    {
        private readonly HttpClient _http;
        private FileNode? _cachedTree;

        public string CloudFrontBaseUrl { get; } =
            "https://d1bo9rmfj24lnn.cloudfront.net";

        public ManifestService(HttpClient http)
        {
            _http = http;
        }

        public async Task<FileNode> GetTreeAsync()
        {
            
            if (_cachedTree != null)
                return _cachedTree;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{CloudFrontBaseUrl}/manifest.txt");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode(); // will throw if not 2xx
            var manifest = await response.Content.ReadAsStringAsync();



            _cachedTree = BuildTree(manifest);
            return _cachedTree;
        }

        private FileNode BuildTree(string manifest)
        {
            var root = new FileNode
            {
                Name = "",
                IsDirectory = true
            };

            foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
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
                            IsDirectory = isDir
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
