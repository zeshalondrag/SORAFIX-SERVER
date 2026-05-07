using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace sorafix_api.Services
{
    public class YooKassaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _shopId;
        private readonly string _secretKey;

        public YooKassaService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _shopId = config["YooKassa:ShopId"]!;
            _secretKey = config["YooKassa:SecretKey"]!;

            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_shopId}:{_secretKey}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }

        public async Task<(string, string)> CreatePaymentAsync(decimal amount, int requestId, string description)
        {
            var requestData = new
            {
                amount = new { value = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), currency = "RUB" },
                capture = true,
                confirmation = new { type = "redirect", return_url = $"https://sorafix.vercel.app/request/{requestId}" },
                description = description,
                metadata = new { request_id = requestId.ToString() }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Add("Idempotence-Key", Guid.NewGuid().ToString()); 

            var response = await _httpClient.PostAsync("https://api.yookassa.ru/v3/payments", content);
            response.EnsureSuccessStatusCode();

            var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = jsonDoc.RootElement;

            string paymentId = root.GetProperty("id").GetString()!;
            string confirmationUrl = root.GetProperty("confirmation").GetProperty("confirmation_url").GetString()!;

            return (paymentId, confirmationUrl);
        }
    }
}
