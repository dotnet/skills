var profile = new Profile { Name = "  Ada  " };

if (profile.Name != "Ada")
{
    throw new InvalidOperationException("The name was not normalized.");
}

Console.WriteLine("language-version-ok");
