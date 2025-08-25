using McdfLoader.API.Data;
using McdfLoader.API.Data.Enum;
using MessagePack;

namespace McdfLoader.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairFullInfoDto(GroupData Group, UserData User, UserPermissions SelfToOtherPermissions, UserPermissions OtherToSelfPermissions) : GroupPairDto(Group, User);