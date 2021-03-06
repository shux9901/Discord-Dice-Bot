﻿using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/* To run this bot, you need tokens.json at the working directory.
 * Sample of tokens.json:
 * {
 *    "debug": "<CLIENT-TOKEN-FOR-DEBUG>",
 *    "release": "<CLIENT-TOKEN-FOR-RELEASE>"
 * }
 * 
 */
namespace DiscordDice
{
    internal static class Configuration
    {
        public static bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                ConsoleEx.WriteError("Received UnobservedTaskException:");
                ConsoleEx.WriteError(e.Exception.GetType().ToString());
                ConsoleEx.WriteError(e.Exception.Message);
                ConsoleEx.WriteError(e.Exception.StackTrace);
                var aggregateException = e.Exception as AggregateException;
                if (aggregateException == null)
                {
                    return;
                }
                ConsoleEx.WriteError("The exception is AggregateException. InnerExceptions are...:");
                foreach (var (ex, i) in aggregateException.InnerExceptions.Select((ex, i) => (ex, i)))
                {
                    ConsoleEx.WriteError($"Exception {i}");
                    ConsoleEx.WriteError(ex.GetType().ToString());
                    ConsoleEx.WriteError(ex.Message);
                    ConsoleEx.WriteError(ex.StackTrace);
                }
                var areExceptionsOk =
                    (aggregateException
                    ?.InnerExceptions
                    ?.AsEnumerable()
                    ?? Enumerable.Empty<Exception>())
                    .All(ex => ex is WebSocketException);
                if (!areExceptionsOk)
                {
                    ConsoleEx.WriteError("Stopping bot...(Make sure \"Restart\" and \"RestartSec\" are set at service file of systemctl)");
                    throw e.Exception;
                }
            };

            await ConnectDiscordAsync();
        }

        public static async Task<string> GetTokenAsync()
        {
            var notFoundErrorMessage = "tokens.json is not found. Requires tokens.json to run this bot.";
            var invalidJsonMessage = "Could not find a token in tokens.json.";

            var path = Path.Combine(Environment.CurrentDirectory, "tokens.json");
            string jsonText;
            try
            {
                jsonText = await File.ReadAllTextAsync(path);
            }
            catch (FileNotFoundException e)
            {
                throw new DiscordDiceException(notFoundErrorMessage, e);
            }

            JObject json = JObject.Parse(jsonText);
            var key = Configuration.IsDebug ? "debug" : "release";
            if (json.TryGetValue(key, out var value))
            {
                string result;
                try
                {
                    result = value.ToObject<string>();
                }
                catch (ArgumentException)
                {
                    throw new DiscordDiceException(invalidJsonMessage);
                }
                return result;
            }
            throw new DiscordDiceException(invalidJsonMessage);
        }

        static async Task ConnectDiscordAsync()
        {
            if (Configuration.IsDebug)
            {
                Console.WriteLine("Configuration is DEBUG.");
            }
            else
            {
                Console.WriteLine("Configuration is RELEASE.");
            }
            using (var context = MainDbContext.GetInstance(Config.Default))
            {
                Console.WriteLine("Ensuring database is created...");
                await context.Database.EnsureCreatedAsync();
                Console.WriteLine("Ensured database is created.");
            }
            Console.WriteLine("Starting...");

            var client = new DiscordSocketClient();

            client.Log += OnLog;
            var entrance = new MessageEntrance(new LazySocketClient(client), Config.Default);
            ResponsesSender.Start(entrance.ResponseSent);
            client.MessageReceived += async message =>
            {
                await entrance.ReceiveMessageAsync(new LazySocketMessage(message), client.CurrentUser.Id);
            };

            var token = await GetTokenAsync();
            Console.WriteLine("Logging in...");
            await client.LoginAsync(TokenType.Bot, token);
            Console.WriteLine("Logged in.");
            Console.WriteLine("Starting...");
            await client.StartAsync();
            Console.WriteLine("Started. Bot is working now.");

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private static Task OnLog(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
