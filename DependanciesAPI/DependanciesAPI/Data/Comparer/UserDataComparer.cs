namespace MareSynchronos.API.Data.Comparer;

public class UserDataComparer : IEqualityComparer<UserData>
{
    private static UserDataComparer _instance = new();

    private UserDataComparer()
    { }

    public static UserDataComparer Instance => _instance;

    public bool Equals(UserData? x, UserData? y)
    {
        if (x == null || y == null) return false;
        return x.UID.Equals(y.UID, StringComparison.Ordinal);
    }

    public int GetHashCode(UserData obj)
    {
        return obj.UID.GetHashCode();
    }
}