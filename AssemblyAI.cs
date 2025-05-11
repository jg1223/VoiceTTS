using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

class Program
{
    static readonly string BaseUrl = "https://api.assemblyai.com";
    static readonly string ApiKey = "a654469ec26f4c49af4446d3d934611c";

    static async Task<string> UploadFileAsync(string filePath, HttpClient httpClient)
    {
        using (var fileStream = File.OpenRead(filePath))
        using (var fileContent = new StreamContent(fileStream))
        {
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using (var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/upload", fileContent))
            {
                response.EnsureSuccessStatusCode();
                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
                // Add null check to fix CS8602 warning
                return jsonDoc?.RootElement.GetProperty("upload_url").GetString() ??
                       throw new InvalidOperationException("Failed to get upload URL from response");
            }
        }
    }

    static async Task Main(string[] args)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("authorization", ApiKey);

        // var audioUrl = await UploadFileAsync("./my_audio.mp3", httpClient);
        string audioUrl = "https://assembly.ai/wildfires.mp3";

        var requestData = new
        {
            audio_url = audioUrl,
            speech_model = "universal"
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestData),
            Encoding.UTF8,
            "application/json");

        using var transcriptResponse = await httpClient.PostAsync($"{BaseUrl}/v2/transcript", jsonContent);
        var transcriptResponseBody = await transcriptResponse.Content.ReadAsStringAsync();
        var transcriptData = JsonSerializer.Deserialize<JsonElement>(transcriptResponseBody);

        if (!transcriptData.TryGetProperty("id", out JsonElement idElement))
        {
            throw new Exception("Failed to get transcript ID");
        }

        string transcriptId = idElement.GetString() ?? throw new Exception("Transcript ID is null");

        string pollingEndpoint = $"{BaseUrl}/v2/transcript/{transcriptId}";

        while (true)
        {
            using var pollingResponse = await httpClient.GetAsync(pollingEndpoint);
            var pollingResponseBody = await pollingResponse.Content.ReadAsStringAsync();
            var transcriptionResult = JsonSerializer.Deserialize<JsonElement>(pollingResponseBody);

            if (!transcriptionResult.TryGetProperty("status", out JsonElement statusElement))
            {
                throw new Exception("Failed to get transcription status");
            }

            string status = statusElement.GetString() ?? throw new Exception("Status is null");

            if (status == "completed")
            {
                if (!transcriptionResult.TryGetProperty("text", out JsonElement textElement))
                {
                    throw new Exception("Failed to get transcript text");
                }

                string transcriptText = textElement.GetString() ?? string.Empty;
                Console.WriteLine($"Transcript Text: {transcriptText}");
                break;
            }
            else if (status == "error")
            {
                string errorMessage = transcriptionResult.TryGetProperty("error", out JsonElement errorElement)
                    ? errorElement.GetString() ?? "Unknown error"
                    : "Unknown error";

                throw new Exception($"Transcription failed: {errorMessage}");
            }
            else
            {
                await Task.Delay(3000);
            }
        }
    }
}