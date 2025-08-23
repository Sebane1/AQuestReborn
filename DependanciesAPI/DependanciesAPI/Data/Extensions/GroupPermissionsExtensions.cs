using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.API.Data.Extensions;

public static class GroupPermissionsExtensions
{
    public static bool IsDisableInvites(this GroupPermissions perm)
    {
        return perm.HasFlag(GroupPermissions.DisableInvites);
    }

    public static bool IsPreferDisableAnimations(this GroupPermissions perm)
    {
        return perm.HasFlag(GroupPermissions.PreferDisableAnimations);
    }

    public static bool IsPreferDisableSounds(this GroupPermissions perm)
    {
        return perm.HasFlag(GroupPermissions.PreferDisableSounds);
    }

    public static bool IsPreferDisableVFX(this GroupPermissions perm)
    {
        return perm.HasFlag(GroupPermissions.PreferDisableVFX);
    }

    public static void SetDisableInvites(this ref GroupPermissions perm, bool set)
    {
        if (set) perm |= GroupPermissions.DisableInvites;
        else perm &= ~GroupPermissions.DisableInvites;
    }

    public static void SetPreferDisableAnimations(this ref GroupPermissions perm, bool set)
    {
        if (set) perm |= GroupPermissions.PreferDisableAnimations;
        else perm &= ~GroupPermissions.PreferDisableAnimations;
    }

    public static void SetPreferDisableSounds(this ref GroupPermissions perm, bool set)
    {
        if (set) perm |= GroupPermissions.PreferDisableSounds;
        else perm &= ~GroupPermissions.PreferDisableSounds;
    }

    public static void SetPreferDisableVFX(this ref GroupPermissions perm, bool set)
    {
        if (set) perm |= GroupPermissions.PreferDisableVFX;
        else perm &= ~GroupPermissions.PreferDisableVFX;
    }
}