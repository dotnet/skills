namespace PartialModern;

public class Repository
{
    private readonly string _name;

    public Repository(string name)
    {
        _name = name;
    }

    public string Describe()
    {
        return "Repository: " + _name;
    }
}
