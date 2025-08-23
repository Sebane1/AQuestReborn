using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.API.Data.Extensions;

public static class GroupUserInfoExtensions
{
    public static bool IsModerator(this GroupPairUserInfo info)
    {
        return info.HasFlag(GroupPairUserInfo.IsModerator);
    }

    public static bool IsPinned(this GroupPairUserInfo info)
    {
        return info.HasFlag(GroupPairUserInfo.IsPinned);
    }

    public static void SetModerator(this ref GroupPairUserInfo info, bool isModerator)
    {
        if (isModerator) info |= GroupPairUserInfo.IsModerator;
        else info &= ~GroupPairUserInfo.IsModerator;
    }

    public static void SetPinned(this ref GroupPairUserInfo info, bool isPinned)
    {
        if (isPinned) info |= GroupPairUserInfo.IsPinned;
        else info &= ~GroupPairUserInfo.IsPinned;
    }
}