using GUNRPG.Application.Identity;
using GUNRPG.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GUNRPG.Tests;

public class IdentityServiceExtensionsTests
{
    [Fact]
    public void AddGunRpgIdentity_RegistersIdentityServicesAsScoped()
    {
        var services = new ServiceCollection();

        services.AddGunRpgIdentity("https://localhost/auth/device/verify");

        var webAuthnDescriptor = Assert.Single(services, d => d.ServiceType == typeof(IWebAuthnService));
        var deviceCodeDescriptor = Assert.Single(services, d => d.ServiceType == typeof(IDeviceCodeService));

        Assert.Equal(ServiceLifetime.Scoped, webAuthnDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, deviceCodeDescriptor.Lifetime);
    }
}
