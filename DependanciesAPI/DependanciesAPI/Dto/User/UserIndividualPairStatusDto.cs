using McdfLoader.API.Data;
using McdfLoader.API.Data.Enum;
using MessagePack;

namespace McdfLoader.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserIndividualPairStatusDto(UserData User, IndividualPairStatus IndividualPairStatus) : UserDto(User);