using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AssettoServer.Server.UserGroup;

public class FileBasedUserGroupProvider : IHostedService, IUserGroupProvider
{
    private readonly Dictionary<string, FileBasedUserGroup> _userGroups = new();

    private readonly ACServerConfiguration _configuration;

    public FileBasedUserGroupProvider(ACServerConfiguration configuration, FileBasedUserGroup.Factory fileBasedUserGroupFactory)
    {
        foreach ((string name, string path) in configuration.Extra.UserGroups)
        {
            _userGroups.Add(name, fileBasedUserGroupFactory(name, path));
        }

        _configuration = configuration;
    }

    public IUserGroup? Resolve(string name)
    {
        return _userGroups.TryGetValue(name, out var group) ? group : null;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_configuration.Extra.UserGroupAuthMethod.Equals("api"))
        {
            Log.Information("User group auth method is set to 'api', skipping file-based user group loading.");
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
}
