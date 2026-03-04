using GUNRPG.Application.Identity;
using GUNRPG.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GUNRPG.Tests;

public class IdentityServiceExtensionsTests
{
    [Fact]
    public void AddGunRpgIdentity_RegistersWebAuthnServiceAsScoped()
    {
        var services = new ServiceCollection();

        services.AddGunRpgIdentity("https://localhost/auth/device/verify");

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IWebAuthnService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(services, d => d.ServiceType == typeof(IDeviceCodeService)).Lifetime);
    }
}
