using System.IO;
using System.Net.Http;
using System.Text;

namespace MajdataEdit_Neo.Utils;

internal static class WebControl
{
    private static readonly HttpClient _httpClient = new();

    public static string RequestPOST(string url, string data = "")
    {
        try
        {
            var content = new StringContent(data, Encoding.UTF8);
            var response = _httpClient.PostAsync(url, content).Result;
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return reader.ReadToEnd();
        }
        catch
        {
            return "ERROR";
        }
    }

    public static string RequestGET(string url)
    {
        try
        {
            var response = _httpClient.GetAsync(url).Result;
            using var reader = new StreamReader(response.Content.ReadAsStream());
            return reader.ReadToEnd();
        }
        catch
        {
            return "ERROR";
        }
    }
}