using MessagePack;

namespace MareSynchronos.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public record DefaultPermissionsDto
{
    public bool DisableIndividualAnimations { get; set; } = false;
    public bool DisableIndividualSounds { get; set; } = false;
    public bool DisableIndividualVFX { get; set; } = false;
    public bool DisableGroupAnimations { get; set; } = false;
    public bool DisableGroupSounds { get; set; } = false;
    public bool DisableGroupVFX { get; set; } = false;
    public bool IndividualIsSticky { get; set; } = true;
}