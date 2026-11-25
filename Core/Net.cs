using System.Net.Http;

namespace Perch.Core;

/// <summary>One shared web client for the online widgets. No cookies, no accounts.</summary>
public static class Net
{
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var h = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Perch/" + (typeof(Net).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return h;
    }

    /// <summary>A short, friendly reason for a failed request.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "The service didn't answer in time.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "Not found. Check the name.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests } => "The service is limiting requests. Try again later.",
        HttpRequestException => "Can't reach the service.",
        _ => "Something went wrong reading the data.",
    };
}
