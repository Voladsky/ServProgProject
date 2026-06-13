using Microsoft.AspNetCore.SignalR;
using ServProgProject.Services;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace ServProgProject.Hubs
{
    // Hubs/GameHub.cs
    public class GameHub : Hub
    {
        private readonly GameManager _manager;
        // Map connection -> player token to avoid sending token on each call
        private static readonly ConcurrentDictionary<string, string> _connToToken = new();

        public GameHub(GameManager manager) => _manager = manager;

        public async Task CreateGame()
        {
            var (gameId, playerToken) = await _manager.CreateGame();
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
            // store connection
            var game = _manager.GetGame(gameId);
            game.Player1Connection = Context.ConnectionId;
            _connToToken[Context.ConnectionId] = playerToken;
            await Clients.Caller.SendAsync("GameCreated", gameId, playerToken);
        }

        public async Task JoinGame(string gameIdStr)
        {
            if (!Guid.TryParse(gameIdStr, out var gameId))
            {
                await Clients.Caller.SendAsync("JoinFailed", "Invalid game id");
                return;
            }
            string playerToken;
            try
            {
                playerToken = await _manager.JoinGame(gameId);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("JoinFailed", ex.Message);
                return;
            }
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
            var game = _manager.GetGame(gameId);
            game.Player2Connection = Context.ConnectionId;
            _connToToken[Context.ConnectionId] = playerToken;
            // Send initial full board state to both players now that board is generated in JoinGame
            await Clients.Group(gameId.ToString()).SendAsync("GameStarted", new { board = game.Board, currentTurn = game.CurrentTurn, player1 = game.Player1Id, player2 = game.Player2Id, player1Removed = game.Player1Removed, player2Removed = game.Player2Removed });
            await Clients.Caller.SendAsync("JoinedGame", playerToken);
        }

        // Reconnect via OnConnectedAsync query parameters
        public override async Task OnConnectedAsync()
        {
            var httpCtx = Context.GetHttpContext();
            string gameIdStr = httpCtx.Request.Query["gameId"];
            string playerToken = httpCtx.Request.Query["playerToken"];
            if (!string.IsNullOrEmpty(gameIdStr) && Guid.TryParse(gameIdStr, out var gameId) && !string.IsNullOrEmpty(playerToken))
            {
                bool reconnected = await _manager.TryReconnect(gameId, playerToken, Context.ConnectionId);
                if (reconnected)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
                    _connToToken[Context.ConnectionId] = playerToken;
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connToToken.TryRemove(Context.ConnectionId, out _);
            await _manager.PlayerDisconnected(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task RemoveFrog(Guid gameId, int row, int col)
        {
            if (!_connToToken.TryGetValue(Context.ConnectionId, out var token)) throw new InvalidOperationException("Not authenticated in this game");
            await _manager.RemoveFrog(gameId, token, row, col);
        }

        public async Task MakeMove(Guid gameId, int startRow, int startCol, List<(int r, int c)> destinations)
        {
            if (!_connToToken.TryGetValue(Context.ConnectionId, out var token)) throw new InvalidOperationException("Not authenticated in this game");
            await _manager.MakeMove(gameId, token, startRow, startCol, destinations);
        }

        public async Task PassTurn(Guid gameId)
        {
            if (!_connToToken.TryGetValue(Context.ConnectionId, out var token)) throw new InvalidOperationException("Not authenticated in this game");
            await _manager.PassTurn(gameId, token);
        }
    }
}
