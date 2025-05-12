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
using Microsoft.Maui.ApplicationModel;


namespace VoiceTTS
{
    public partial class MainPage : ContentPage
    {
        private const string AssemblyAiApiKey = "a654469ec26f4c49af4446d3d934611c"; // AssemblyAI API 키

        public MainPage()
        {
            InitializeComponent();

            // 앱 실행 시 VoiceTTS 폴더 생성
#if ANDROID
            CreateVoiceTTSFolder();
#endif

            // 앱 실행 시 블로그 자동 열기
            OpenBlogOnStartup();
        }

        // MainPage.xaml.csLauncher
        private async void OnPickAndConvertFilesClicked(object sender, EventArgs e)
        {
            // 권한 요청
            await RequestFilePermissionsAsync();

            try
            {
                // 파일 선택 대화상자 표시
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    // 로딩바 표시
                    LoadingIndicator.IsVisible = true;
                    LoadingIndicator.IsRunning = true;

                    // 선택한 파일을 텍스트로 변환
                    string transcript = await ConvertAudioToText(result.FullPath);

#if ANDROID
                    // VoiceTTS 폴더 경로 설정
                    string voiceTTSFolderPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath, "VoiceTTS");

                    // 텍스트 파일 저장
                    string audioFileName = Path.GetFileNameWithoutExtension(result.FileName); // 확장자 제거
                    string textFileName = $"{audioFileName}.txt"; // 텍스트 파일 이름 생성
                    string filePath = Path.Combine(voiceTTSFolderPath, textFileName); // VoiceTTS 폴더에 저장 경로 설정
                    File.WriteAllText(filePath, transcript); // 텍스트 파일 저장

                    // 변환 결과를 알림으로 표시
                    await DisplayAlert("변환 완료", $"텍스트 파일이 생성되었습니다: {filePath}", "확인");
#elif WINDOWS
                    // Windows: 기존 방식대로 AppDataDirectory에 저장
                    string audioFileName = Path.GetFileNameWithoutExtension(result.FileName); // 확장자 제거
                    string textFileName = $"{audioFileName}.txt"; // 텍스트 파일 이름 생성
                    string filePath = Path.Combine(FileSystem.AppDataDirectory, textFileName); // 저장 경로 설정
                    File.WriteAllText(filePath, transcript); // 텍스트 파일 저장

                    // 변환 결과를 알림으로 표시
                    await DisplayAlert("변환 완료", $"텍스트 파일이 생성되었습니다: {filePath}", "확인");
#else
                    await DisplayAlert("오류", "현재 플랫폼에서 파일 저장이 지원되지 않습니다.", "확인");
#endif
                }
            }
            catch (Exception ex)
            {
                // 오류 발생 시 알림 표시
                await DisplayAlert("오류", ex.Message, "확인");
            }
            finally
            {
                // 로딩바 숨김
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        // 텍스트에 줄바꿈 추가 메서드
        private string AddLineBreaks(string text, int maxLineLength)
        {
            var words = text.Split(' '); // 단어 단위로 분리
            var formattedText = new StringBuilder();
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 > maxLineLength) // 줄 길이 초과 시 줄바꿈
                {
                    formattedText.AppendLine(currentLine.ToString().Trim());
                    currentLine.Clear();
                }
                currentLine.Append(word + " ");
            }

            if (currentLine.Length > 0) // 마지막 줄 추가
            {
                formattedText.AppendLine(currentLine.ToString().Trim());
            }

            return formattedText.ToString();
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

