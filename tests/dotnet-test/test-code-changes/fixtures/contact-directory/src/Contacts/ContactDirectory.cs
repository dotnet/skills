namespace Contacts;

public sealed record Contact(string Name, string Email);

public interface IContactStore
{
    Contact? FindByNormalizedEmail(string email);
}

public sealed class ContactDirectory(IContactStore store)
{
    public Contact? FindByEmail(string? email) =>
        store.FindByNormalizedEmail(email!.Trim().ToUpperInvariant());
}
