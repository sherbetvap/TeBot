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
using System.Data.SQLite;

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
#if DEBUG
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"stagingconfig.json");
#else
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"config.json");            
#endif
            // create the configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(path: jsonPath);

            // build the configuration and assign to config          
            config = builder.Build();
        }

        public async Task MainAsync()
        {
            SQLiteConnection sqlite;
            sqlite = CreateConnection();

            client = new DiscordSocketClient();
            service = new CommandService(new CommandServiceConfig
            {                                       // Add the command service to the collection
                LogLevel = LogSeverity.Verbose,     // Tell the logger to give Verbose amount of info
                DefaultRunMode = RunMode.Async,     // Force all commands to run async by default
            });
            commandHandler = new CommandHandler(client, service, config, sqlite);

            client.Log += Log;
            await client.LoginAsync(TokenType.Bot, config["Token"]);
            await client.StartAsync();
            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        static SQLiteConnection CreateConnection()
        {
            SQLiteConnection sqlite_conn;
            // Create a new database connection:
            sqlite_conn = new SQLiteConnection("Data Source=TeDB.db;Version=3;New=False;Compress=True;");
            // Open the connection:
            try
            {
                sqlite_conn.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine("COULD NOT CONNECT TO DB");
            }
            return sqlite_conn;
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
    
