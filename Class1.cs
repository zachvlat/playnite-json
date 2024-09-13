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
                    // Handle cover image if needed
                    // string coverImagePath = string.Empty;
                    // if (!string.IsNullOrEmpty(game.CoverImage))
                    // {
                    //     coverImagePath = Path.Combine(PlayniteApi.Paths.ImagesPath, game.CoverImage);
                    // }

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

                    gameList.Add(new GameInfo
                    {
                        Name = game.Name,
                        Platform = platformName,
                        Playtime = (long?)game.Playtime,
                        LastPlayed = game.LastActivity,
                        Genres = game.Genres?.Select(g => g.Name).ToList(),
                        Sources = sourceName, // Updated property name
                                              // CoverImagePath = coverImagePath
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
            public string Name { get; set; }
            public string Platform { get; set; }
            public long? Playtime { get; set; }
            public DateTime? LastPlayed { get; set; }
            public List<string> Genres { get; set; }
            public string Sources { get; internal set; }
            // public string CoverImagePath { get; set; } // Uncomment if you decide to include cover images
        }
    }
}
