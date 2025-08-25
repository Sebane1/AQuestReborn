using McdfLoader.API.Data;
using MessagePack;

namespace McdfLoader.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserDto(UserData User);