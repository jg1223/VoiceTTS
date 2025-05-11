using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage; // FilePicker 사용을 위해 필요
using Google.Cloud.Speech.V1;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace VoiceTTS
{
    public partial class MainPage : ContentPage
    {
        private const string AssemblyAiApiKey = "a654469ec26f4c49af4446d3d934611c"; // AssemblyAI API 키

        public MainPage()
        {
            InitializeComponent();
        }

        // MainPage.xaml.cs
        private async void OnPickAndConvertFilesClicked(object sender, EventArgs e)
        {
            try
            {
                // 파일 선택 대화상자 표시
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    // 선택한 파일을 텍스트로 변환
                    string transcript = await ConvertAudioToText(result.FullPath);

                    // 음성 파일과 동일한 이름으로 텍스트 파일 생성
                    string audioFileName = Path.GetFileNameWithoutExtension(result.FileName); // 확장자 제거
                    string textFileName = $"{audioFileName}.txt"; // 텍스트 파일 이름 생성
                    string filePath = Path.Combine(FileSystem.AppDataDirectory, textFileName); // 저장 경로 설정
                    File.WriteAllText(filePath, transcript); // 텍스트 파일 저장

                    // 변환 결과를 알림으로 표시
                    await DisplayAlert("변환 완료", $"텍스트 파일이 생성되었습니다: {filePath}", "확인");
                }
            }
            catch (Exception ex)
            {
                // 오류 발생 시 알림 표시
                await DisplayAlert("오류", ex.Message, "확인");
            }
        }

        // AssemblyAI를 사용하여 음성 파일을 텍스트로 변환
        private async Task<string> ConvertAudioToText(string audioFilePath)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("authorization", AssemblyAiApiKey);

            // 1. 파일 업로드
            var uploadUrl = "https://api.assemblyai.com/v2/upload";
            using var fileStream = File.OpenRead(audioFilePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var uploadResponse = await httpClient.PostAsync(uploadUrl, fileContent);
            uploadResponse.EnsureSuccessStatusCode();
            var uploadResult = await uploadResponse.Content.ReadAsStringAsync();
            var uploadJson = JsonDocument.Parse(uploadResult);
            var audioUrl = uploadJson.RootElement.GetProperty("upload_url").GetString();

            // 2. 음성 텍스트 변환 요청
            var transcriptRequest = new
            {
                audio_url = audioUrl,
                speech_model = "universal",
                language_code = "ko" // 한국어 설정
            };

            var transcriptUrl = "https://api.assemblyai.com/v2/transcript";
            var transcriptContent = new StringContent(JsonSerializer.Serialize(transcriptRequest), Encoding.UTF8, "application/json");
            var transcriptResponse = await httpClient.PostAsync(transcriptUrl, transcriptContent);
            transcriptResponse.EnsureSuccessStatusCode();
            var transcriptResult = await transcriptResponse.Content.ReadAsStringAsync();
            var transcriptJson = JsonDocument.Parse(transcriptResult);
            var transcriptId = transcriptJson.RootElement.GetProperty("id").GetString();

            // 3. 변환 결과 확인
            string transcriptStatusUrl = $"{transcriptUrl}/{transcriptId}";
            while (true)
            {
                var statusResponse = await httpClient.GetAsync(transcriptStatusUrl);
                statusResponse.EnsureSuccessStatusCode();
                var statusResult = await statusResponse.Content.ReadAsStringAsync();
                var statusJson = JsonDocument.Parse(statusResult);
                var status = statusJson.RootElement.GetProperty("status").GetString();

                if (status == "completed")
                {
                    // 변환 완료 시 텍스트 반환
                    return statusJson.RootElement.GetProperty("text").GetString();
                }
                else if (status == "error")
                {
                    // 변환 실패 시 예외 발생
                    throw new Exception("AssemblyAI 변환 실패");
                }

                // 3초 대기 후 상태 재확인
                await Task.Delay(3000);
            }
        }
    }

}
