using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using AssettoServer.Server.Configuration;

namespace AssettoServer.Server.UserGroup;

public class UserGroupManager
{
    private readonly IList<IUserGroupProvider> _providers;
    private readonly ACExtraConfiguration _extraConfig;

    public UserGroupManager(IList<IUserGroupProvider> providers, ACServerConfiguration configuration)
    {
        _providers = providers;
        _extraConfig = configuration.Extra;
    }

    public bool TryResolve(string name, [NotNullWhen(true)] out IUserGroup? group)
    {
        var method = _extraConfig.UserGroupAuthMethod.ToLowerInvariant();
        foreach (var provider in _providers)
        {
            // Only use the provider matching the configured method
            if ((method == "file" && provider is FileBasedUserGroupProvider) ||
                (method == "api" && provider is ApiBasedUserGroupProvider))
            {
                group = provider.Resolve(name);
                if (group != null)
                    return true;
            }
        }

        group = null;
        return false;
    }

    public IUserGroup Resolve(string name)
    {
        return TryResolve(name, out var group) ? group : throw new ConfigurationException($"No user group found with name {name}");
    }

    public async Task ReloadAllAsync()
    {
        var method = _extraConfig.UserGroupAuthMethod.ToLowerInvariant();
        foreach (var provider in _providers)
        {
            if ((method == "file" && provider is FileBasedUserGroupProvider fileProvider))
            {
                await fileProvider.ReloadAllAsync();
            }
            else if ((method == "api" && provider is ApiBasedUserGroupProvider apiProvider))
            {
                await apiProvider.ReloadAllAsync();
            }
        }
    }
}
