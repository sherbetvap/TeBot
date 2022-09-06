using System.Threading.Tasks;
using Discord.Commands;

namespace TeBot.Modules
{
    public class TestModules : ModuleBase<SocketCommandContext>
    {
        [Command("test")]
        public async Task Task1()
        {
            await Context.Channel.SendMessageAsync("Success");
        }

        [Command("repeat", true)]
        public async Task Task2(string repeat)
        {
            await Context.Guild.GetTextChannel(Context.Message.Channel.Id).SendMessageAsync(repeat);
            //await discord.GetGuild(serverID).GetTextChannel(voicechatID).SendMessageAsync(repeat);
        }
    }
}
