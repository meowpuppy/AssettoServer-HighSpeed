using AssettoServer.Server.Configuration;
using AssettoServer.Server.UserGroup;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AssettoServer.Server.Blacklist;

public class BlacklistService : IBlacklistService
{
    private readonly IUserGroup _userGroup;

    private readonly ACServerConfiguration _configuration;

    public BlacklistService(ACServerConfiguration configuration, UserGroupManager userGroupManager)
    {
        _userGroup = userGroupManager.Resolve(configuration.Extra.BlacklistUserGroup);
        _userGroup.Changed += OnChanged;
        _configuration = configuration;
    }

    private void OnChanged(IUserGroup sender, EventArgs args)
    {
        Changed?.Invoke(this, args);
    }

    public async Task<bool> IsBlacklistedAsync(ulong guid)
    {
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
                    Log.Error(ex, "Failed to refresh API-based user group for blacklist check.");
                    return false;
                }
            }
        }

        return await _userGroup.ContainsAsync(guid);
    }

    public async Task AddAsync(ulong guid, string reason = "", ulong? admin = null)
    {
        await _userGroup.AddAsync(guid);
    }

    public event EventHandler<IBlacklistService, EventArgs>? Changed;
}
