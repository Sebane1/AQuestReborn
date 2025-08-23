namespace MareSynchronos.API.Data.Enum;

[Flags]
public enum GroupUserPreferredPermissions
{
    NoneSet = 0x0,
    Paused = 0x1,
    DisableAnimations = 0x2,
    DisableSounds = 0x4,
    DisableVFX = 0x8,
}