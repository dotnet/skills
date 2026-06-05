#nullable disable

namespace FileByFile;

public class UserProfile
{
    public string Name { get; set; }
    public string Bio { get; set; }
    public string Email { get; set; }

    public UserProfile(string name, string email)
    {
        Name = name;
        Email = email;
    }

    public string GetDisplayName()
    {
        return Bio != null ? $"{Name} - {Bio}" : Name;
    }
}