            // 2. 음성 텍스트 변환 요청 (스피커 다이어리제이션 활성화)
            var transcriptRequest = new
            {
                audio_url = audioUrl,
                speech_model = "universal",
                language_code = "ko", // 한국어 설정
                speaker_labels = true // 스피커 다이어리제이션 활성화
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
                    // 스피커 다이어리제이션 결과 처리
                    var segments = statusJson.RootElement.GetProperty("utterances");
                    var formattedTranscript = new StringBuilder();

                    foreach (var segment in segments.EnumerateArray())
                    {
                        var speaker = segment.GetProperty("speaker").GetString();
                        var text = segment.GetProperty("text").GetString();
                        formattedTranscript.AppendLine($"사용자 {speaker}: {text}");
                        formattedTranscript.AppendLine(); // 사용자 단위로 줄바꿈 추가
                    }

                    return formattedTranscript.ToString();
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
        //private async void OnOpenBlogClicked(object sender, EventArgs e)
        //{
        //    var url = "https://jg1223.tistory.com/";
        //    await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        //}

        private async void OpenBlogOnStartup()
        {
            var url = "https://jg1223.tistory.com/";
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }

        private async Task RequestFilePermissionsAsync()
        {
            try
            {
                // 읽기 권한 확인 및 요청
                var readStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                if (readStatus != PermissionStatus.Granted)
                {
                    readStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
                    if (readStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("권한 필요", "파일 읽기 권한이 필요합니다.", "확인");
                        return;
                    }
                }

                // 쓰기 권한 확인 및 요청
                var writeStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                if (writeStatus != PermissionStatus.Granted)
                {
                    writeStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
                    if (writeStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("권한 필요", "파일 쓰기 권한이 필요합니다.", "확인");
                        return;
                    }
                }

                // 권한이 이미 승인된 경우 알림창 표시 생략
            }
            catch (Exception ex)
            {
                await DisplayAlert("오류", $"권한 요청 중 오류가 발생했습니다: {ex.Message}", "확인");
            }
        }
        // 'OpenFolderRequest'는 현재 프로젝트에서 정의되지 않은 클래스입니다.  
        // 이 문제를 해결하려면 'OpenFolderRequest'를 정의하거나, 적절한 대체 코드를 사용해야 합니다.  
        // 아래는 Windows 플랫폼에서 폴더를 여는 대체 코드입니다.  

        private async void OnOpenSavedPathClicked(object sender, EventArgs e)
        {
            // 권한 요청
            await RequestFolderPermissionsAsync();

            try
            {
                // 내부 저장소 경로 가져오기
                string directoryPath = FileSystem.AppDataDirectory;

#if ANDROID
                try
                {
                    // 내부 저장소 열기 시도
                    var uri = Android.Net.Uri.Parse(directoryPath);
                    var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                    intent.SetDataAndType(uri, "*/*");
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask);

                    Android.App.Application.Context.StartActivity(intent);
                }
                catch (Exception)
                {
                    // 내부 저장소 열기 실패 시 외부 저장소로 전환
                    directoryPath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath;

                    var uri = Android.Net.Uri.Parse(directoryPath);
                    var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                    intent.SetDataAndType(uri, "*/*");
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask);

                    Android.App.Application.Context.StartActivity(intent);
                }
#elif WINDOWS
                // Windows 플랫폼에서 폴더 열기
                var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = directoryPath,
                    UseShellExecute = true
                };
                process.Start();
#else
                await DisplayAlert("오류", "현재 플랫폼에서 디렉터리 열기가 지원되지 않습니다.", "확인");
#endif
            }
            catch (Exception ex)
            {
                await DisplayAlert("오류", $"폴더를 여는 중 오류가 발생했습니다: {ex.Message}", "확인");
            }
        }
        private async Task RequestFolderPermissionsAsync()
        {
            try
            {
                // 읽기 권한 확인 및 요청
                var readStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                if (readStatus != PermissionStatus.Granted)
                {
                    readStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
                    if (readStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("권한 필요", "폴더를 열기 위해 파일 읽기 권한이 필요합니다.", "확인");
                        return;
                    }
                }

                // 쓰기 권한 확인 및 요청
                var writeStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                if (writeStatus != PermissionStatus.Granted)
                {
                    writeStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
                    if (writeStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("권한 필요", "폴더를 열기 위해 파일 쓰기 권한이 필요합니다.", "확인");
                        return;
                    }
                }

                // 권한이 이미 승인된 경우 알림창 표시 생략
            }
            catch (Exception ex)
            {
                await DisplayAlert("오류", $"권한 요청 중 오류가 발생했습니다: {ex.Message}", "확인");
            }
        }
        private void CreateVoiceTTSFolder()
        {
            try
            {
                // VoiceTTS 폴더 경로 설정
                string voiceTTSFolderPath = Path.Combine(Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDocuments).AbsolutePath, "VoiceTTS");

                // 폴더가 이미 존재하면 로그 출력 후 종료
                if (Directory.Exists(voiceTTSFolderPath))
                {
                    //System.Diagnostics.Debug.WriteLine("VoiceTTS 폴더가 이미 존재합니다.");
                    return;
                }

                // 폴더 생성
                Directory.CreateDirectory(voiceTTSFolderPath);
                System.Diagnostics.Debug.WriteLine("VoiceTTS 폴더가 성공적으로 생성되었습니다.");
            }
            catch (Exception ex)
            {
                // 폴더 생성 실패 시 로그 출력
                System.Diagnostics.Debug.WriteLine($"VoiceTTS 폴더 생성 중 오류 발생: {ex.Message}");
            }
        }

    }

}
