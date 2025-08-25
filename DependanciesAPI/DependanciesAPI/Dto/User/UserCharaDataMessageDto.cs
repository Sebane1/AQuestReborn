using McdfLoader.API.Data;
using MessagePack;

namespace McdfLoader.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserCharaDataMessageDto(List<UserData> Recipients, CharacterData CharaData, CensusDataDto? CensusDataDto);
