using MessagePack;

namespace MareSynchronos.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public record SystemInfoDto
{
    public int OnlineUsers { get; set; }
}