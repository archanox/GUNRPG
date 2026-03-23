using System.Security.Cryptography;
using System.Text;

namespace GUNRPG.Application.Combat;

internal static class StableGuidFactory
{
    public static Guid FromString(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }
}
