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
    private readonly UserGroupManager _userGroupManager;
    private readonly ACServerConfiguration _configuration;

    public WhitelistService(ACServerConfiguration configuration, UserGroupManager userGroupManager)
    {
        _userGroup = userGroupManager.Resolve(configuration.Extra.WhitelistUserGroup);
        _configuration = configuration;
        _userGroupManager = userGroupManager;
    }

    public async Task<bool> IsWhitelistedAsync(ulong guid)
    {
        if (_configuration.Extra.UserGroupAuthMethod.Equals("api"))
        {
            await _userGroupManager.ReloadAllAsync();
        }

        return (_userGroup is IListableUserGroup listableUserGroup && listableUserGroup.List.Count == 0) || await _userGroup.ContainsAsync(guid);
    }

    public async Task AddAsync(ulong guid)
    {
        await _userGroup.AddAsync(guid);
    }
}
