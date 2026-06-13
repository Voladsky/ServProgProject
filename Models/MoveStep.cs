using System.Text.Json.Serialization;

namespace ServProgProject.Models
{
    public class MoveStep
    {
        [JsonPropertyName("r")]
        public int Row { get; set; }

        [JsonPropertyName("c")]
        public int Col { get; set; }
    }
}