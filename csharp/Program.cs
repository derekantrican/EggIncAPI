using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ei;
using Google.Protobuf;

await GetCoopStatus("shipping-surge", "shippingrun");

async Task GetCoopStatus(string contractId, string coopId)
{
    ContractCoopStatusRequest coopStatusRequest = new ContractCoopStatusRequest();
    coopStatusRequest.ContractIdentifier = contractId;
    coopStatusRequest.CoopIdentifier = coopId;

    byte[] bytes;
    using (var stream = new MemoryStream())
    {
        coopStatusRequest.WriteTo(stream);
        bytes = stream.ToArray();
    }

    string url = "https://wasmegg.zw.workers.dev/?url=https://www.auxbrain.com/ei/coop_status";
    string response = await PostRequest(url, new FormUrlEncodedContent(new Dictionary<string, string>
    {
        { "data", Convert.ToBase64String(bytes) }
    }));

    AuthenticatedMessage authenticatedMessage = AuthenticatedMessage.Parser.ParseFrom(Convert.FromBase64String(response));

    ContractCoopStatusResponse contractCoopStatusResponse = ContractCoopStatusResponse.Parser.ParseFrom(authenticatedMessage.Message);

    Console.WriteLine(JsonSerializer.Serialize(contractCoopStatusResponse));
}

async Task<string> PostRequest(string url, FormUrlEncodedContent json)
{
    using (var client = new HttpClient())
    {
        var response = await client.PostAsync(url, json);
        return await response.Content.ReadAsStringAsync();
    }
}