using Polly;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssettoServer.Server.UserGroup
{
    public class ApiBasedUserGroup : IListableUserGroup, IDisposable
    {
        public IReadOnlyCollection<ulong> List { get; private set; }

        public delegate ApiBasedUserGroup Factory(string name, string url);

        private readonly string _name;
        private readonly string _url;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly ConcurrentDictionary<ulong, bool> _guidList = new();

        public event EventHandler<IUserGroup, EventArgs>? Changed;

        public ApiBasedUserGroup(SignalHandler signalHandler, string name, string url)
        {
            List = _guidList.Keys.ToList();
            _url = url;
            _name = name;
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Log.Error(e.GetException(), "Failed pulling api {Name}", _url);
        }

        public async Task LoadAsync()
        {
            var policy = Policy.Handle<IOException>().WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(attempt * 100));

            await _lock.WaitAsync();
            try
            {
                using var httpClient = new HttpClient();
                try
                {
                    var response = await policy.ExecuteAsync(() => httpClient.GetAsync(_url));
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    _guidList.Clear();
                    foreach (string guidStr in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!ulong.TryParse(guidStr, out ulong guid)) continue;

                        //Log.Information("Loaded GUID {Guid} from {_url}", guid, _url);

                        if (_guidList.ContainsKey(guid))
                        {
                            Log.Warning("Duplicate entry in {url}: {Guid}", _url, guid);
                        }
                        _guidList[guid] = true;
                    }
                }
                catch (HttpRequestException ex)
                {
                    Log.Error(ex, "Failed to fetch API data from {url}", _url);
                }

                List = _guidList.Keys.ToList();
                Log.Debug("Loaded {Name} with {Count} entries", _name, _guidList.Count);

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading {url}", _url);
            }
            finally
            {
                _lock.Release();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<bool> ContainsAsync(ulong guid)
        {
            await _lock.WaitAsync();
            try
            {
                return _guidList.ContainsKey(guid);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> AddAsync(ulong guid)
        {
            await _lock.WaitAsync();
            try
            {
                if (_guidList.TryAdd(guid, true))
                    await File.AppendAllLinesAsync(_url, new[] { guid.ToString() });
            }
            finally
            {
                _lock.Release();
            }

            return true;
        }

        public void Dispose()
        {
            _lock.Dispose();
        }
    }
}
