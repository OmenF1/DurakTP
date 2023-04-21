using Microsoft.AspNetCore.SignalR;
using Durak.Models;
using System.Diagnostics;
using System.Security.AccessControl;
using Microsoft.AspNetCore.Connections.Features;
using System.Text.RegularExpressions;

namespace Durak.Hubs
{
    public class DurakHub : Hub
    {
        private const string receiveMessage = "ReceiveMessage";
        private const string receiveCards = "ReceiveCards";
        private const string receiveGameState = "ReceiveGameState";
        private const string startGame = "StartGame";
        private const string receiveGamePlayState = "ReceiveGamePlayState";
        private const string enableAttackMode = "EnableAttackMode";
        private const string enableDefenseMode = "EnableDefenseMode";
        private const string enableBombMode = "EnableBombMode"; // We need to have a bomb mode because attacking and bombing are have different requirements.
        private const string serverSend = "Server";
        private const string endGame = "EndGame";
        private const string receivePlayerSeating = "ReceivePlayerSeating";
        private const string notifyAttacking = "NotifyAttacking";
        private const string notifyDefending = "NotifyDefending";

        private static List<Game> games = new List<Game>();

        //  TODO:
        //  Add checking to ensure that two people cannot enter with the same player name.

        //  There's going to be some weird things around this for now,
        //  I'm going to switch to using identities with SignalR, but since
        //  this is a learning project for me with signalR, I've not yet figured it out and 
        //  want to get these core functions working before I tackle that.
        public async Task<string> JoinGroup(string groupId,string? gType = null, string? dType = null)
        {
            if (!games.Exists(i => i.id == groupId))
            {
                if (gType == null || dType == null)
                    return "";
                games.Add(new Game(groupId, Enum.Parse<GameType>(gType), Enum.Parse<DeckType>(dType), Context.GetHttpContext().RequestServices.GetRequiredService<IHubContext<DurakHub>>()));
            }
            bool notify = games.FirstOrDefault(i => i.id == groupId).AddPlayer(Context.ConnectionId, Context.User.Identity.Name, Context.UserIdentifier);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
            if (notify)
                await Clients.Group(groupId).SendAsync(receiveMessage, serverSend, $"{Context.User.Identity.Name} has joined the party");

            //  Handle Page Refreshes.
            if (!notify && games.FirstOrDefault(i => i.id == groupId).state == GameState.InProgress && games.FirstOrDefault(i => i.id == groupId).gamePlayState.playerOrder.Contains(Context.User.Identity.Name))
            {
                var player = games.FirstOrDefault(i => i.id == groupId)._players.FirstOrDefault(i => i.Name == Context.User.Identity.Name);
                await HandlePageRefresh(player, groupId);
            }
            return "Ok";
        }
        public async Task<bool> GroupExists(string groupId)
        {
            if (games.Exists(i => i.id == groupId))
                return true;
            return false;
        }

        public async Task LeaveGroup(string groupId)
        {
            if (games.Exists(i => i.id == groupId))
            {
                var connections = games.FirstOrDefault(i => i.id == groupId).RemovePlayer(Context.User.Identity.Name);
                if (games.FirstOrDefault(i => i.id == groupId).GetPlayerCount() == 0)
                {
                    games.Remove(games.FirstOrDefault(i => i.id == groupId));
                }
                else
                {
                    if (connections != null)
                    {
                        foreach(string id in connections)
                        {
                            await Groups.RemoveFromGroupAsync(id, groupId);
                            await Clients.Group(groupId).SendAsync(receiveMessage, serverSend, $"User {Context.User.Identity.Name} has left the group");
                        }
                    }
                }
                    
            }
                    
            
        }
        public async Task GetGamesList()
        {
            var lobbies = games.Select(l => new { l.id, l.gameType, l.deckType, l._players.Count }).ToList();
            await Clients.Client(Context.ConnectionId).SendAsync("ReceiveLobbyList", System.Text.Json.JsonSerializer.Serialize(lobbies));
        }
        public async Task SendMessage(string message, string groupId)
        {
            //await Clients.Group(groupId).SendAsync("ReceiveMessage", user, message);
            await Clients.Group(groupId).SendAsync("ReceiveMessage", Context.User.Identity.Name, message);
        }

        public async Task TimerEnd(string groupId)
        {
            NotifyGameStateChanged(groupId);
        }

        public async Task StartGame(string groupId)
        {
            if (!games.Exists(i => i.id == groupId))
                return;

            if (games.FirstOrDefault(i => i.id == groupId).GetPlayerCount() < Game.minPlayers)
                return;

            games.FirstOrDefault(i => i.id == groupId).StartGame();

            //  Notify connected client browsers to start preparing the table
            await Clients.Group(groupId).SendAsync(startGame);
            await RefreshPlayerHands(groupId);
            await Clients.Group(groupId).SendAsync(receivePlayerSeating, System.Text.Json.JsonSerializer.Serialize(games.FirstOrDefault(i => i.id == groupId).gamePlayState.tableOrder));
            await NotifyGameStateChanged(groupId);
        }

