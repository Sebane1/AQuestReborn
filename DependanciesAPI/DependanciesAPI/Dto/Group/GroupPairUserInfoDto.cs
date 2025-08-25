using McdfLoader.API.Data;
using McdfLoader.API.Data.Enum;
using MessagePack;

namespace McdfLoader.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserInfoDto(GroupData Group, UserData User, GroupPairUserInfo GroupUserInfo) : GroupPairDto(Group, User);