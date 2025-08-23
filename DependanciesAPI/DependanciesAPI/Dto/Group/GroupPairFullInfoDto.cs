using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MessagePack;

namespace MareSynchronos.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairFullInfoDto(GroupData Group, UserData User, UserPermissions SelfToOtherPermissions, UserPermissions OtherToSelfPermissions) : GroupPairDto(Group, User);