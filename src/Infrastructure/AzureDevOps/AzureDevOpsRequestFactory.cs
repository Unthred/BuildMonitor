using System.Net.Http.Headers;
using System.Text;

namespace BuildMonitor.Infrastructure.AzureDevOps;

internal static class AzureDevOpsRequestFactory
{
    public static HttpRequestMessage CreateGet(string url, string pat)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public static string ApiUrl(string organizationUrl, string relativePathAndQuery)
    {
        var baseUrl = organizationUrl.TrimEnd('/');
        var relative = relativePathAndQuery.StartsWith('/')
            ? relativePathAndQuery
            : "/" + relativePathAndQuery;
        return baseUrl + relative;
    }
}
