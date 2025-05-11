using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage; // FilePicker 사용을 위해 필요

namespace VoiceTTS
{
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();
        }

        private void OnCounterClicked(object sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);
        }

        // 폴더 내 모든 WAV 파일을 텍스트로 변환 후 저장
        private async void OnConvertFolderClicked(object sender, EventArgs e)
        {
            string folderPath = "/storage/emulated/0/Download"; // 실제 폴더 경로로 변경
            string subscriptionKey = "YourAzureSubscriptionKey";
            string region = "YourRegion";

            var audioFiles = Directory.GetFiles(folderPath, "*.*");
            foreach (var audioFile in audioFiles)
            {
                string text = await ConvertSpeechToText(audioFile, subscriptionKey, region);
                string txtFile = Path.ChangeExtension(audioFile, ".txt");
                File.WriteAllText(txtFile, text);
            }

            await DisplayAlert("완료", "모든 음성 파일이 텍스트로 저장되었습니다.", "확인");
        }

        // Replace the problematic code block with the following:

        // 음성 파일을 텍스트로 변환
        private async Task<string> ConvertSpeechToText(string audioFilePath, string subscriptionKey, string region)
        {
            var config = SpeechConfig.FromSubscription(subscriptionKey, region);

            using var audioInputStream = AudioInputStream.CreatePushStream();
            using (var fileStream = File.OpenRead(audioFilePath))
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    audioInputStream.Write(buffer, bytesRead);
                }
                audioInputStream.Close();
            }

            using var audioInput = AudioConfig.FromStreamInput(audioInputStream);
            using var recognizer = new SpeechRecognizer(config, audioInput);

            var result = await recognizer.RecognizeOnceAsync();
            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return result.Text;
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                return $"인식 실패: {result.Reason}, 이유: {cancellation.Reason}, 세부정보: {cancellation.ErrorDetails}";
            }
            else
            {
                return $"인식 실패: {result.Reason}";
            }
        }

        private async void OnPickAndConvertFilesClicked(object sender, EventArgs e)
        {
            string subscriptionKey = "YourAzureSubscriptionKey"; // 실제 키로 변경
            string region = "YourRegion"; // 실제 지역으로 변경

            var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
           {
               { DevicePlatform.Android, new[] { "audio/wav", "audio/mpeg", "audio/mp3", "audio/m4a" } },
               { DevicePlatform.iOS, new[] { "public.audio" } },
               { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".m4a" } }
           });

            var result = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "음성 파일을 선택하세요",
                FileTypes = customFileType
            });

            if (result == null)
                return;

            foreach (var file in result)
            {
                using var stream = await file.OpenReadAsync();
                string tempPath = Path.Combine(FileSystem.CacheDirectory, file.FileName);
                using (var fs = File.OpenWrite(tempPath))
                {
                    await stream.CopyToAsync(fs);
                }

                string text = await ConvertSpeechToText(tempPath, subscriptionKey, region);
                string txtFile = Path.ChangeExtension(tempPath, ".txt");
                File.WriteAllText(txtFile, text);
            }

            await DisplayAlert("완료", "선택한 음성 파일이 텍스트로 저장되었습니다.", "확인");
        }
        private string ConvertM4AToWav(string m4aFilePath)
        {
            string wavFilePath = Path.ChangeExtension(m4aFilePath, ".wav");
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{m4aFilePath}\" \"{wavFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg 변환 실패: {process.StandardError.ReadToEnd()}");
            }

            return wavFilePath;
        }
    }

}
