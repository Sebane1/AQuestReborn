using McdfLoader.API.Data;
using McdfLoader.API.Data.Enum;
using MessagePack;

namespace McdfLoader.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserPermissionDto(GroupData Group, UserData User, GroupUserPreferredPermissions GroupPairPermissions) : GroupPairDto(Group, User);