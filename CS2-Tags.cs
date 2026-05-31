using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace CS2_Tags;

[MinimumApiVersion(159)]
public class CS2_Tags : BasePlugin
{
	private HashSet<string> GaggedIds = new HashSet<string>();
	public static JObject? JsonTags { get; private set; }
	public override string ModuleName => "CS2-Tags";
	public override string ModuleDescription => "Add player tags easily in cs2 game";
	public override string ModuleAuthor => "daffyy (Optimized)";
	public override string ModuleVersion => "1.0.6";

	// 效能優化：事先將顏色 pattern 緩存起來，避免每次打字都執行高消耗的「反射」
	private static readonly Dictionary<string, string> ColorCache = new(StringComparer.OrdinalIgnoreCase);

	static CS2_Tags()
	{
		foreach (FieldInfo field in typeof(ChatColors).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			var val = field.GetValue(null)?.ToString();
			if (val != null)
			{
				ColorCache[$"{{{field.Name}}}"] = val;
			}
		}
	}

	public override void Load(bool hotReload)
	{
		CreateOrLoadJsonFile(ModuleDirectory + "/tags.json");

		RegisterListener<Listeners.OnMapStart>(OnMapStart);
		RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
		RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
		RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		AddCommandListener("say", OnPlayerChat);
		AddCommandListener("say_team", OnPlayerChatTeam);
	}

	private void OnMapStart(string mapName)
	{
		GaggedIds.Clear();
	}

	private static void CreateOrLoadJsonFile(string filepath)
	{
		if (!File.Exists(filepath))
		{
			var templateData = new JObject
			{
				["tags"] = new JObject
				{
					["#css/admin"] = new JObject
					{
						["prefix"] = "{RED}[ADMIN]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[ADMIN]"
					},
					["@css/chat"] = new JObject
					{
						["prefix"] = "{GREEN}[CHAT]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[CHAT]"
					},
					["76561197961430531"] = new JObject
					{
						["prefix"] = "{RED}[ADMIN]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[ADMIN]"
					},
					["everyone"] = new JObject
					{
						["team_chat"] = false,
						["prefix"] = "{Grey}[Player]",
						["nick_color"] = "",
						["message_color"] = "",
						["scoreboard"] = "[Player]"
					},
				}
			};

			File.WriteAllText(filepath, templateData.ToString());
			var jsonData = File.ReadAllText(filepath);
			JsonTags = JObject.Parse(jsonData);
		}
		else
		{
			var jsonData = File.ReadAllText(filepath);
			JsonTags = JObject.Parse(jsonData);
		}
	}

	[ConsoleCommand("css_tags_reload")]
	public void OnReloadConfig(CCSPlayerController? player, CommandInfo info)
	{
		if (player != null) return;
		CreateOrLoadJsonFile(ModuleDirectory + "/tags.json");

		Server.PrintToConsole("[CS2-Tags] Config reloaded!");
	}

