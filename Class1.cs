using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK.Events;
using System.Runtime.InteropServices;

namespace playnite_json
{
    [Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4")]
    public class ExportGamesPlugin : GenericPlugin
    {
        private readonly IGameDatabase _gameDatabase;

        public ExportGamesPlugin(IPlayniteAPI api) : base(api)
        {
            _gameDatabase = api.Database;
        }

        public override Guid Id { get; } = new Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4");

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);
            ExportGamesToJson();
        }

        private void ExportGamesToJson()
        {
            try
            {
                var games = _gameDatabase.Games;
                var gameList = new List<GameInfo>();

                foreach (var game in games)
                {
                    // Get platform name
                    var platformName = string.Empty;
                    if (game.PlatformIds?.Any() == true)
                    {
                        var platform = _gameDatabase.Platforms.Get(game.PlatformIds.First());
                        platformName = platform?.Name ?? "Unknown";
                    }

                    // Get source/library name
                    var sourceName = string.Empty;
                    if (game.Source != null)
                    {
                        sourceName = game.Source.Name; // Assuming Source has a Name property
                    }

                    // Extract the community hub URL from game links
                    var communityHubUrl = game.Links?.FirstOrDefault(link => link.Name.Contains("Community"))?.Url;

                    gameList.Add(new GameInfo
                    {
                        Id = game.Id, // Unique game ID
                        Name = game.Name,
                        Description = game.Description, // Game description
                        Platform = platformName,
                        Playtime = (long?)game.Playtime,
                        LastPlayed = game.LastActivity,
                        Genres = game.Genres?.Select(g => g.Name).ToList(),
                        Sources = sourceName,
                        ReleaseDate = game.ReleaseDate?.Date, // Convert Playnite's ReleaseDate to DateTime?
                        CommunityHubUrl = communityHubUrl, // Use the first link that contains "Community"
                        Added = game.Added // Date game was added to the Playnite library
                    });
                }

                // Serialize to JSON and write to file
                string json = JsonConvert.SerializeObject(gameList, Formatting.Indented);
                string filePath = Path.Combine(PlayniteApi.Paths.ConfigurationPath, "games_export.json");
                File.WriteAllText(filePath, json);

                // Notify user
                PlayniteApi.Dialogs.ShowMessage($"Games exported to {filePath}");
            }
            catch (Exception ex)
            {
                PlayniteApi.Dialogs.ShowErrorMessage($"An error occurred: {ex.Message}", "Error");
            }
        }


        private class GameInfo
        {
            public Guid Id { get; set; } // Unique game ID (NEW)
            public string Name { get; set; }
            public string Description { get; set; } // Game description (NEW)
            public string Platform { get; set; }
            public long? Playtime { get; set; }
            public DateTime? LastPlayed { get; set; }
            public List<string> Genres { get; set; }
            public string Sources { get; internal set; }
            public DateTime? ReleaseDate { get; set; } // Release date (NEW)
            public string CommunityHubUrl { get; set; } // Community hub URL (NEW)
            public DateTime? Added { get; set; } // Date game was added to the library (NEW)
        }
    }
}
