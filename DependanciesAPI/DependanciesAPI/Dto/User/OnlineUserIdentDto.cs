using McdfLoader.API.Data;
using MessagePack;

namespace McdfLoader.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record OnlineUserIdentDto(UserData User, string Ident) : UserDto(User);