﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        private const string MEDIA_HEADER_0 = "Media from ", MEDIA_HEADER_1 = "'s post:";
        private const string ATTEMPTING_TO_EMBED_0 = "Couldn't load embeds from ", ATTEMPTING_TO_EMBED_1 = " post, attempting with FixTweet:";
        private const string HAS_LEFT = " has left.";
        private const string ORIGINAL_MESSAGE_HEADER = "Original Message:";
        private const string USER_LINK_0 = "<@", USER_LINK_1 = ">";
        private const string SPOILER = "SPOILER_", SPOILER_TAG = "||";
        private static readonly char[] WHITE_SPACE_CHARS = { ' ', '\n', '\t' };

        // URL constants
        private const string TWITTER_URL = "https://twitter.com/", FXTWITTER_URL = "https://fxtwitter.com/", HTTP = "http";
        private const string DISCORD_MESSAGE_LINK = "https://discord.com/channels/";
        private const char TWITTER_TRACKING_INFO_SYMBOL = '?', FORWARD_SLASH = '/';
        private const string TENOR_URL = "tenor.com/view/";

        // Wait constants
        private const int CROSSPOST_WAIT_MS = 5000, FXTWITTER_WAIT_MS = 2000;

        // Final variables
        private readonly DiscordSocketClient discord;
        private readonly CommandService commands;
        private readonly SQLManager sqlManager;
        private readonly Dictionary<ulong, ulong> crosspostChannelsDictionary;
        private readonly ulong serverId;
        private readonly ulong modChannelId;
        private readonly ulong editableBy;
        private readonly string commandPrefix;

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
            // Ensure the message is from a user
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

            // Check if the message has a valid command prefix, or is mentioned
            // Check if allowed by everyone, or if admin only and then make sure user is admin
            if (IsCommand(msg, ref argPos) && (IsEverybody() || IsModOnlyAndModMsg(userPerms) || IsAdmOnlyAndAdmMsg(userPerms)))
            {
                ExecuteCommand(context, argPos);
            }
            // Crosspost if the message is within a crosspost channel
            else if (crosspostChannelsDictionary.TryGetValue(context.Channel.Id, out ulong channelToPostTo))
            {
                bool couldHaveEmbed = context.Message.Content.Contains(HTTP);
                // check if link is tenor link -- suggests a reaction image. users 
                // submitting gifs to the crosspost channel upload them directly / to discord, not tenor 
                bool isTenorLink = context.Message.Content.Contains(TENOR_URL);
                if (!isTenorLink && (couldHaveEmbed || context.Message.Attachments.Count > 0))
                {
                    LinkImagesToOtherChannel(context, channelToPostTo, couldHaveEmbed);
                }
            }
            // We only want to include bot FixTweet posts on channels people aren't posting their created art
            else if (context.Message.Content.Contains(TWITTER_URL))
            {
                // Wait to allow any embeds to appear
                SendFxtwitterUrlsIfNeeded(context);
            }
        }

        /// <summary>
        /// On message deleted, check to see if channel is a gallery channel, and check DB to see if ID of message exists.
        /// </summary>
        /// <param name="sourceMessage"></param>
        /// <param name="sourceChannel"></param>
        /// <returns></returns>
        private async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> sourceMessage, Cacheable<IMessageChannel, ulong> sourceChannel)
        {
            ulong readLinkId = sqlManager.CheckMatch(sourceMessage.Id);

            // Link id exists, delete message and remove from DB
            if (readLinkId != NO_ID)
            {
                // If exists, then it is a crosspost, else it is a random fxtwitter post.
                ulong channelToDeleteFrom = GetValueOrDefault(crosspostChannelsDictionary, sourceChannel.Id, sourceChannel.Id);

                // LOOK ARIA THE MESSEGE IS DELETED
                // Hi this is Aria, good job Coffvee!
                // P.S., you misspelled "message"
                try
                {
                    // Try to delete linked message
                    discord.GetGuild(serverId).GetTextChannel(channelToDeleteFrom).DeleteMessageAsync(readLinkId);
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
        /// <param name="guild"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        private async Task OnUserLeftAsync(SocketGuild guild, SocketUser user)
        {
            if (serverId != NO_ID)
            {
                guild.GetTextChannel(modChannelId).SendMessageAsync(text: CreateDiscordUserLink(user) + HAS_LEFT, allowedMentions: AllowedMentions.None);
            }
        }

        private async Task ExecuteCommand(SocketCommandContext context, int argPos)
        {
            // Execute the command
            var result = await commands.ExecuteAsync(context, argPos, null);

            // If not successful, reply with the error.
            if (!result.IsSuccess)
            {
                await context.Channel.SendMessageAsync(result.ToString());
            }
        }

        /// <summary>
        /// Takes in the context and ulong, posting images and embedded links from context channel to ulong channel
        /// </summary>
        /// <param name="context"></param>
        /// <param name="channelToPostTo"></param>
        /// <param name="shouldWait"></param>
        /// <returns>null</returns>
        private async Task LinkImagesToOtherChannel(SocketCommandContext context, ulong channelToPostTo, bool shouldWait)
        {
            // If needed, wait to allow any embeds to appear
            if (shouldWait)
            {
                await Task.Delay(CROSSPOST_WAIT_MS);
            }

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
                StringBuilder message = new StringBuilder().Append(CROSSPOST_HEADER_0).Append(CreateDiscordUserLink(context.User)).AppendLine(CROSSPOST_HEADER_1);

                // Display files first then link
                foreach (var attachment in refreshedMessage.Attachments)
                {
                    String urlToAppend;
                    if (attachment.Url.Contains(SPOILER))
                    {
                        urlToAppend = SPOILER_TAG + attachment.Url + " " + SPOILER_TAG;
                    }
                    else
                    {
                        urlToAppend = attachment.Url;
                    }

                    message.AppendLine(urlToAppend);
                }

                HashSet<string> appendedEmbedUrls = new HashSet<string>();
                foreach (var embed in refreshedMessage.Embeds)
                {
                    string urlToAppend = IsTwitterUrl(embed.Url) ? FormatTwitterUrl(embed.Url) : embed.Url;

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                    {
                        message.AppendLine(urlToAppend);
                    }
                }

                message.AppendLine(ORIGINAL_MESSAGE_HEADER).Append(DISCORD_MESSAGE_LINK).Append(context.Guild.Id).Append(FORWARD_SLASH).Append(context.Channel.Id).Append(FORWARD_SLASH).Append(context.Message.Id).AppendLine();

                IUserMessage sentMessage = null;
                try
                {
                    // Send message
                    sentMessage = await context.Guild.GetTextChannel(channelToPostTo).SendMessageAsync(text: message.ToString(), allowedMentions: AllowedMentions.None);
                }
                finally
                {
                    // Insert into database if the message was successfully sent
                    if (sentMessage != null)
                    {
                        sqlManager.InsertToTable(context.Message.Id, sentMessage.Id);
                    }
                }
            }
        }

        private async Task SendFxtwitterUrlsIfNeeded(SocketCommandContext context)
        {
            await Task.Delay(FXTWITTER_WAIT_MS);
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            // Null probably if message was deleted before bot could send a URL
            if (refreshedMessage == null)
            {
                Console.WriteLine("Refreshed message null when trying to post");
                return;
            }
            else if (refreshedMessage.IsSuppressed)
            {
                Console.WriteLine("Message should not have embeds, moving on");
                return;
            }

            bool shouldPost = false;
            HashSet<string> appendedEmbedUrls = new HashSet<string>();
            StringBuilder message = new StringBuilder();

            // Discord couldn't load embeds, try using FixTweet
            if (refreshedMessage.Embeds.Count == 0)
            {
                message.Append(ATTEMPTING_TO_EMBED_0).Append(CreateDiscordUserLink(context.User)).AppendLine(ATTEMPTING_TO_EMBED_1);

                string[] words = refreshedMessage.Content.Split(WHITE_SPACE_CHARS);
                foreach (var word in words)
                {
                    if (word.StartsWith(TWITTER_URL))
                    {
                        string urlToAppend = FormatTwitterUrl(word);

                        if (appendedEmbedUrls.Add(urlToAppend))
                        {
                            message.AppendLine(urlToAppend);
                            shouldPost = true;
                        }
                    }
                }
            }
            // Discord could load embeds, only use FxTwitter if there is a video within the embeds, keep other non=Twitter video embeds included as well
            else
            {
                message.Append(MEDIA_HEADER_0).Append(CreateDiscordUserLink(context.User)).AppendLine(MEDIA_HEADER_1);

                foreach (var embed in refreshedMessage.Embeds)
                {
                    string urlToAppend;
                    if (IsTwitterUrl(embed.Url))
                    {
                        urlToAppend = FormatTwitterUrl(embed.Url);
                        shouldPost |= embed.Video != null;
                    }
                    else
                    {
                        urlToAppend = embed.Url;
                    }

                    // Prevents duplicate urls from being appended multiple times
                    if (appendedEmbedUrls.Add(urlToAppend))
                    {
                        message.AppendLine(urlToAppend);
                    }
                }
            }

            if (shouldPost)
            {
                IUserMessage sentMessage = null;
                try
                {
                    // Send message
                    sentMessage = await context.Message.ReplyAsync(text: message.ToString(), allowedMentions: AllowedMentions.None);
                    context.Message.ModifyAsync(msg => msg.Flags = MessageFlags.SuppressEmbeds);
                }
                finally
                {
                    // Insert into database if the message was successfully sent
                    if (sentMessage != null)
                    {
                        sqlManager.InsertToTable(context.Message.Id, sentMessage.Id);
                    }
                }
            }
        }

        private bool IsCommand(SocketUserMessage msg, ref int argPos)
        {
            return msg.HasStringPrefix(commandPrefix, ref argPos) || msg.HasMentionPrefix(discord.CurrentUser, ref argPos);
        }

        private bool IsEverybody()
        {
            return editableBy == EVERYONE;
        }

        private bool IsModOnlyAndModMsg(GuildPermissions userPerms)
        {
            return editableBy == MOD_ONLY && (userPerms.ManageChannels || userPerms.Administrator);
        }

        private bool IsAdmOnlyAndAdmMsg(GuildPermissions userPerms)
        {
            return editableBy == ADMIN_ONLY && userPerms.Administrator;
        }

        private bool IsTwitterUrl(string url)
        {
            return url.StartsWith(TWITTER_URL);
        }

        private string FormatTwitterUrl(string twitterUrl)
        {
            return FXTWITTER_URL + RemoveTwitterTrackingInfo(twitterUrl).Substring(TWITTER_URL.Length);
        }

        private string RemoveTwitterTrackingInfo(string url)
        {
            int trackingInfoIndex = url.IndexOf(TWITTER_TRACKING_INFO_SYMBOL);
            return trackingInfoIndex == -1 ? url : url.Substring(0, trackingInfoIndex);
        }

        private string CreateDiscordUserLink(SocketUser user)
        {
            return USER_LINK_0 + user.Id + USER_LINK_1;
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

        public static TValue GetValueOrDefault<TKey, TValue>(IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
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