	[ConsoleCommand("css_tag_mute")]
	[CommandHelper(minArgs: 1, usage: "<SteamID>", whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		string? steamid = command.GetArg(1);

		if (steamid != null && steamid.Length == 17)
		{
			if (!GaggedIds.Contains(steamid))
				GaggedIds.Add(steamid);
		}
	}

	[ConsoleCommand("css_tag_unmute")]
	[CommandHelper(minArgs: 1, usage: "<SteamID>", whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagUnMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		string? steamid = command.GetArg(1);

		if (steamid != null && steamid.Length == 17)
		{
			if (GaggedIds.Contains(steamid))
				GaggedIds.Remove(steamid);
		}
	}

	private void OnClientAuthorized(int playerSlot, SteamID steamId)
	{
		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return;

		AddTimer(2.0f, () => SetPlayerClanTag(player));
	}

	private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return HookResult.Continue;

		AddTimer(2.0f, () => SetPlayerClanTag(player));

		return HookResult.Continue;
	}

	private void OnClientDisconnect(int playerSlot)
	{
		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return;

		// 安全修正：防止 SteamID 尚未取得時為 null 導致的異常
		if (player.AuthorizedSteamID != null)
		{
			GaggedIds.Remove(player.AuthorizedSteamID.SteamId64.ToString());
		}
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

		AddTimer(1.5f, () => SetPlayerClanTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

		AddTimer(1.5f, () => SetPlayerClanTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null || !player.IsValid || info.GetArg(1).Length == 0 || player.AuthorizedSteamID == null) return HookResult.Continue;
		string steamid = player.AuthorizedSteamID.SteamId64.ToString();

		// 安全修正：使用安全的方式比對 Gagged 狀態，防止 NullReferenceException
		if (GaggedIds.Contains(steamid)) return HookResult.Handled;

		if (info.GetArg(1).StartsWith("!") || info.GetArg(1).StartsWith("@") || info.GetArg(1).StartsWith("/") || info.GetArg(1).StartsWith(".") || info.GetArg(1) == "rtv") return HookResult.Continue;

		if (JsonTags != null && JsonTags.TryGetValue("tags", out var tags) && tags is JObject tagsObject)
		{
			string deadIcon = !player.PawnIsAlive ? $"{ChatColors.White}☠ {ChatColors.Default}" : "";

			if (tagsObject.TryGetValue(steamid, out var playerTag) && playerTag is JObject)
			{
				string prefix = playerTag["prefix"]?.ToString() ?? "";
				string nickColor = playerTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = playerTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}", player.TeamNum));

				return HookResult.Handled;
			}

			foreach (var tagKey in tagsObject.Properties())
			{
				if (tagKey.Name.StartsWith("#"))
				{
					string group = tagKey.Name;
					bool inGroup = AdminManager.PlayerInGroup(player, group);

					if (inGroup)
					{
						if (tagsObject.TryGetValue(group, out var groupTag) && groupTag is JObject)
						{
							string prefix = groupTag["prefix"]?.ToString() ?? "";
							string nickColor = groupTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
							string messageColor = groupTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

							Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}", player.TeamNum));

							return HookResult.Handled;
						}
					}
				}

				if (tagKey.Name.StartsWith("@"))
				{
					string permission = tagKey.Name;
					bool hasPermission = AdminManager.PlayerHasPermissions(player, permission);

					if (hasPermission)
					{
						if (tagsObject.TryGetValue(permission, out var permissionTag) && permissionTag is JObject)
						{
							string prefix = permissionTag["prefix"]?.ToString() ?? "";
							string nickColor = permissionTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
							string messageColor = permissionTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

							Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}", player.TeamNum));

							return HookResult.Handled;
						}
					}
				}
			}

			// 邏輯修正：移除錯誤的 team_chat 限制，確保普通玩家在全域聊天室也能顯示「everyone」稱號
			if (tagsObject.TryGetValue("everyone", out var everyoneTag) && everyoneTag is JObject)
			{
				string prefix = everyoneTag["prefix"]?.ToString() ?? "";
				string nickColor = everyoneTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = everyoneTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}", player.TeamNum));

				return HookResult.Handled;
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnPlayerChatTeam(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null || !player.IsValid || info.GetArg(1).Length == 0 || player.AuthorizedSteamID == null) return HookResult.Continue;
		string steamid = player.AuthorizedSteamID.SteamId64.ToString();

		//  安全修正：防止 Null 指標異常
		if (GaggedIds.Contains(steamid)) return HookResult.Handled;

		if (info.GetArg(1).StartsWith("@") && AdminManager.PlayerHasPermissions(player, "@css/chat"))
		{
			foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && AdminManager.PlayerHasPermissions(p, "@css/chat")))
			{
				p.PrintToChat($" {ChatColors.Lime}(ADMIN) {ChatColors.Default}{player.PlayerName}：{info.GetArg(1).Remove(0, 1)}");
			}

