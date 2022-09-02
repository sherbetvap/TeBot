using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TeBot
{
    public class CommandHandler
    {
        private const string ADMIN_ONLY = "0", MOD_ONLY = "1", EVERYONE = "2";

        private const string CROSSPOST_HEADER_0 = "Posted by ", CROSSPOST_HEADER_1 = ":";
        private const string OTHER_HEADER_0 = "Video from ", OTHER_HEADER_1 = "'s Twitter link:";
        private const string TWITTER_URL = "https://twitter.com/", FXTWITTER_URL = "https://fxtwitter.com/", DFXTWITTER_URL = "https://d.fxtwitter.com/";
        private const char TWITTER_TRACKING_INFO_SYMBOL = '?';

        private const string CROSSPOST_INSERT_0 = "INSERT INTO SourceLinkIDPairs (SourceID, LinkID) VALUES (", CROSSPOST_INSERT_1 = ",", CROSSPOST_INSERT_2 = ")";
        private const string CROSSPOST_SELECT = "SELECT LinkID FROM SourceLinkIDPairs WHERE SourceID = ";
        private const string CROSSPOST_DELETE = "DELETE FROM SourceLinkIDPairs WHERE SourceID = ";

        private const int CROSSPOST_WAIT_MS = 5000, FXTWITTER_WAIT_MS = 2000;

        private readonly DiscordSocketClient discord;
        private readonly CommandService commands;
        private readonly Dictionary<ulong, ulong> crosspostChannelsDictionary;
        private readonly ulong serverID;
        private readonly string commandPrefix;
        private readonly string editableBy;

        private SQLiteConnection sqlite;
        private SQLiteDataReader sqlite_datareader;
        private SQLiteCommand sqlite_cmd;

        // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
        public CommandHandler(DiscordSocketClient discord, CommandService commands, IConfiguration config, SQLiteConnection sqlite)
        {
            this.discord = discord;
            this.commands = commands;
            this.sqlite = sqlite;

            // Get key/value pairs for lists of channels
            this.crosspostChannelsDictionary = CreateCrosspostChannelDictionary(config.GetSection("ChannelsCrossPost").GetChildren());
            this.serverID = ParseStringToUlong(config["serverID"]);
            this.commandPrefix = config["Prefix"];
            this.editableBy = config["EditableBy"];

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
            sqlite_cmd.CommandText = CROSSPOST_SELECT + sourceMessage.Id;
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
                // P.S., you misspelled "message"
                try
                {
                    await discord.GetGuild(serverID).GetTextChannel(channelToDeleteFrom).DeleteMessageAsync(readLinkId);
                }
                finally
                {
                    // Delete entry from table
                    sqlite_cmd = sqlite.CreateCommand();
                    sqlite_cmd.CommandText = CROSSPOST_DELETE + sourceMessage.Id;
                    sqlite_cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Parse a message and perform any required actions.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            var msg = s as SocketUserMessage;
            // Ensure the message is from a user/bot
            if (msg == null) return;

            // Ignore self when checking commands
            if (msg.Author.Id == discord.CurrentUser.Id) return;

            // Create the command context
            var context = new SocketCommandContext(discord, msg);
            var userPerms = (context.User as IGuildUser).GuildPermissions;
            int argPos = 0;

            // Check if the message has a valid command prefix, or is mentioned. 
            // Check if allowed by everyone, or if admin only and then make sure user is admin            
            if (isCommand(msg, ref argPos) && (isEverybody() || isModOnlyAndModMsg(userPerms) || isAdmOnlyAndAdmMsg(userPerms)))
            {
                // Execute the command
                var result = await commands.ExecuteAsync(context, argPos, null);

                // If not successful, reply with the error.
                if (!result.IsSuccess)
                    await context.Channel.SendMessageAsync(result.ToString());
            }
            else // If it is not a command check what channel it is
            {
                // Check if key matches the context channel ID
                if (crosspostChannelsDictionary.TryGetValue(context.Channel.Id, out ulong channelToPostTo))
                {
                    // Wait to allow any embeds to appear
                    Thread.Sleep(CROSSPOST_WAIT_MS);
                    await LinkImagesToOtherChannel(context, channelToPostTo);
                }
                // We probably only want to include bot fxtwitter posts on channels people aren't posting their created art
                else
                {
                    // Wait to allow any embeds to appear
                    Thread.Sleep(FXTWITTER_WAIT_MS);
                    await SendFxtwitterUrlsIfNeeded(context);
                }
            }
        }

        private bool isCommand(SocketUserMessage msg, ref int argPos)
        {
            return msg.HasStringPrefix(commandPrefix, ref argPos) || msg.HasMentionPrefix(discord.CurrentUser, ref argPos);
        }

        private bool isEverybody()
        {
            return editableBy.Equals(EVERYONE);
        }

        private bool isModOnlyAndModMsg(GuildPermissions userPerms)
        {
            return editableBy.Equals(MOD_ONLY) && (userPerms.ManageChannels || userPerms.Administrator);
        }

        private bool isAdmOnlyAndAdmMsg(GuildPermissions userPerms)
        {
            return editableBy.Equals(ADMIN_ONLY) && userPerms.Administrator;
        }

        private async Task SendFxtwitterUrlsIfNeeded(SocketCommandContext context)
        {
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            HashSet<string> appendedEmbedUrls = new HashSet<string>();
            bool containsTwitterVideo = false;

            StringBuilder message = new StringBuilder().Append(OTHER_HEADER_0).Append(refreshedMessage.Author.Username).AppendLine(OTHER_HEADER_1);
            foreach (var embed in refreshedMessage.Embeds)
            {
                bool isTwitterVideo = IsTwitterUrl(embed.Url) && embed.Video != null;
                containsTwitterVideo |= isTwitterVideo;

                if (isTwitterVideo)
                {
                    string urlToAppend = FormatTwitterUrl(embed.Url, false);

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                        message.AppendLine(urlToAppend);
                }
            }

            if (containsTwitterVideo)
            {
                // TODO: remove embeds from original message if possible
                await context.Channel.SendMessageAsync(message.ToString());
            }
        }

        /// <summary>
        /// Takes in the context and ulong, posting images and embedded links from context channel to ulong channel
        /// </summary>
        /// <param name="context"></param>
        /// <param name="channelTo"></param>
        /// <returns></returns>
        private async Task LinkImagesToOtherChannel(SocketCommandContext context, ulong channelToPostTo)
        {
            // Refresh message to retrieve generated embeds
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            // Message must contain a link or file or else it will not be copied
            if (refreshedMessage.Attachments.Count > 0 || refreshedMessage.Embeds.Count > 0)
            {
                StringBuilder message = new StringBuilder().Append(CROSSPOST_HEADER_0).Append(refreshedMessage.Author.Username).AppendLine(CROSSPOST_HEADER_1);

                // Display files first then link
                foreach (var attachment in refreshedMessage.Attachments)
                {
                    message.Append(attachment.Url + "\n");
                }

                HashSet<string> appendedEmbedUrls = new HashSet<string>();
                foreach (var embed in refreshedMessage.Embeds)
                {
                    string urlToAppend = IsTwitterUrl(embed.Url) ? FormatTwitterUrl(embed.Url, true) : embed.Url;

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                        message.AppendLine(urlToAppend);
                }

                IUserMessage sentMessage = null;
                try
                {
                    // Send message
                    sentMessage = await context.Guild.GetTextChannel(channelToPostTo).SendMessageAsync(message.ToString());
                }
                finally
                {
                    // Insert into database
                    sqlite_cmd = sqlite.CreateCommand();
                    sqlite_cmd.CommandText = CROSSPOST_INSERT_0 + context.Message.Id + CROSSPOST_INSERT_1 + sentMessage.Id + CROSSPOST_INSERT_2;
                    sqlite_cmd.ExecuteNonQuery();
                }
            }
        }

        private bool IsTwitterUrl(string url)
        {
            return url.StartsWith(TWITTER_URL);
        }

        private string FormatTwitterUrl(string twitterUrl, bool isCrosspost)
        {
            return (isCrosspost ? FXTWITTER_URL : DFXTWITTER_URL) + RemoveTwitterTrackingInfo(twitterUrl).Substring(TWITTER_URL.Length);
        }

        private string RemoveTwitterTrackingInfo(string url)
        {
            int contextIndex = url.IndexOf(TWITTER_TRACKING_INFO_SYMBOL);
            return contextIndex == -1 ? url : url.Substring(0, contextIndex);
        }

        /// <summary>
        /// Parse a string representation of ulong. Returns 0 if failed.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private ulong ParseStringToUlong(string s)
        {
            ulong channelID = 0;

            try
            {
                channelID = Convert.ToUInt64(s);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to parse" + s);
            }

            return channelID;
        }

        /// <summary>
        /// Fills dictionary with crossposting channels
        /// </summary>
        private Dictionary<ulong, ulong> CreateCrosspostChannelDictionary(IEnumerable<IConfigurationSection> crosspostChannelEnumeration)
        {
            Dictionary<ulong, ulong> tempCrosspostChannelDictionary = new Dictionary<ulong, ulong>();
            foreach (var channel in crosspostChannelEnumeration)
            {
                // Parse key string to ulong 
                ulong channelFrom = ParseStringToUlong(channel.Key);
                ulong channelTo = ParseStringToUlong(channel.Value);
                tempCrosspostChannelDictionary.Add(channelFrom, channelTo);
            }
            return tempCrosspostChannelDictionary;
        }
    }
}
