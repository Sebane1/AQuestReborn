namespace MareSynchronos.API.Data.Comparer;

public class GroupDataComparer : IEqualityComparer<GroupData>
{
    private static GroupDataComparer _instance = new GroupDataComparer();

    private GroupDataComparer()
    { }

    public static GroupDataComparer Instance => _instance;

    public bool Equals(GroupData? x, GroupData? y)
    {
        if (x == null || y == null) return false;
        return x.GID.Equals(y.GID, StringComparison.Ordinal);
    }

    public int GetHashCode(GroupData obj)
    {
        return obj.GID.GetHashCode();
    }
}