			return HookResult.Handled;
		}

		if (info.GetArg(1).StartsWith("!") || info.GetArg(1).StartsWith("@") || info.GetArg(1).StartsWith("/") || info.GetArg(1).StartsWith(".") || info.GetArg(1) == "rtv") return HookResult.Continue;

		if (JsonTags != null && JsonTags.TryGetValue("tags", out var tags) && tags is JObject tagsObject)
		{
			string deadIcon = !player.PawnIsAlive ? $"{ChatColors.White}☠ {ChatColors.Default}" : "";
			if (tagsObject.TryGetValue(steamid, out var playerTag) && playerTag is JObject)
			{
				string prefix = playerTag["prefix"]?.ToString() ?? "";
				string nickColor = playerTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = playerTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
				{
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}";
					p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
				}

				return HookResult.Handled;
			}

			foreach (var tagKey in tagsObject.Properties())
			{
				if (tagKey.Name.StartsWith("#"))
				{
					string group = tagKey.Name;
					bool inGroup = AdminManager.PlayerInGroup(player, group);

					if (inGroup && tagsObject.TryGetValue(group, out var groupTag) && groupTag is JObject)
					{
						string prefix = groupTag["prefix"]?.ToString() ?? "";
						string nickColor = groupTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
						string messageColor = groupTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

						foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
						{
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}";
							p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
						}

						return HookResult.Handled;
					}
				}

				if (tagKey.Name.StartsWith("@"))
				{
					string permission = tagKey.Name;
					bool hasPermission = AdminManager.PlayerHasPermissions(player, permission);

					if (hasPermission && tagsObject.TryGetValue(permission, out var permissionTag) && permissionTag is JObject)
					{
						string prefix = permissionTag["prefix"]?.ToString() ?? "";
						string nickColor = permissionTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
						string messageColor = permissionTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

						foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
						{
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}";
							p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
						}

						return HookResult.Handled;
					}
				}
			}

			if (tagsObject.TryGetValue("everyone", out var everyoneTag) && everyoneTag is JObject)
			{
				string prefix = everyoneTag["prefix"]?.ToString() ?? "";
				string nickColor = everyoneTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = everyoneTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
				{
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}：{messageColor}{info.GetArg(1)}";
					p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
				}

				return HookResult.Handled;
			}
		}
		return HookResult.Continue;
	}

	private void SetPlayerClanTag(CCSPlayerController? player)
	{
		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV || player.AuthorizedSteamID == null) return;

		string steamid = player.AuthorizedSteamID.SteamId64.ToString();

		if (JsonTags != null && JsonTags.TryGetValue("tags", out var tags) && tags is JObject tagsObject)
		{
			if (tagsObject.TryGetValue(steamid, out var playerTag) && playerTag is JObject)
			{
				var scoreboardValue = playerTag["scoreboard"]?.ToString();
				if (!string.IsNullOrEmpty(scoreboardValue))
				{
					player.Clan = scoreboardValue + "\u2004";
					return;
				}
			}

			foreach (var tagKey in tagsObject.Properties())
			{
				if (tagKey.Name.StartsWith("#"))
				{
					string group = tagKey.Name;
					bool inGroup = AdminManager.PlayerInGroup(player, group);

					if (inGroup)
					{
						if (tagsObject.TryGetValue(group, out var groupTag) && groupTag is JObject)
						{
							var scoreboardValue = groupTag["scoreboard"]?.ToString();
							if (!string.IsNullOrEmpty(scoreboardValue))
							{
								player.Clan = scoreboardValue + "\u2004";
								return;
							}
						}
					}
				}

				if (tagKey.Name.StartsWith("@"))
				{
					string permission = tagKey.Name;
					bool hasPermission = AdminManager.PlayerHasPermissions(player, permission);

					if (hasPermission)
					{
						if (tagsObject.TryGetValue(permission, out var permissionTag) && permissionTag is JObject)
						{
							var scoreboardValue = permissionTag["scoreboard"]?.ToString();
							if (!string.IsNullOrEmpty(scoreboardValue))
							{
								player.Clan = scoreboardValue + "\u2004";
								return;
							}
						}
					}
				}
			}

			if (tagsObject.TryGetValue("everyone", out var everyone) && everyone is JObject everyoneObject)
			{
				var scoreboardValue = everyoneObject["scoreboard"]?.ToString();
				if (!string.IsNullOrEmpty(scoreboardValue))
				{
					player.Clan = scoreboardValue + "\u2004";
				}
			}
		}
	}

	private string TeamName(int teamNum)
	{
		return teamNum switch
		{
			0 => "(NONE)",
			1 => "(SPEC)",
			2 => $"{ChatColors.Yellow}(T)",
			3 => $"{ChatColors.Blue}(CT)",
			_ => ""
		};
	}

	private string TeamColor(int teamNum)
	{
		return teamNum switch
		{
			2 => $"{ChatColors.Gold}",
			3 => $"{ChatColors.Blue}",
			_ => ""
		};
	}

	private string ReplaceTags(string message, int teamNum = 0)
	{
		if (!message.Contains('{')) return message;

		string modifiedValue = message;

		//效能優化：使用快取的 ColorCache 取代高能耗的 Reflection 反射，打字更順暢
		foreach (var (pattern, colorCode) in ColorCache)
		{
			if (modifiedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase))
			{
				modifiedValue = modifiedValue.Replace(pattern, colorCode, StringComparison.OrdinalIgnoreCase);
			}
		}

		return modifiedValue.Replace("{TEAMCOLOR}", TeamColor(teamNum));
	}
}
