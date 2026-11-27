using Xunit;

namespace Contacts.Tests;

public sealed class FindByEmailTests
{
    [Fact]
    public void Finds_existing_address_without_matching_case()
    {
        var contact = new Contact("Jun", "jun@example.test");
        var directory = new ContactDirectory(new InMemoryContactStore(contact));

        Assert.Equal(contact, directory.FindByEmail(" JUN@example.test "));
    }
}
