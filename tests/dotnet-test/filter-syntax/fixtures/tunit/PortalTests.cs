using System.Threading.Tasks;
using TUnit.Core;

namespace Contoso.Portal.Tests.Api;

public class LoginTests
{
    [Test]
    [Category("Smoke")]
    public async Task AcceptCookiesTest() { await Task.CompletedTask; }

    [Test]
    [Category("Slow")]
    public async Task LoginWithExpiredPasswordTest() { await Task.CompletedTask; }
}

public class SignupTests
{
    [Test]
    [Category("Smoke")]
    public async Task SignupWithValidEmailTest() { await Task.CompletedTask; }

    [Test]
    [Category("Slow")]
    public async Task SignupRejectsDuplicateEmailTest() { await Task.CompletedTask; }
}
