using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssettoServer.Server.UserGroup
{
    public class ApiBasedUserGroupProvider : IHostedService, IUserGroupProvider
    {
        private readonly Dictionary<string, ApiBasedUserGroup> _userGroups = new();

        private readonly ACServerConfiguration _configuration;

        public ApiBasedUserGroupProvider(ACServerConfiguration configuration, ApiBasedUserGroup.Factory apiBasedUserGroupFactory)
        {
            foreach ((string name, string url) in configuration.Extra.UserGroupsApi)
            {
                _userGroups.Add(name, apiBasedUserGroupFactory(name, url));
            }

            _configuration = configuration;
        }

        public IUserGroup? Resolve(string name)
        {
            return _userGroups.TryGetValue(name, out var group) ? group : null;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_configuration.Extra.UserGroupAuthMethod.Equals("file"))
            {
                Log.Information("User group auth method is set to 'file', skipping api-based user group loading.");
                return;
            }

            foreach (var group in _userGroups.Values)
            {
                await group.LoadAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var group in _userGroups.Values)
            {
                group.Dispose();
            }

            return Task.CompletedTask;
        }

        public async Task ReloadAllAsync()
        {
            foreach (var group in _userGroups.Values)
            {
                await group.LoadAsync();
            }
        }
    }
}
