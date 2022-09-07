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
        private static string DATA_LOCATION = (new FileInfo(AppDomain.CurrentDomain.BaseDirectory)).Directory.Parent.FullName;
        private static int RECONNECT_WAIT_MS = 180000; // 3 minutes

        private readonly IConfiguration config;

        private DiscordSocketClient client;
        private CommandHandler commandHandler;
        private CommandService service;

        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public Program()
        {
#if DEBUG
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"stagingconfig.json");
#else
            Console.WriteLine("Looking for Json and DB in: " + dataLocation);
            string jsonPath = Path.Combine(DATA_LOCATION, @"stagingconfig.json");
#endif

            // Create and build the configuration and assign to config          
            config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile(path: jsonPath)
                    .Build();
        }

        public async Task MainAsync()
        {
            client = new DiscordSocketClient();

            // Add the command service to the collection
            service = new CommandService(new CommandServiceConfig
            {
                // Tell the logger to give Verbose amount of info
                LogLevel = LogSeverity.Verbose,

                // Force all commands to run async by default
                DefaultRunMode = RunMode.Async,
            });

            SQLManager sqlManager = new SQLManager(CreateConnection());

            commandHandler = new CommandHandler(client, service, config, sqlManager);

            client.Disconnected += OnDisconnected;

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
                Console.WriteLine("COULD NOT CONNECT TO DB: " + ex.ToString());
            }

            return sqlite_conn;
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        /// <summary>
        /// When disconnected, wait for 3 minutes to reconnect. If not connected again, then restart connection attempt. 
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private async Task OnDisconnected(Exception arg)
        {
            // Immediately try to connect again
            await client.StartAsync();

            // If client is still not connected, try to connect until it is
            while (client.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(RECONNECT_WAIT_MS);
                await client.StartAsync();
            }
        }
    }
}

