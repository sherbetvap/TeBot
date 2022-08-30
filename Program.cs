using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;

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

            // Create and build the configuration and assign to config          
            config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(path: jsonPath)
                .Build();
        }

        public async Task MainAsync()
        {
            SQLiteConnection sqlite = CreateConnection();
            client = new DiscordSocketClient();

            // Add the command service to the collection
            service = new CommandService(new CommandServiceConfig
            {
                // Tell the logger to give Verbose amount of info
                LogLevel = LogSeverity.Verbose,

                // Force all commands to run async by default
                DefaultRunMode = RunMode.Async,
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
            // Create a new database connection:
            SQLiteConnection sqlite_conn = new SQLiteConnection("Data Source=TeDB.db;Version=3;New=False;Compress=True;");

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

