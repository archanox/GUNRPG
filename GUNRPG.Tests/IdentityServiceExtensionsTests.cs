using Fido2NetLib;
using GUNRPG.Application.Identity;
using GUNRPG.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        var fido2ValidationDescriptor = Assert.Single(services, d =>
            d.ServiceType == typeof(IValidateOptions<Fido2Configuration>));

        Assert.Equal(ServiceLifetime.Scoped, webAuthnDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, deviceCodeDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, fido2ValidationDescriptor.Lifetime);
    }
}