        public async Task NotifyGameStateChanged(string groupId)
        {
            var game = games.FirstOrDefault(i => i.id == groupId);
            if (game.gamePlayState.checkDurak == true)
            {
                if (game.playerHands.Where(i => i.Value.Count > 0).Count() < 2)
                {
                    var durak = game.playerHands.Where(i => i.Value.Count > 0).FirstOrDefault().Key;
                    await Clients.Group(groupId).SendAsync(receiveMessage, serverSend, $"{durak} Is The Durak!");
                    games.FirstOrDefault(i => i.id == groupId).state = GameState.Pending;
                    await Clients.Group(groupId).SendAsync(endGame);
                }
                else
                {
                    games.FirstOrDefault(i => i.id == groupId).gamePlayState.checkDurak = false;
                }
            }


            if (game.refreshPlayerCards)
                RefreshPlayerHands(groupId);
            if (game.gameType == GameType.TPEdition)
                await Clients.Group(groupId).SendAsync(enableBombMode);
            await Clients.Group(groupId).SendAsync(receiveGamePlayState, System.Text.Json.JsonSerializer.Serialize(game.gamePlayState));
            var defender = game._players.FirstOrDefault(i => i.Name == game.gamePlayState.defenderId);
            var attacker = game._players.FirstOrDefault(i => i.Name == game.gamePlayState.attackerId);
            await SendToSinglePlayer(defender, enableDefenseMode);
            await SendToSinglePlayer(attacker, enableAttackMode);

            if (game.notifyNewTurn)
            {
                await SendToSinglePlayer(defender, notifyDefending);
                await SendToSinglePlayer(attacker, notifyAttacking);
                game.notifyNewTurn = false;
            }
        }

        private async Task RefreshPlayerHands(string groupId)
        {
            //  This who section of creating the list of cards and sending it in this way is only temp
            //  I seem to be having a javascript issue with the received JSON result, so I'm using this for the time being.
            foreach (KeyValuePair<string, List<Card>> player in games.FirstOrDefault(i => i.id == groupId).playerHands)
            {
                var _player = games.FirstOrDefault(i => i.id == groupId)._players.FirstOrDefault(p => p.Name == player.Key);
                await SendToSinglePlayer(_player, receiveCards, System.Text.Json.JsonSerializer.Serialize(games.FirstOrDefault(i => i.id == groupId).playerHands[player.Key].OrderByDescending(x => x.value).Select(x => x.friendlyName).ToList()));
            }
            games.FirstOrDefault(i => i.id == groupId).refreshPlayerCards = false;
        }

        //  I think I should do some more checking here based on game state to see if it's a valid card play before
        //  calling the objects cardPlayed method, but for now we'll leave it like this.
        public async Task CardPlayed(string cardFriendlyName, string groupId, string cardDefendingName)
        {
            if (cardFriendlyName == null || groupId == null)
                return;

            if (!games.Exists(i => i.id == groupId))
                return;
            var game = games.FirstOrDefault(i => i.id == groupId);

            if (game.CardPlayed(Context.User.Identity.Name, cardFriendlyName, cardDefendingName))
            {
                var _player = game._players.FirstOrDefault(p => p.Name == Context.User.Identity.Name);
                await SendToSinglePlayer(_player, "RemoveCardFromHand", cardFriendlyName);
                if (game.allowPickUpPass)
                    await Clients.Group(groupId).SendAsync("StartTimer", game.timerDuration);
                await NotifyGameStateChanged(groupId);
            }
            
        }

        public async Task PickUp(string groupId)
        {
            if (!games.Exists(i => i.id == groupId))
                return;

            var game = games.FirstOrDefault(i => i.id == groupId);

            if (game.gamePlayState.defenderId != Context.User.Identity.Name)
                return;

            if (game.gamePlayState.cardsInPlay.Count() == 0)
                return;

            game.PickUp();
           game.gamePlayState.checkDurak = true;

            await NotifyGameStateChanged(groupId);
        }

        public async Task FinishAttack(string groupId)
        {
            if (!games.Exists(i => i.id == groupId))
                return;

            var game = games.FirstOrDefault(i => i.id == groupId);

            if (game.gamePlayState.attackerId != Context.User.Identity.Name)
                return;

            if (game.gamePlayState.cardsInPlay.Count() == 0)
                return;

            game.gamePlayState.checkDurak = true;
            if (game.EndAttack())
                await NotifyGameStateChanged(groupId);
        }

        private async Task SendToSinglePlayer(Player player, string command, string? message = null)
        {
            foreach (string id in player.Connections)
            {
                if (message == null)
                {
                    await Clients.Client(id).SendAsync(command);
                }
                else
                {
                    await Clients.Client(id).SendAsync(command, message);
                }
            }
        }

        private async Task HandlePageRefresh(Player player, string groupId)
        {
            await SendToSinglePlayer(player, startGame);
            await SendToSinglePlayer(player, receiveGamePlayState, System.Text.Json.JsonSerializer.Serialize(games.FirstOrDefault(i => i.id == groupId).gamePlayState));
            await SendToSinglePlayer(player, receiveCards, System.Text.Json.JsonSerializer.Serialize(games.FirstOrDefault(i => i.id == groupId).playerHands[player.Name].Select(x => x.friendlyName).ToList()));
            if (games.FirstOrDefault(i => i.id == groupId).gameType == GameType.TPEdition)
                await SendToSinglePlayer(player, enableBombMode);
            
            if (games.FirstOrDefault(i => i.id == groupId).gamePlayState.defenderId == player.Name)
            {
                await SendToSinglePlayer(player, enableDefenseMode);
            }
            else if (games.FirstOrDefault(i => i.id == groupId).gamePlayState.attackerId == player.Name)
            {
                await SendToSinglePlayer(player, enableAttackMode);
            }
            await Clients.Group(groupId).SendAsync(receivePlayerSeating, System.Text.Json.JsonSerializer.Serialize(games.FirstOrDefault(i => i.id == groupId).gamePlayState.tableOrder));

        }

        //  I'll come back to this, I'm just thinking about when a person disconnects
        //  and the clean up work around that.
        //public override async Task OnDisconnectedAsync(Exception? exception)
        //{
        //    Trace.WriteLine(Context.ConnectionId + "- disconnected");
        //    await base.OnDisconnectedAsync(exception);
        //}
    }
}
