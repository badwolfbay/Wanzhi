using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Wanzhi.Models;

namespace Wanzhi.Services
{
    /// <summary>
    /// 今日诗词 API 服务
    /// </summary>
    public class JinrishiciService
    {
        private const string TokenUrl = "https://v2.jinrishici.com/token";
        private const string SentenceUrl = "https://v2.jinrishici.com/sentence";
        private readonly string _tokenFilePath;
        private readonly HttpClient _httpClient;
        private string? _cachedToken;

        public JinrishiciService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // Set strict timeout
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WanzhiDesktop/1.0");
            
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Wanzhi"
            );
            Directory.CreateDirectory(appDataPath);
            _tokenFilePath = Path.Combine(appDataPath, "token.txt");
        }

        /// <summary>
        /// 获取或创建 Token
        /// </summary>
        private async Task<string> GetOrCreateTokenAsync()
        {
            // 如果内存中有缓存，直接返回
            if (!string.IsNullOrEmpty(_cachedToken))
            {
                App.Log("Using cached token (memory).");
                return _cachedToken;
            }

            // 尝试从文件读取
            if (File.Exists(_tokenFilePath))
            {
                try 
                {
                    var token = await File.ReadAllTextAsync(_tokenFilePath);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        _cachedToken = token.Trim();
                        App.Log("Using cached token (file).");
                        return _cachedToken;
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"Error reading token file: {ex.Message}");
                }
            }

            // 从 API 获取新 Token
            try
            {
                App.Log("Requesting new token from API...");
                var response = await _httpClient.GetStringAsync(TokenUrl);
                App.Log($"Token response received. Length: {response.Length}");
                
                var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(response);

                if (tokenResponse?.Status == "success" && !string.IsNullOrEmpty(tokenResponse.Data))
                {
                    _cachedToken = tokenResponse.Data;
                    await File.WriteAllTextAsync(_tokenFilePath, _cachedToken);
                    App.Log("New token acquired and saved.");
                    return _cachedToken;
                }
                else
                {
                    App.Log($"Token API returned status: {tokenResponse?.Status}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取 Token 失败: {ex.Message}");
                App.Log($"Get Token Exception: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取推荐诗词
        /// </summary>
        public async Task<PoetryData?> GetPoetryAsync()
        {
            try
            {
                var token = await GetOrCreateTokenAsync();
                App.Log($"GetPoetryAsync: Token acquired? {(!string.IsNullOrEmpty(token))}");
                
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                // 创建带 Token 的请求
                var request = new HttpRequestMessage(HttpMethod.Get, SentenceUrl);
                request.Headers.Add("X-User-Token", token);

                App.Log("Sending poetry request...");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                App.Log($"Poetry response received. Length: {content.Length}");
                
                var poetryResponse = JsonConvert.DeserializeObject<PoetryResponse>(content);

                if (poetryResponse?.Status == "success" && poetryResponse.Data != null)
                {
                    return poetryResponse.Data;
                }
                else if (poetryResponse?.ErrorCode != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"API 错误: {poetryResponse.ErrorCode} - {poetryResponse.ErrorMessage}"
                    );
                    App.Log($"API Error: {poetryResponse.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取诗词失败: {ex.Message}");
                App.Log($"GetPoetryAsync Exception: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 清除缓存的 Token（用于测试或重置）
        /// </summary>
        public void ClearToken()
        {
            _cachedToken = null;
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
            }
        }
    }
}
