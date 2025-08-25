using McdfLoader.API.Data;
using McdfLoader.API.Data.Enum;
using MessagePack;

namespace McdfLoader.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPermissionDto(GroupData Group, GroupPermissions Permissions) : GroupDto(Group);