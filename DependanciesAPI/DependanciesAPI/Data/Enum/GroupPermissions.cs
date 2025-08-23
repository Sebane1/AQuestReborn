namespace MareSynchronos.API.Data.Enum;

[Flags]
public enum GroupPermissions
{
    NoneSet = 0x0,
    PreferDisableAnimations = 0x1,
    PreferDisableSounds = 0x2,
    DisableInvites = 0x4,
    PreferDisableVFX = 0x8,
}