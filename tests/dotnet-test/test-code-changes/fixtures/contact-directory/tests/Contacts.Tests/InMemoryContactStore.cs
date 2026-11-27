namespace Contacts.Tests;

internal sealed class InMemoryContactStore(params Contact[] contacts) : IContactStore
{
    public Contact? FindByNormalizedEmail(string email) =>
        contacts.SingleOrDefault(contact => contact.Email.ToUpperInvariant() == email);
}
