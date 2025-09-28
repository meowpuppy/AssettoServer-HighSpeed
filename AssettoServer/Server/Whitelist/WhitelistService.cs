using AssettoServer.Server.Configuration;
using AssettoServer.Server.UserGroup;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;

namespace AssettoServer.Server.Whitelist;

public class WhitelistService : IWhitelistService
{
    private readonly IUserGroup _userGroup;

    private readonly ACServerConfiguration _configuration;

    public WhitelistService(ACServerConfiguration configuration, UserGroupManager userGroupManager)
    {
        _userGroup = userGroupManager.Resolve(configuration.Extra.WhitelistUserGroup);
        _configuration = configuration;
    }

    public async Task<bool> IsWhitelistedAsync(ulong guid)
    {
        //if (_userGroup is IListableUserGroup listableUserGroup)
        //{
        //    var listString = string.Join('\n', listableUserGroup.List);
        //    Log.Information("Whitelist user group list:\n{List}", listString);
        //}

        if (_configuration.Extra.UserGroupAuthMethod.Equals("api"))
        {
            // rerun ApiBasedUserGroup load to ensure we have the latest data
            if (_userGroup is ApiBasedUserGroup apiBasedUserGroup)
            {
                try
                {
                    await apiBasedUserGroup.LoadAsync();
                }
                catch (HttpRequestException ex)
                {
                    Log.Error(ex, "Failed to refresh API-based user group for whitelist check.");
                    return false;
                }
            }
        }

        return (_userGroup is IListableUserGroup listableUserGroup && listableUserGroup.List.Count == 0) || await _userGroup.ContainsAsync(guid);
    }

    public async Task AddAsync(ulong guid)
    {
        await _userGroup.AddAsync(guid);
    }
}
