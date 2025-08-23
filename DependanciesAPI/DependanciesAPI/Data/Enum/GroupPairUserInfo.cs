namespace MareSynchronos.API.Data.Enum;

[Flags]
public enum GroupPairUserInfo
{
    None = 0x0,
    IsModerator = 0x2,
    IsPinned = 0x4
}