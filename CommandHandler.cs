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
        private IEnumerable<IConfigurationSection> channelEnumeration;
        private IEnumerable<IConfigurationSection> crosspostChannelEnumeration;

        // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
        public CommandHandler(DiscordSocketClient discord, CommandService commands, IConfiguration config)
        {
            this.discord = discord;
            this.commands = commands;
            this.config = config;        

            //Get key/value pairs for lists of channels
            channelEnumeration = config.GetSection("ChannelList").GetChildren();
            crosspostChannelEnumeration = config.GetSection("ChannelsCrossPost").GetChildren();

            //load modules
            commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
       
            //Set delegate to go off for every message
            this.discord.MessageReceived += OnMessageReceivedAsync;
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
            if ( isCommand && (config["EditableBy"].Equals(EVERYONE)) || isModOnlyAndModMsg || isAdmOnlyAndAdmMsg)                      
            {
                var result = await commands.ExecuteAsync(context, argPos, null);     // Execute the command

                if (!result.IsSuccess)                              // If not successful, reply with the error.
                    await context.Channel.SendMessageAsync(result.ToString());
            }
            else //if it is not a command check what channel it is
            {
                //wait to allow any embeds to appear
                Thread.Sleep(5000);                 
                
                //for each crosspost key, check if key matches the context channel ID
                foreach (var channel in crosspostChannelEnumeration)
                {        
                    //parse key string to ulong 
                    ulong channelID = ParseStringToUlong(channel.Key);                    

                    //test to see if key matches context. If it does, get the value. That is the channel to post to.
                    if (context.Channel.Id == channelID)
                    {
                        ulong channelTo = ParseStringToUlong(channel.Value);

                        //dont send message if parse failed
                        if(channelTo != 0)
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

            //refresh message to retrieve generated embeds
            var refreshedMessage = await context.Channel.GetMessageAsync(context.Message.Id);

            //message must contain a link or file or else it will not be copied
            if (refreshedMessage.Attachments.Count > 0 || refreshedMessage.Embeds.Count > 0)
            {
                StringBuilder message = new StringBuilder();

                //display files first then link
                foreach (var attachment in refreshedMessage.Attachments)
                {
                    message.Append(attachment.Url + "\n");
                }
                foreach (var embed in refreshedMessage.Embeds)
                {
                    //makes sure it is not geting the same url from last time
                    if (!lastString.Equals(embed.Url))
                        message.Append(embed.Url + "\n");
                    lastString = embed.Url;
                }

                //send message
                await context.Guild.GetTextChannel(channelTo).SendMessageAsync(message.ToString());
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
    }    
}
