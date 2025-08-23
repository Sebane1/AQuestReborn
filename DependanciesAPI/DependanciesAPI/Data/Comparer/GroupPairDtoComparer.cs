using MareSynchronos.API.Dto.Group;

namespace MareSynchronos.API.Data.Comparer;

public class GroupPairDtoComparer : IEqualityComparer<GroupPairDto>
{
    private static GroupPairDtoComparer _instance = new();

    private GroupPairDtoComparer()
    { }

    public static GroupPairDtoComparer Instance => _instance;

    public bool Equals(GroupPairDto? x, GroupPairDto? y)
    {
        if (x == null || y == null) return false;
        return x.GID.Equals(y.GID, StringComparison.Ordinal) && x.UID.Equals(y.UID, StringComparison.Ordinal);
    }

    public int GetHashCode(GroupPairDto obj)
    {
        return HashCode.Combine(obj.Group.GID.GetHashCode(), obj.User.UID.GetHashCode());
    }
}