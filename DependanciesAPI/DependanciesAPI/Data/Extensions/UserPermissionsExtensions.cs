using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.API.Data.Extensions;

public static class UserPermissionsExtensions
{
    public static bool IsDisableAnimations(this UserPermissions perm)
    {
        return perm.HasFlag(UserPermissions.DisableAnimations);
    }

    public static bool IsDisableSounds(this UserPermissions perm)
    {
        return perm.HasFlag(UserPermissions.DisableSounds);
    }

    public static bool IsDisableVFX(this UserPermissions perm)
    {
        return perm.HasFlag(UserPermissions.DisableVFX);
    }

    public static bool IsPaused(this UserPermissions perm)
    {
        return perm.HasFlag(UserPermissions.Paused);
    }

    public static bool IsSticky(this UserPermissions perm)
    {
        return perm.HasFlag(UserPermissions.Sticky);
    }

    public static void SetDisableAnimations(this ref UserPermissions perm, bool set)
    {
        if (set) perm |= UserPermissions.DisableAnimations;
        else perm &= ~UserPermissions.DisableAnimations;
    }

    public static void SetDisableSounds(this ref UserPermissions perm, bool set)
    {
        if (set) perm |= UserPermissions.DisableSounds;
        else perm &= ~UserPermissions.DisableSounds;
    }

    public static void SetDisableVFX(this ref UserPermissions perm, bool set)
    {
        if (set) perm |= UserPermissions.DisableVFX;
        else perm &= ~UserPermissions.DisableVFX;
    }

    public static void SetPaused(this ref UserPermissions perm, bool paused)
    {
        if (paused) perm |= UserPermissions.Paused;
        else perm &= ~UserPermissions.Paused;
    }

    public static void SetSticky(this ref UserPermissions perm, bool sticky)
    {
        if (sticky) perm |= UserPermissions.Sticky;
        else perm &= ~UserPermissions.Sticky;
    }
}