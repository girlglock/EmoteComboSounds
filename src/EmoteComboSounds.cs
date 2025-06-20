using System.Text.Json;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using NAudio.Wave;

namespace TwitchEmoteSoundPlayer
{
    public class Config
    {
        public string Channel { get; set; } = "";
        public int EmoteThreshold { get; set; } = 5;
        public int Cooldown { get; set; } = 5;
        public string SoundsDir { get; set; } = "./sounds";
    }

    class Program
    {
        private static TwitchClient? client;
        private static Config? config;
        private static readonly Dictionary<string, DateTime> availableEmotes = [];
        private static readonly Queue<string> recentMessages = new Queue<string>();
        private static WaveOutEvent? waveOutDevice;
        private static AudioFileReader? audioFileReader;

        static void Main(string[] args)
        {
            if (!LoadConfig())
            {
                Console.WriteLine("press any key to exit...");
                Console.ReadKey();
                return;
            }

            LoadAvailableEmotes();

            if (availableEmotes.Count == 0)
            {
                Console.WriteLine($"no sound files found in {config!.SoundsDir} dir!");
                Console.WriteLine($"please add .mp3 files named after emotes");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"loaded {availableEmotes.Count} emote sounds:");
            foreach (var emote in availableEmotes.Keys)
            {
                Console.WriteLine($"  - {emote}");
            }

            Console.WriteLine($"\nconfig:");
            Console.WriteLine($"  channel: {config!.Channel}");
            Console.WriteLine($"  emote threshold: {config.EmoteThreshold}");
            Console.WriteLine($"  cooldown: {config.Cooldown}s");
            Console.WriteLine($"  sounds directory: {config.SoundsDir}");

            var anonymousCredentials = new ConnectionCredentials("justinfan123", "oauth:justinfan123");
            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = 750,
                ThrottlingPeriod = TimeSpan.FromSeconds(30)
            };
            var customClient = new WebSocketClient(clientOptions);
            client = new TwitchClient(customClient);
            client.Initialize(anonymousCredentials, config.Channel);

            client.OnJoinedChannel += Client_OnJoinedChannel!;
            client.OnMessageReceived += Client_OnMessageReceived!;
            client.OnConnected += Client_OnConnected!;

            client.Connect();

            Console.WriteLine("\npress any key to exit...");
            Console.ReadKey();

            client?.Disconnect();
            waveOutDevice?.Dispose();
            audioFileReader?.Dispose();
        }

        private static bool LoadConfig()
        {
            const string configFile = "config.json";

            if (!File.Exists(configFile))
            {
                var defaultConfig = new Config
                {
                    Channel = "your_channel_name",
                    EmoteThreshold = 5,
                    Cooldown = 5,
                    SoundsDir = "./sounds"
                };

                try
                {
                    var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configFile, json);
                    Console.WriteLine($"created {configFile}. please edit it.");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"error creating config file: {ex.Message}");
                    return false;
                }
            }

            try
            {
                var json = File.ReadAllText(configFile);
                config = JsonSerializer.Deserialize<Config>(json);

                if (config == null)
                {
                    Console.WriteLine($"error: config file is invalid.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(config.Channel) || 
                    config.Channel == "your_channel_name")
                {
                    Console.WriteLine($"error: please set a valid channel in config.json");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error loading config: {ex.Message}");
                return false;
            }
        }

        private static void LoadAvailableEmotes()
        {
            if (!Directory.Exists(config!.SoundsDir))
            {
                Directory.CreateDirectory(config.SoundsDir);
                return;
            }

            var mp3Files = Directory.GetFiles(config.SoundsDir, "*.mp3");
            foreach (var file in mp3Files)
            {
                var emoteName = Path.GetFileNameWithoutExtension(file);
                availableEmotes[emoteName.ToLower()] = DateTime.MinValue;
            }
        }

        private static void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Console.WriteLine($"connected to Twitch IRC");
        }

        private static void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Console.WriteLine($"joined channel: {e.Channel}");
            Console.WriteLine($"listening for emote chains...");
        }

        private static void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            var message = e.ChatMessage.Message.ToLower().Trim();
            
            recentMessages.Enqueue(message);
            
            while (recentMessages.Count > config!.EmoteThreshold)
            {
                recentMessages.Dequeue();
            }

            if (recentMessages.Count == config.EmoteThreshold)
            {
                var messagesArray = recentMessages.ToArray();
                var firstMessage = messagesArray[0];

                if (messagesArray.All(msg => msg == firstMessage) && availableEmotes.ContainsKey(firstMessage))
                {
                    var lastPlayed = availableEmotes[firstMessage];
                    if (DateTime.Now - lastPlayed > TimeSpan.FromSeconds(config.Cooldown))
                    {
                        PlaySound(firstMessage);
                        availableEmotes[firstMessage] = DateTime.Now;
                        
                        Console.WriteLine($"[EMOTE COMBO] {firstMessage}.mp3");
                        
                        recentMessages.Clear();
                    }
                }
            }

            Console.WriteLine($"[{e.ChatMessage.Username}]: {e.ChatMessage.Message}");
        }

        private static void PlaySound(string emoteName)
        {
            try
            {
                var soundFile = Path.Combine(config!.SoundsDir, $"{emoteName}.mp3");
                
                if (!File.Exists(soundFile))
                    return;

                // TODO: add multiple sounds playing at once, for now just end current one :p
                waveOutDevice?.Stop();
                audioFileReader?.Dispose();
                waveOutDevice?.Dispose();

                // TODO: add random speed
                audioFileReader = new AudioFileReader(soundFile);
                waveOutDevice = new WaveOutEvent();
                waveOutDevice.Init(audioFileReader);
                waveOutDevice.Play();

                waveOutDevice.PlaybackStopped += (sender, args) =>
                {
                    audioFileReader?.Dispose();
                    waveOutDevice?.Dispose();
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error playing sound for {emoteName}: {ex.Message}");
            }
        }
    }
}