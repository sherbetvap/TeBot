using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

namespace TeBot
{
    class Program
    {
        private readonly IConfiguration config;
        private DiscordSocketClient client;
        private CommandHandler commandHandler;
        private CommandService service;

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public Program()
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"config.json");

            // create the configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(path: jsonPath);

            // build the configuration and assign to config          
            config = builder.Build();
        }

        public async Task MainAsync()
        {
            client = new DiscordSocketClient();
            service = new CommandService(new CommandServiceConfig
            {                                       // Add the command service to the collection
                LogLevel = LogSeverity.Verbose,     // Tell the logger to give Verbose amount of info
                DefaultRunMode = RunMode.Async,     // Force all commands to run async by default
            });
            commandHandler = new CommandHandler(client, service, config);

            client.Log += Log;
            await client.LoginAsync(TokenType.Bot, config["Token"]);
            await client.StartAsync();
            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
    
