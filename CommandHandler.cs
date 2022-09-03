using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TeBot
{
    public class CommandHandler
    {
        // ID constants
        public const ulong NO_ID = 0;
        private const ulong ADMIN_ONLY = 0, MOD_ONLY = 1, EVERYONE = 2;

        // Message constants
        private const string CROSSPOST_HEADER_0 = "Posted by ", CROSSPOST_HEADER_1 = ":";
        private const string VIDEO_HEADER_0 = "Video from ", VIDEO_HEADER_1 = "'s Twitter link:";
        private const string HAS_LEFT = " has left.";
        private const char DISCORD_DISCRIMINATOR_SYMBOL = '#';

        // URL constants
        private const string TWITTER_URL = "https://twitter.com/", FXTWITTER_URL = "https://fxtwitter.com/", DFXTWITTER_URL = "https://d.fxtwitter.com/";
        private const char TWITTER_TRACKING_INFO_SYMBOL = '?';

        // Wait constants
        private const int CROSSPOST_WAIT_MS = 5000, FXTWITTER_WAIT_MS = 2000;

        // Final variables
        private readonly DiscordSocketClient discord;
        private readonly CommandService commands;
        private readonly Dictionary<ulong, ulong> crosspostChannelsDictionary;
        private readonly ulong serverId;
        private readonly ulong modChannelId;
        private readonly ulong editableBy;
        private readonly string commandPrefix;

        private SQLManager sqlManager;

        public CommandHandler(DiscordSocketClient discord, CommandService commands, IConfiguration config, SQLManager sqlManager)
        {
            this.discord = discord;
            this.commands = commands;
            this.sqlManager = sqlManager;

            // Get key/value pairs for lists of channels
            crosspostChannelsDictionary = CreateCrosspostChannelDictionary(config.GetSection("ChannelsCrossPost").GetChildren());
            serverId = ParseStringToUlong(config["serverID"]);
            modChannelId = GetModChannelId(config.GetSection("ChannelList").GetChildren());
            editableBy = ParseStringToUlong(config["EditableBy"]);
            commandPrefix = config["Prefix"];

            // Load modules
            commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);

            // Set delegate to go off for every message
            this.discord.MessageReceived += OnMessageReceivedAsync;
            // Set delegate to go off every delete
            this.discord.MessageDeleted += OnMessageDeletedAsync;
            // Set delegate to go off when someone leaves server
            this.discord.UserLeft += OnUserLeftAsync;
        }

        /// <summary>
        /// Parse a message and perform any required actions.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            // Ensure the message is from a user/bot
            var msg = s as SocketUserMessage;

            // Ignore self when checking commands
            if (msg == null || msg.Author.Id == discord.CurrentUser.Id)
            {
                return;
            }

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
                {
                    await context.Channel.SendMessageAsync(result.ToString());
                }
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

        /// <summary>
        /// On message deleted, check to see if channel is a gallery channel, and check DB to see if ID of message exists.
        /// </summary>
        /// <param name="sourceMessage"></param>
        /// <param name="sourceChannel"></param>
        /// <returns></returns>
        private async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> sourceMessage, ISocketMessageChannel sourceChannel)
        {
            ulong readLinkId = sqlManager.CheckMatch(sourceMessage.Id);

            // Link id exists, delete message and remove from DB
            if (readLinkId != NO_ID)
            {
                ulong channelToDeleteFrom = crosspostChannelsDictionary[sourceChannel.Id];

                // LOOK ARIA THE MESSEGE IS DELETED
                // Hi this is Aria, good job Coffvee!
                // P.S., you misspelled "message"
                try
                {
                    // Try to delete linked message
                    await discord.GetGuild(serverId).GetTextChannel(channelToDeleteFrom).DeleteMessageAsync(readLinkId);
                }
                catch (Discord.Net.HttpException ex)
                {
                    if (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Console.WriteLine("Message to delete not found, was it deleted already?");
                    }
                    else
                    {
                        throw ex;
                    }
                }
                finally
                {
                    // Clean up db, delete entry for deleted message
                    sqlManager.DeleteFromTable(sourceMessage.Id);
                }
            }
        }

        /// <summary>
        /// Sends a messgae in a channel when a user leaves the server.
        /// </summary>
        /// <param name="User"></param>
        /// <returns></returns>
        private async Task OnUserLeftAsync(SocketGuildUser user)
        {
            if (serverId != NO_ID)
            {
                await user.Guild.GetTextChannel(modChannelId).SendMessageAsync(CreateDiscordUsername(user) + HAS_LEFT);
            }
        }

        /// <summary>
        /// Takes in the context and ulong, posting images and embedded links from context channel to ulong channel
        /// </summary>
        /// <param name="context"></param>
        /// <param name="channelToPostTo"></param>
        /// <returns>null</returns>
        private async Task LinkImagesToOtherChannel(SocketCommandContext context, ulong channelToPostTo)
        {
            // Refresh message to retrieve generated embeds
            IMessage refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            // Null probably if message was deleted before bot could crosspost link
            if (refreshedMessage == null)
            {
                Console.WriteLine("Refreshed message null when trying to crosspost");
                return;
            }

            // Message must contain a link or file or else it will not be copied
            if (refreshedMessage.Attachments.Count > 0 || refreshedMessage.Embeds.Count > 0)
            {
                StringBuilder message = new StringBuilder().Append(CROSSPOST_HEADER_0).Append(CreateDiscordUsername(context.User)).AppendLine(CROSSPOST_HEADER_1);

                // Display files first then link
                foreach (var attachment in refreshedMessage.Attachments)
                {
                    message.AppendLine(attachment.Url);
                }

                HashSet<string> appendedEmbedUrls = new HashSet<string>();
                foreach (var embed in refreshedMessage.Embeds)
                {
                    string urlToAppend = IsTwitterUrl(embed.Url) ? FormatTwitterUrl(embed.Url, true) : embed.Url;

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                    {
                        message.AppendLine(urlToAppend);
                    }
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
                    sqlManager.InsertToTable(context.Message.Id, sentMessage.Id);
                }
            }
        }

        private async Task SendFxtwitterUrlsIfNeeded(SocketCommandContext context)
        {
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            HashSet<string> appendedEmbedUrls = new HashSet<string>();
            bool containsTwitterVideo = false;

            StringBuilder message = new StringBuilder().Append(VIDEO_HEADER_0).Append(CreateDiscordUsername(context.User)).AppendLine(VIDEO_HEADER_1);
            foreach (var embed in refreshedMessage.Embeds)
            {
                bool isTwitterVideo = IsTwitterUrl(embed.Url) && embed.Video != null;
                containsTwitterVideo |= isTwitterVideo;

                if (isTwitterVideo)
                {
                    string urlToAppend = FormatTwitterUrl(embed.Url, false);

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                    {
                        message.AppendLine(urlToAppend);
                    }
                }
            }

            if (containsTwitterVideo)
            {
                // TODO: remove embeds from original message if possible
                await context.Channel.SendMessageAsync(message.ToString());
            }
        }

        private bool isCommand(SocketUserMessage msg, ref int argPos)
        {
            return msg.HasStringPrefix(commandPrefix, ref argPos) || msg.HasMentionPrefix(discord.CurrentUser, ref argPos);
        }

        private bool isEverybody()
        {
            return editableBy == EVERYONE;
        }

        private bool isModOnlyAndModMsg(GuildPermissions userPerms)
        {
            return editableBy == MOD_ONLY && (userPerms.ManageChannels || userPerms.Administrator);
        }

        private bool isAdmOnlyAndAdmMsg(GuildPermissions userPerms)
        {
            return editableBy == ADMIN_ONLY && userPerms.Administrator;
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
            int trackingInfoIndex = url.IndexOf(TWITTER_TRACKING_INFO_SYMBOL);
            return trackingInfoIndex == -1 ? url : url.Substring(0, trackingInfoIndex);
        }

        private string CreateDiscordUsername(SocketUser user)
        {
            return user.Username + DISCORD_DISCRIMINATOR_SYMBOL + user.Discriminator;
        }

        /// <summary>
        /// Parse a string representation of ulong. Returns 0 if failed.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private ulong ParseStringToUlong(string s)
        {
            try
            {
                return Convert.ToUInt64(s);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to parse" + s);
            }

            return NO_ID;
        }

        /// <summary>
        /// Creates a dictionary from crosspost by to crosspost to
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

        /// <summary>
        /// Gets the mod channel ID
        /// </summary>
        private ulong GetModChannelId(IEnumerable<IConfigurationSection> channelEnumeration)
        {
            string modChannelString = channelEnumeration.FirstOrDefault(x => x.Key == "modChannelID")?.Value;
            if (modChannelString != null)
            {
                return ParseStringToUlong(modChannelString);
            }

            return NO_ID;
        }
    }
}
