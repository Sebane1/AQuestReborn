namespace MareSynchronos.API.Dto.Files;

public interface ITransferFileDto
{
    string ForbiddenBy { get; set; }
    string Hash { get; set; }
    bool IsForbidden { get; set; }
}