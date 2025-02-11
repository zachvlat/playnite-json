using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK.Events;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Runtime;
using Playnite.SDK.Models;

namespace playnite_json
{
    [Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4")]
    public class ExportGamesPlugin : GenericPlugin
    {
        private readonly IGameDatabase _gameDatabase;
        private readonly Guid steamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private static readonly ILogger Logger = LogManager.GetLogger();

        private const string ClientId = "un7v7t5r1fv7mo9fpsl5rtvmjhjttv";
        private const string ClientSecret = "de80exz3avpf7tiet24kchis7tan5k";
        private string _igdbAccessToken;

        public ExportGamesPlugin(IPlayniteAPI api) : base(api)
        {
            _gameDatabase = api.Database;
        }

        public override Guid Id { get; } = new Guid("1B6AA61E-561C-47F4-9AED-15FFEB574CF4");

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            //// Ask for user confirmation
            //var result = PlayniteApi.Dialogs.ShowMessage(
            //    "Do you want to export your game library?",
            //    "Confirm Export",
            //    MessageBoxButton.YesNo
            //);

            //if (result != MessageBoxResult.Yes)
            //{
            //    Logger.Info("Game export canceled by user.");
            //    return;
            //}

            //ExportGames(_gameDatabase.Games);
        }

        private void ExportGames(List<Game> games)
        {
            _igdbAccessToken = GetIgdbAccessToken();

            // Show progress UI while exporting games
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                progress.Text = "Exporting game library...";
                ExportGamesToJson(progress, games);
            }, new GlobalProgressOptions("Exporting Games", true));
        }

        private static readonly string _menuSection = "Json Exporter";
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return new List<GameMenuItem>
                {
                    new GameMenuItem
                    {
                        Description = "Export",
                        MenuSection = $"{_menuSection}",                        
                        Action = a => {
                           ExportGames(args.Games);
                        }
                    }

                };
        }

        private void ExportGamesToJson(GlobalProgressActionArgs progress, List<Game> games)
        {
            try
            {                
                var gameList = new List<GameInfo>();
                int totalGames = games.Count();
                int processed = 0;

                foreach (var game in games)
                {
                    if (progress.CancelToken.IsCancellationRequested)
                    {
                        Logger.Warn("Game export canceled by user.");
                        return;
                    }

                    processed++;
                    progress.CurrentProgressValue = (processed / (double)totalGames) * 100;
                    progress.Text = $"Exporting {processed}/{totalGames}: {game.Name}";

                    var platformName = game.PlatformIds?.Any() == true
                        ? _gameDatabase.Platforms.Get(game.PlatformIds.First())?.Name ?? "Unknown"
                        : "Unknown";

                    var sourceName = game.Source?.Name ?? "Unknown";
                    string steamId = game.PluginId == steamPluginId ? game.GameId : null;

                    string coverArtUrl = GetIgdbCoverUrl(game.Name) ??
                                         (steamId != null ? $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{steamId}/library_600x900.jpg"
                                                          : "https://placehold.co/60x60.svg");

                    var communityHubUrl = game.Links?.FirstOrDefault(link => link.Name.Contains("Community"))?.Url;

                    gameList.Add(new GameInfo
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
                        CoverArtUrl = coverArtUrl
                    });
                }

                string json = JsonConvert.SerializeObject(gameList, Formatting.Indented);
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string filePath = Path.Combine(path, "games_export.json");
                File.WriteAllText(filePath, json);

                PlayniteApi.Dialogs.ShowMessage($"Export complete! {processed} games saved to:\n{filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to export games.");
                PlayniteApi.Dialogs.ShowErrorMessage($"An error occurred: {ex.Message}", "Export Error");
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
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to get IGDB token: {ex.Message}", "IGDB Error");
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

                    gameName = RemoveEditions(gameName);

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

        public static string RemoveEditions(string name)
        {
            string cleanName = name.Replace(": Windows Edition", "");
            cleanName = cleanName.Replace(" (Windows Version)", "");
            cleanName = cleanName.Replace(" - Standard Edition", "");
            cleanName = cleanName.Replace(" - Ultimate Edition", "");
            cleanName = cleanName.Replace(" - Windows Edition", "");
            cleanName = cleanName.Replace(" - Limited Edition", "");
            cleanName = cleanName.Replace(" - Gold Edition", "");
            cleanName = cleanName.Replace(" - Special Edition", "");
            cleanName = cleanName.Replace(" - Definitive Edition", "");
            cleanName = cleanName.Replace(" - Collector's Edition", "");
            cleanName = cleanName.Replace(" - Base Game", "");
            cleanName = cleanName.Replace(" - PC Edition", "");
            cleanName = cleanName.Replace(" - Complete Edition", "");
            cleanName = cleanName.Replace(" - Complete Bundle", "");
            cleanName = cleanName.Replace(" - Game of the Year Edition", "");
            cleanName = cleanName.Replace(" - Nintendo Switch Edition", "");

            cleanName = cleanName.Replace(": Complete Edition", "");
            cleanName = cleanName.Replace(": Challenger Edition", "");
            cleanName = cleanName.Replace(": The Complete Edition", "");
            cleanName = cleanName.Replace(" Game of the Year Edition", "");
            cleanName = cleanName.Replace(" GAME OF THE YEAR EDITION", "");

            cleanName = cleanName.Replace(" Standard Edition", "");
            cleanName = cleanName.Replace(" Ultimate Edition", "");
            cleanName = cleanName.Replace(" Windows Edition", "");
            cleanName = cleanName.Replace(" Limited Edition", "");
            cleanName = cleanName.Replace(" Gold Edition", "");
            cleanName = cleanName.Replace(" Special Edition", "");
            cleanName = cleanName.Replace(" Definitive Edition", "");
            cleanName = cleanName.Replace(" Collector's Edition", "");
            cleanName = cleanName.Replace(" Base Game", "");
            cleanName = cleanName.Replace(" PC Edition", "");
            cleanName = cleanName.Replace(" Complete Edition", "");
            cleanName = cleanName.Replace(" Complete Bundle", "");
            cleanName = cleanName.Replace(" Classic Edition", "");
            cleanName = cleanName.Replace(" Console Edition", "");
            cleanName = cleanName.Replace(" Enhanced Edition", "");
            cleanName = cleanName.Replace(" Deluxe Edition", "");
            cleanName = cleanName.Replace(" Legacy Edition", "");
            cleanName = cleanName.Replace(" Challenger Edition", "");
            cleanName = cleanName.Replace(" Collector's Edition", "");
            cleanName = cleanName.Replace(" (Windows Edition)", "");
            cleanName = cleanName.Replace(" for Nintendo Switch", "");
            cleanName = cleanName.Replace(" Digital Bonus Edition", "");
            cleanName = cleanName.Replace(" (Game Preview)", "");
            cleanName = cleanName.Replace(" PS4 & PS5", "");

            // complete                        
            cleanName = cleanName.Replace(" Console", "");
            cleanName = cleanName.Replace(" for Windows", "");
            cleanName = cleanName.Replace(" Collection", "");
            cleanName = cleanName.Replace(" Edition", "");

            return cleanName;
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
        }
    }
}
