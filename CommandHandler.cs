using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using System.Data.SQLite;

namespace TeBot
{
    public class CommandHandler
    {
        private const string ADMIN_ONLY = "0";
        private const string MOD_ONLY = "1";
        private const string EVERYONE = "2";
        private readonly IConfiguration config;
        private readonly DiscordSocketClient discord;
        private readonly CommandService commands;
        private readonly ulong serverID;
        private IEnumerable<IConfigurationSection> channelEnumeration;
        private IEnumerable<IConfigurationSection> crosspostChannelEnumeration;
        private SQLiteConnection sqlite;
        private SQLiteDataReader sqlite_datareader;
        private SQLiteCommand sqlite_cmd;
        private Dictionary<ulong, ulong> crosspostChannelsDictionary;

        // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
        public CommandHandler(DiscordSocketClient discord, CommandService commands, IConfiguration config, SQLiteConnection sqlite)
        {
            this.discord = discord;
            this.commands = commands;
            this.config = config;
            this.sqlite = sqlite;
            crosspostChannelsDictionary = new Dictionary<ulong, ulong>();

            // Get key/value pairs for lists of channels
            channelEnumeration = config.GetSection("ChannelList").GetChildren();
            crosspostChannelEnumeration = config.GetSection("ChannelsCrossPost").GetChildren();
            InitiateCrosspostChannels();
            serverID = ParseStringToUlong(config["serverID"]);

            // Load modules
            commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
       
            // Set delegate to go off for every message
            this.discord.MessageReceived += OnMessageReceivedAsync;
            // Set delegate to go off every delete
            this.discord.MessageDeleted += OnMessageDeletedAsync;
        }

        /// <summary>
        /// On message deleted, check to see if channel is a gallery channel, and check DB to see if ID of message exists.
        /// </summary>
        /// <param name="sourceMessage"></param>
        /// <param name="sourceChannel"></param>
        /// <returns></returns>
        private async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> sourceMessage, ISocketMessageChannel sourceChannel)
        {
            sqlite_cmd = sqlite.CreateCommand();
            sqlite_cmd.CommandText = "SELECT LinkID FROM SourceLinkIDPairs WHERE SourceID = " + sourceMessage.Id + ";";
            sqlite_datareader = sqlite_cmd.ExecuteReader();
            ulong readLinkId = 0;
            if (sqlite_datareader.Read())
            {
                readLinkId = (ulong) sqlite_datareader.GetInt64(0);
            }

            // Get channel posted from
            // If it matches a crosspost channel then get the value from key
            // use value to access channel and get the message

            // Link id exists, delete message and remove from DB
            if (readLinkId != 0)
            {
                ulong channelToDeleteFrom = crosspostChannelsDictionary[sourceChannel.Id];

                // LOOK ARIA THE MESSEGE IS DELETED
                // Hi this is Aria, good job Coffvee!
                await discord.GetGuild(serverID).GetTextChannel(channelToDeleteFrom).DeleteMessageAsync(readLinkId);

                // Delete entry from table
                sqlite_cmd = sqlite.CreateCommand();
                sqlite_cmd.CommandText = "DELETE FROM SourceLinkIDPairs WHERE SourceID = " + sourceMessage.Id + ";";
                sqlite_cmd.ExecuteNonQuery();
            }            
        }

        /// <summary>
        /// Parse a message and perform any required actions.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            var msg = s as SocketUserMessage;                       // Ensure the message is from a user/bot
            if (msg == null) return;
            if (msg.Author.Id == discord.CurrentUser.Id) return;    // Ignore self when checking commands

            var context = new SocketCommandContext(discord, msg);   // Create the command context
            var userPerms = (context.User as IGuildUser).GuildPermissions;

            int argPos = 0;
            bool isCommand = msg.HasStringPrefix(config["Prefix"], ref argPos) || msg.HasMentionPrefix(discord.CurrentUser, ref argPos);
            bool isModOnlyAndModMsg = config["EditableBy"].Equals(MOD_ONLY) && (userPerms.ManageChannels || userPerms.Administrator);
            bool isAdmOnlyAndAdmMsg = config["EditableBy"].Equals(ADMIN_ONLY) && userPerms.Administrator;
            // Check if the message has a valid command prefix, or is mentioned. 
            // Check if allowed by everyone, or if admin only and then make sure user is admin            
            if ( isCommand && (config["EditableBy"].Equals(EVERYONE) || isModOnlyAndModMsg || isAdmOnlyAndAdmMsg) )                 
            {
                var result = await commands.ExecuteAsync(context, argPos, null);     // Execute the command

                if (!result.IsSuccess)                              // If not successful, reply with the error.
                    await context.Channel.SendMessageAsync(result.ToString());
            }
            else // If it is not a command check what channel it is
            {
                // Wait to allow any embeds to appear
                Thread.Sleep(5000);                 
                
                // Check if key matches the context channel ID
                foreach (var channel in crosspostChannelEnumeration)
                {        
                    // Parse key string to ulong 
                    ulong channelID = ParseStringToUlong(channel.Key);                    

                    // Test to see if key matches context. If it does, get the value. That is the channel to post to.
                    if (context.Channel.Id == channelID)
                    {
                        ulong channelTo = ParseStringToUlong(channel.Value);

                        // Only send message if parse succeeded 
                        if (channelTo != 0)
                            await LinkImagesToOtherChannel(context, channelTo);
                    }
                }
            }
        }

        /// <summary>
        /// Takes in the context and ulong, posting images and embedded links from context channel to ulong channel
        /// </summary>
        /// <param name="context"></param>
        /// <param name="channelTo"></param>
        /// <returns></returns>
        private async Task LinkImagesToOtherChannel(SocketCommandContext context, ulong channelTo)
        {
            string lastString = "";

            // Refresh message to retrieve generated embeds
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            // Message must contain a link or file or else it will not be copied
            if (refreshedMessage.Attachments.Count > 0 || refreshedMessage.Embeds.Count > 0)
            {
                StringBuilder message = new StringBuilder();

                // Display files first then link
                foreach (var attachment in refreshedMessage.Attachments)
                {
                    message.Append(attachment.Url + "\n");
                }
                foreach (var embed in refreshedMessage.Embeds)
                {
                    // Makes sure it is not geting the same url from last time
                    if (!lastString.Equals(embed.Url))
                        message.Append(embed.Url + "\n");
                    lastString = embed.Url;
                }

                // Send message
                var sentMessage = await context.Guild.GetTextChannel(channelTo).SendMessageAsync(message.ToString());
                
                // Insert into database
                sqlite_cmd = sqlite.CreateCommand();
                sqlite_cmd.CommandText = "INSERT INTO SourceLinkIDPairs (SourceID, LinkID) VALUES (" + context.Message.Id + ", " + sentMessage.Id + ");";
                sqlite_cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Parse a string representation of ulong. Returns 0 if failed.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private ulong ParseStringToUlong(string s)
        {
            ulong channelID = 0;

            try { channelID = Convert.ToUInt64(s); }
            catch (Exception) { Console.WriteLine("Failed to parse" + s); }

            return channelID;
        }

        /// <summary>
        /// Fills dictionary with crossposting channels
        /// </summary>
        private void InitiateCrosspostChannels()
        {
            foreach (var channel in crosspostChannelEnumeration)
            {
                // Parse key string to ulong 
                ulong channelFrom = ParseStringToUlong(channel.Key);

                ulong channelTo = ParseStringToUlong(channel.Value);

                crosspostChannelsDictionary.Add(channelFrom, channelTo);
            }
        }
    }    
}
