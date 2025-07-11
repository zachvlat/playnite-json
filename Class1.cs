using Playnite.SDK;
using Playnite.SDK.Plugins;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Threading.Tasks;

namespace playnite_json
{
    [Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4")]
    public class ExportGamesPlugin : GenericPlugin
    {
        private readonly IGameDatabase _gameDatabase;
        private readonly Guid steamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const string ClientId = "ENV";
        private const string ClientSecret = "ENV";
        private string _igdbAccessToken;

        public ExportGamesPlugin(IPlayniteAPI api) : base(api)
        {
            _gameDatabase = api.Database;
        }

        public override Guid Id { get; } = new Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4");

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            var result = PlayniteApi.Dialogs.ShowMessage(
                "Do you want to export your game library?",
                "Confirm Export",
                MessageBoxButton.YesNo
            );

            if (result != MessageBoxResult.Yes)
            {
                Logger.Info("Game export canceled by user.");
                return;
            }

            PlayniteApi.Notifications.Add(new NotificationMessage(
                "playnite-json-export-started",
                "Game export started in the background.",
                NotificationType.Info
            ));

            Task.Run(() =>
            {
                _igdbAccessToken = GetIgdbAccessToken();
                ExportGamesToJson();
            });
        }

        private void ExportGamesToJson()
        {
            try
            {
                string filePath = Path.Combine(PlayniteApi.Paths.ApplicationPath, "games_export.json");
                Logger.Info($"Export file path: {filePath}");

                List<GameInfo> existingGames = null;

                if (File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(filePath);
                        existingGames = JsonConvert.DeserializeObject<List<GameInfo>>(existingJson) ?? new List<GameInfo>();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to read existing JSON file.");
                        existingGames = new List<GameInfo>();
                    }
                }

                var games = _gameDatabase.Games;
                var gameList = new List<GameInfo>();
                int totalGames = games.Count();
                int processed = 0;
                bool changesDetected = false;

                foreach (var game in games)
                {
                    processed++;

                    var platformName = game.PlatformIds?.Any() == true
                        ? _gameDatabase.Platforms.Get(game.PlatformIds.First())?.Name ?? "Unknown"
                        : "Unknown";

                    var sourceName = game.Source?.Name ?? "Unknown";
                    string steamId = game.PluginId == steamPluginId ? game.GameId : null;

                    string coverArtUrl = GetIgdbCoverUrl(game.Name) ??
                                         (steamId != null ? $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{steamId}/library_600x900.jpg"
                                                          : "https://placehold.co/60x60.svg");

                    var communityHubUrl = game.Links?.FirstOrDefault(link => link.Name.Contains("Community"))?.Url;

                    var newGame = new GameInfo
                    {
                        Id = game.Id,
                        Name = game.Name,
                        Description = game.Description,
                        Platform = platformName,
                        Playtime = (long?)game.Playtime,
                        LastPlayed = game.LastActivity,
                        Genres = game.Genres?.Select(g => g.Name).ToList(),
                        Sources = sourceName,
                        ReleaseDate = game.ReleaseDate?.Date,
                        CommunityHubUrl = communityHubUrl,
                        Added = game.Added,
                        SteamId = steamId,
                        CoverArtUrl = coverArtUrl,
                        CommunityScore = game.CommunityScore,
                        CriticScore = game.CriticScore,
                        UserScore = game.UserScore
                    };

                    var existingGame = existingGames?.FirstOrDefault(g => g.Id == newGame.Id);
                    if (existingGame != null)
                    {
                        if (!JsonConvert.SerializeObject(existingGame).Equals(JsonConvert.SerializeObject(newGame)))
                        {
                            Logger.Info($"Updating game: {newGame.Name}");
                            gameList.Add(newGame);
                            changesDetected = true;
                        }
                        else
                        {
                            gameList.Add(existingGame);
                        }
                    }
                    else
                    {
                        Logger.Info($"Adding new game: {newGame.Name}");
                        gameList.Add(newGame);
                        changesDetected = true;
                    }
                }

                if (!changesDetected)
                {
                    Logger.Info("No changes detected, skipping save.");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "playnite-json-no-changes",
                        $"No changes detected — existing export is up to date. Location: {filePath}",
                        NotificationType.Info
                    ));
                    return;
                }

                string json = JsonConvert.SerializeObject(gameList, Formatting.Indented);
                File.WriteAllText(filePath, json);

                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "playnite-json-export-complete",
                    $"Export complete! {processed} games saved to: {filePath}",
                    NotificationType.Info
                ));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to export games.");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "playnite-json-export-error",
                    $"An error occurred: {ex.Message}",
                    NotificationType.Error
                ));
            }
        }

        private string GetIgdbAccessToken()
        {
            try
            {
                using (var client = new WebClient())
                {
                    var values = new System.Collections.Specialized.NameValueCollection
                    {
                        { "client_id", ClientId },
                        { "client_secret", ClientSecret },
                        { "grant_type", "client_credentials" }
                    };

                    var response = client.UploadValues("https://id.twitch.tv/oauth2/token", values);
                    var responseString = Encoding.Default.GetString(response);
                    var json = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseString);

                    return json["access_token"];
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get IGDB access token.");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "playnite-json-igdb-error",
                    $"Failed to get IGDB token: {ex.Message}",
                    NotificationType.Error
                ));
                return null;
            }
        }

        private string GetIgdbCoverUrl(string gameName)
        {
            if (string.IsNullOrEmpty(_igdbAccessToken))
                return null;

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("Client-ID", ClientId);
                    client.Headers.Add("Authorization", $"Bearer {_igdbAccessToken}");
                    client.Headers.Add("Accept", "application/json");

                    string query = $"search \"{gameName}\"; fields cover.image_id; limit 1;";
                    var response = client.UploadString("https://api.igdb.com/v4/games", query);
                    var games = JsonConvert.DeserializeObject<List<dynamic>>(response);

                    if (games.Count > 0 && games[0].cover != null)
                    {
                        string imageId = games[0].cover.image_id.ToString();
                        return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to fetch IGDB cover for {gameName}.");
            }

            return null;
        }

        private class GameInfo
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string Platform { get; set; }
            public long? Playtime { get; set; }
            public DateTime? LastPlayed { get; set; }
            public List<string> Genres { get; set; }
            public string Sources { get; set; }
            public DateTime? ReleaseDate { get; set; }
            public string CommunityHubUrl { get; set; }
            public DateTime? Added { get; set; }
            public string SteamId { get; set; }
            public string CoverArtUrl { get; set; }
            public float? CommunityScore { get; set; }
            public int? CriticScore { get; set; }
            public float? UserScore { get; set; }
        }
    }
}