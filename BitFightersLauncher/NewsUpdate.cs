using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace BitFightersLauncher
{
    public class NewsUpdate
    {
        public string Title { get; set; } = string.Empty;
        // A dátumot string-ként tároljuk, hogy a JSON feldolgozó ne jelezzen hibát
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

                // Manuálisan próbáljuk meg átalakítani a stringet DateTime-má
                if (!string.IsNullOrEmpty(CreatedAt) && DateTime.TryParse(CreatedAt, out var createdDate))
                {
                    return createdDate.ToString("yyyy-MM-dd HH:mm");
                }

                return "Dátum nem elérhető";
            }
        }
    }
}