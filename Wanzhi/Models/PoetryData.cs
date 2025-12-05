using Newtonsoft.Json;

namespace Wanzhi.Models
{
    /// <summary>
    /// 今日诗词 API 响应根对象
    /// </summary>
    public class PoetryResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("data")]
        public PoetryData? Data { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; } = string.Empty;

        [JsonProperty("ipAddress")]
        public string IpAddress { get; set; } = string.Empty;

        [JsonProperty("errCode")]
        public int? ErrorCode { get; set; }

        [JsonProperty("errMessage")]
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 诗词数据
    /// </summary>
    public class PoetryData
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [JsonProperty("popularity")]
        public int Popularity { get; set; }

        [JsonProperty("origin")]
        public OriginInfo? Origin { get; set; }

        [JsonProperty("matchTags")]
        public List<string> MatchTags { get; set; } = new();

        [JsonProperty("recommendedReason")]
        public string RecommendedReason { get; set; } = string.Empty;

        [JsonProperty("cacheAt")]
        public string CacheAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// 诗词来源信息
    /// </summary>
    public class OriginInfo
    {
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("dynasty")]
        public string Dynasty { get; set; } = string.Empty;

        [JsonProperty("author")]
        public string Author { get; set; } = string.Empty;

        [JsonProperty("content")]
        public List<string> Content { get; set; } = new();

        [JsonProperty("translate")]
        public List<string>? Translate { get; set; }
    }

    /// <summary>
    /// Token 响应
    /// </summary>
    public class TokenResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("data")]
        public string Data { get; set; } = string.Empty;
    }
}
