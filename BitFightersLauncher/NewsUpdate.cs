using System;
using System.Text.Json.Serialization;

namespace BitFightersLauncher
{
    public class NewsUpdate
    {
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
        public string? Content { get; set; } = string.Empty;

        public string ShortContent
        {
            get
            {
                if (!string.IsNullOrEmpty(Content))
                {
                    return Content;
                }

                if (!string.IsNullOrEmpty(CreatedAt) && DateTime.TryParse(CreatedAt, out var createdDate))
                {
                    return createdDate.ToString("yyyy-MM-dd HH:mm");
                }

                return "Dátum nem elérhető";
            }
        }
    }
}