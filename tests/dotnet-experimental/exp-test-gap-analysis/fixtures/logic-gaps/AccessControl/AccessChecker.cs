namespace AccessControl;

public enum Role { Guest, User, Editor, Admin }

public class Permission
{
    public string Resource { get; init; } = "";
    public bool CanRead { get; init; }
    public bool CanWrite { get; init; }
}

public class AccessChecker
{
    /// <summary>
    /// Checks if a user with the given role can access a resource.
    /// Admins can access everything. Editors can read and write non-system resources.
    /// Users can only read non-system resources. Guests have no access.
    /// </summary>
    public Permission GetPermission(Role role, string resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        bool isSystem = resource.StartsWith("sys/", StringComparison.OrdinalIgnoreCase);

        return role switch
        {
            Role.Admin => new Permission { Resource = resource, CanRead = true, CanWrite = true },
            Role.Editor when !isSystem => new Permission { Resource = resource, CanRead = true, CanWrite = true },
            Role.Editor => new Permission { Resource = resource, CanRead = true, CanWrite = false },
            Role.User when !isSystem => new Permission { Resource = resource, CanRead = true, CanWrite = false },
            _ => new Permission { Resource = resource, CanRead = false, CanWrite = false },
        };
    }

    /// <summary>
    /// Returns true if the user can perform the requested action.
    /// </summary>
    public bool CanPerform(Role role, string resource, bool writeAccess)
    {
        var perm = GetPermission(role, resource);
        return writeAccess ? perm.CanWrite : perm.CanRead;
    }

    /// <summary>
    /// Elevates a user's effective role if they have a temporary elevation token.
    /// Returns the elevated role, or the original role if the token is invalid.
    /// </summary>
    public Role ElevateRole(Role currentRole, string? token)
    {
        if (string.IsNullOrEmpty(token))
            return currentRole;

        if (token == "ELEVATE-ADMIN" && currentRole >= Role.Editor)
            return Role.Admin;

        if (token == "ELEVATE-EDITOR" && currentRole >= Role.User)
            return Role.Editor;

        return currentRole;
    }
}
