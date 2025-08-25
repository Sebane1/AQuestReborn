using McdfLoader.API.Data;
using MessagePack;

namespace McdfLoader.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public record ConnectionDto(UserData User)
{
    public Version CurrentClientVersion { get; set; } = new(0, 0, 0);
    public int ServerVersion { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsModerator { get; set; }
    public ServerInfo ServerInfo { get; set; } = new();
    public DefaultPermissionsDto DefaultPreferredPermissions { get; set; } = new();
}

[MessagePackObject(keyAsPropertyName: true)]
public record ServerInfo
{
    public string ShardName { get; set; } = string.Empty;
    public int MaxGroupUserCount { get; set; }
    public int MaxGroupsCreatedByUser { get; set; }
    public int MaxGroupsJoinedByUser { get; set; }
    public Uri FileServerAddress { get; set; } = new Uri("http://nonemptyuri");
}
