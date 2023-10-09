using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;

public class Program
{
    private DiscordSocketClient _client;
    private CommandService _commands;
    private IServiceProvider _services;
    private CommandServiceConfig _commandServiceConfig;
    private IServiceProvider _provider;

    private Dictionary<ulong, QueueData> queues = new Dictionary<ulong, QueueData>();
    private Dictionary<ulong, TeamQueueData> teamQueues = new Dictionary<ulong, TeamQueueData>();

    public RestUserMessage queueMessage;
    public RestUserMessage teamQueueMessage;

    public string[] agents = new string[]
    {
        "Astra", "Breach", "Brimstone", "Chamber", "Cypher", "Jett",
        "Kay/O", "Killjoy", "Neon", "Omen", "Phoenix", "Raze",
        "Reyna", "Sage", "Skye", "Sova", "Viper", "Yoru", "Deadlock"
    };
    public string[] maps = new string[]
    {
        "Bind", "Breeze", "Fracture", "Haven", "Icebox", "Split", "Ascent", "Pearl", "Lotus", "Sunset"
    };

    public static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

    public async Task RunBotAsync()
    {
        // Enable Message Content Intent
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });

        _commandServiceConfig = new CommandServiceConfig
        {
            DefaultRunMode = RunMode.Async,
            LogLevel = LogSeverity.Info
        };

        _commands = new CommandService(_commandServiceConfig);

        _client.ReactionAdded += HandleReactionAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;

        await RegisterCommandsAsync();

        await _client.LoginAsync(TokenType.Bot, "YOUR-TOKEN-HERE");
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    public async Task RegisterCommandsAsync()
    {
        _client.MessageReceived += HandleCommandAsync;

        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _provider);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        Console.WriteLine("Bot is connected.");
        return Task.CompletedTask;
    }

    private async Task HandleCommandAsync(SocketMessage arg)
    {
        var message = arg as SocketUserMessage;
        if (message == null || message.Author.IsBot) return;

        var context = new SocketCommandContext(_client, message);

        // Check if the message starts with a prefix (e.g., '!') and execute it as a command
        int argPos = 0;
        if (!message.HasStringPrefix("!", ref argPos)) return;

        // Extract the command text excluding the prefix
        var commandText = message.Content.Substring(argPos).Trim().ToLower();

        if (commandText.StartsWith("help"))
        {
            await context.Channel.SendMessageAsync("### This is the Valorant Customs Bot. Made by Syed & Sahil.\n### Commands:\n1) ``!queue {time}``\n2) ``!team (Split your teams)``\n3) ``!start (Assign Characters to players)``");
        }
        else if (commandText.StartsWith("queue"))
        {
            var parts = commandText.Split(' ');
            if (parts.Length != 2)
            {
                await context.Channel.SendMessageAsync("Invalid command format. Use `!queue {time}`.");
                return;
            }

            var timeString = parts[1];

            if (!TryParseTime(timeString, out DateTime queueTime))
            {
                await context.Channel.SendMessageAsync("Invalid time format. Use `!queue {time}`.");
                return;
            }

            queueMessage = await context.Channel.SendMessageAsync($"Valorant Customs queue started for {timeString}");
            queues[queueMessage.Id] = new QueueData(queueTime, queueMessage.Id);
            await queueMessage.AddReactionAsync(new Emoji("✅"));
        }
        else if (commandText.StartsWith("team"))
        {
            teamQueueMessage = await context.Channel.SendMessageAsync($"Valorant Team queues started");
            teamQueues[teamQueueMessage.Id] = new TeamQueueData(DateTime.Now, teamQueueMessage.Id);
            await teamQueueMessage.AddReactionAsync(new Emoji("1️⃣"));
            await teamQueueMessage.AddReactionAsync(new Emoji("2️⃣"));
        }
        else if (commandText.StartsWith("start"))
        {
            if (teamQueues.TryGetValue(teamQueueMessage.Id, out var teamQueue))
            {
                if (teamQueue.Queue1.Count < 1 || teamQueue.Queue2.Count < 1)
                {
                    await context.Channel.SendMessageAsync($"Cannot start character randomisation. Not enough players.");
                    return;
                }

                List<Tuple<SocketGuildUser, string>> agentList1 = new List<Tuple<SocketGuildUser, string>>();
                List<Tuple<SocketGuildUser, string>> agentList2 = new List<Tuple<SocketGuildUser, string>>();

                var agentShuffled1 = agents.OrderBy(s => Guid.NewGuid()).ToList();
                for (int i = 0; i < teamQueue.Queue1.Count; i++)
                {
                    agentList1.Add(new Tuple<SocketGuildUser, string>(teamQueue.Queue1[i], agentShuffled1[i]));
                }


                var agentShuffled2 = agents.OrderBy(s => Guid.NewGuid()).ToList();
                for (int i = 0; i < teamQueue.Queue2.Count; i++)
                {
                    agentList2.Add(new Tuple<SocketGuildUser, string>(teamQueue.Queue2[i], agentShuffled2[i]));
                }

                var mapPicked = maps.OrderBy(s => Guid.NewGuid()).First();

                // publish the message
                await context.Channel.SendMessageAsync($"### Team 1:\n{String.Join("\n ", agentList1.Select(u => u.Item1.Mention + " - " + u.Item2))}\n### Team 2:\n{String.Join("\n ", agentList2.Select(u => u.Item1.Mention + " - " + u.Item2))}");
                await context.Channel.SendMessageAsync($"## The map is '{mapPicked}'.");
            }
        }
    }

    private async Task HandleReactionAsync(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (queues.ContainsKey(reaction.MessageId))
        {
            var user = reaction.User.Value as SocketGuildUser;
            if (user == null || user.IsBot) return;

            var queueData = queues[reaction.MessageId];

            if (reaction.Emote.Name == "✅")
            {
                if (!queueData.Queue.Exists(u => user.Username == u.Username))
                {
                    queueData.Queue.Add(user);

                    string updatedMessageContent = $"Valorant Customs queue started for {queueData.QueueTime:htt}.\n{String.Join("\n", queueData.Queue.Select(u => u.Mention))}";
                    await queueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
        }
        else if (teamQueues.ContainsKey(reaction.MessageId))
        {
            var user = reaction.User.Value as SocketGuildUser;
            if (user == null || user.IsBot) return;

            var queueData = teamQueues[reaction.MessageId];

            // No duplicate in team 2 && max 5
            if (reaction.Emote.Name == "1️⃣")
            {
                if (queueData.Queue1.Count == 5) return;
                if (queueData.Queue2.Exists(u => u.Username == user.Username)) return;

                if (!queueData.Queue1.Exists(u => user.Username == u.Username))
                {
                    queueData.Queue1.Add(user);

                    string updatedMessageContent = $"### Team 1: {String.Join(", ", queueData.Queue1.Select(u => u.Mention))}\n### Team 2: {String.Join(", ", queueData.Queue2.Select(u => u.Mention))}";
                    await teamQueueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
            else if (reaction.Emote.Name == "2️⃣")
            {
                if (queueData.Queue2.Count == 5) return;
                if (queueData.Queue1.Exists(u => u.Username == user.Username)) return;

                if (!queueData.Queue2.Exists(u => user.Username == u.Username))
                {
                    queueData.Queue2.Add(user);

                    string updatedMessageContent = $"### Team 1: {String.Join(", ", queueData.Queue1.Select(u => u.Mention))}\n### Team 2: {String.Join(", ", queueData.Queue2.Select(u => u.Mention))}";
                    await teamQueueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
        }
    }

    private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (queues.ContainsKey(reaction.MessageId))
        {
            var user = reaction.User.Value as SocketGuildUser;
            if (user == null || user.IsBot) return;

            var queueData = queues[reaction.MessageId];

            if (reaction.Emote.Name == "✅")
            {
                if (queueData.Queue.Contains(user))
                {
                    queueData.Queue.Remove(user);

                    string updatedMessageContent = $"Valorant Customs queue started for {queueData.QueueTime:htt}.\n{String.Join("\n", queueData.Queue.Select(u => u.Mention))}";
                    await queueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
        }
        else if (teamQueues.ContainsKey(reaction.MessageId))
        {
            var user = reaction.User.Value as SocketGuildUser;
            if (user == null || user.IsBot) return;

            var queueData = teamQueues[reaction.MessageId];

            // No duplicate in team 2 && max 5
            if (reaction.Emote.Name == "1️⃣")
            {
                if (queueData.Queue1.Exists(u => user.Username == u.Username))
                {
                    queueData.Queue1.Remove(user);

                    string updatedMessageContent = $"### Team 1: {String.Join(", ", queueData.Queue1.Select(u => u.Mention))}\n### Team 2: {String.Join(", ", queueData.Queue2.Select(u => u.Mention))}";
                    await teamQueueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
            else if (reaction.Emote.Name == "2️⃣")
            {
                if (queueData.Queue2.Exists(u => user.Username == u.Username))
                {
                    queueData.Queue2.Remove(user);

                    string updatedMessageContent = $"### Team 1: {String.Join(", ", queueData.Queue1.Select(u => u.Mention))}\n### Team 2: {String.Join(", ", queueData.Queue2.Select(u => u.Mention))}";
                    await teamQueueMessage.ModifyAsync(properties => properties.Content = updatedMessageContent);
                }
            }
        }
    }

    private bool TryParseTime(string timeString, out DateTime queueTime)
    {
        if (DateTime.TryParseExact(timeString, "htt", null, System.Globalization.DateTimeStyles.None, out queueTime))
        {
            return true;
        }

        // Add more time parsing logic if needed

        queueTime = default;
        return false;
    }

    private class QueueData
    {
        public DateTime QueueTime { get; }
        public ulong QueueMessageId { get; }
        public List<SocketGuildUser> Queue { get; } = new List<SocketGuildUser>();

        public QueueData(DateTime queueTime, ulong queueMessageId)
        {
            QueueTime = queueTime;
            QueueMessageId = queueMessageId;
        }
    }

    private class TeamQueueData
    {
        public DateTime QueueTime { get; }
        public ulong QueueMessageId { get; }
        public List<SocketGuildUser> Queue1 { get; } = new List<SocketGuildUser>();
        public List<SocketGuildUser> Queue2 { get; } = new List<SocketGuildUser>();

        public TeamQueueData(DateTime queueTime, ulong queueMessageId)
        {
            QueueTime = queueTime;
            QueueMessageId = queueMessageId;
        }
    }
}
