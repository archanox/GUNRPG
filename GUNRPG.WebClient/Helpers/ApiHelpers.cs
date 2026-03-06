using System.Net.Http.Json;

namespace GUNRPG.WebClient.Helpers;

internal static class ApiHelpers
{
    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }

    public static async Task<string?> TryReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
            return body?.Error;
        }
        catch
        {
            return null;
        }
    }
}
