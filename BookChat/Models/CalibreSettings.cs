namespace BookChat.Models
{
    /// <summary>
    /// Model for persisting Calibre integration settings
    /// </summary>
    public class CalibreSettings
    {
        public string LibraryPath { get; set; } = string.Empty;
        public bool AutoImport { get; set; } = false;
        public string DuplicateHandling { get; set; } = "ignore";
        public string ContentServerUrl { get; set; } = "http://localhost:8080";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
