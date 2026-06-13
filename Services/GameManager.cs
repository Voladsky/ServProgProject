using Microsoft.AspNetCore.SignalR;
using ServProgProject.Hubs;
using ServProgProject.Models;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

namespace ServProgProject.Services
{
    // Services/GameManager.cs - updated to match assignment rules and GameHub usage
    public class GameManager
    {
        private readonly ConcurrentDictionary<Guid, Game> _games = new();
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
        private readonly IHubContext<GameHub> _hubContext;
        private readonly TimeSpan _reconnectTimeout = TimeSpan.FromSeconds(30);

        public GameManager(IHubContext<GameHub> hubContext) => _hubContext = hubContext;

        private SemaphoreSlim GetLock(Guid gameId) => _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));

        public async Task<(Guid gameId, string playerToken)> CreateGame()
        {
            var game = new Game();
            game.Player1Id = Guid.NewGuid().ToString();
            game.Status = GameStatus.WaitingForPlayers; // explicit
            game.Player1Removed = false; game.Player2Removed = false;
            _games[game.Id] = game;
            return (game.Id, game.Player1Id);
        }

        public async Task<string> JoinGame(Guid gameId)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException("Game not found");
            if (game.Status != GameStatus.WaitingForPlayers) throw new InvalidOperationException("Game not joinable");
            game.Player2Id = Guid.NewGuid().ToString();
            game.Status = GameStatus.InProgress;
            game.CurrentTurn = game.Player1Id; // creator starts
            // Generate random starting placement now that both players are present
            game.Board = GenerateRandomBoard();
            return game.Player2Id;
        }

        public Game GetGame(Guid gameId)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException("Game not found");
            return game;
        }

        private Board GenerateRandomBoard()
        {
            var board = new Board();
            var whiteCells = Board.AllWhiteCells().ToList();
            // Shuffle the entire list of 36 cells
            var rng = new Random();
            for (int i = whiteCells.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (whiteCells[i], whiteCells[j]) = (whiteCells[j], whiteCells[i]);
            }

            // First 18 shuffled cells → Player1, next 18 → Player2
            for (int i = 0; i < 18; i++)
                board.Place(whiteCells[i].r, whiteCells[i].c, true);
            for (int i = 18; i < 36; i++)
                board.Place(whiteCells[i].r, whiteCells[i].c, false);

            return board;
        }

        // Services/GameManager.cs (только изменённые методы)

        public async Task RemoveFrog(Guid gameId, string playerToken, int row, int col)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException("Game not found");
            await GetLock(gameId).WaitAsync();
            try
            {
                if (game.CurrentTurn != playerToken) throw new InvalidOperationException("Not your turn");
                bool isPlayer1 = playerToken == game.Player1Id;
                bool alreadyRemoved = isPlayer1 ? game.Player1Removed : game.Player2Removed;
                if (alreadyRemoved) throw new InvalidOperationException("Already removed a frog");

                // Проверка: удаление возможно только на самом первом ходу (до прыжков/паса)
                bool firstTurnDone = isPlayer1 ? game.Player1FirstTurnDone : game.Player2FirstTurnDone;
                if (firstTurnDone) throw new InvalidOperationException("You can only remove a frog on your very first turn");

                if (!game.Board.IsEmpty(row, col))
                {
                    game.Board.Remove(row, col);
                }

                if (isPlayer1) game.Player1Removed = true;
                else game.Player2Removed = true;

                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("FrogRemoved", row, col, playerToken);
            }
            finally { GetLock(gameId).Release(); }
        }

        public async Task MakeMove(Guid gameId, string playerToken, int startRow, int startCol, List<(int r, int c)> destinations)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException("Game not found");
            var sem = GetLock(gameId);
            if (!await sem.WaitAsync(0)) return;
            try
            {
                if (game.CurrentTurn != playerToken)
                    return;

                bool isPlayer1 = playerToken == game.Player1Id;

                // 1. Starting cell must contain the player's frog
                if (!game.Board.IsPlayerFrog(startRow, startCol, isPlayer1))
                    throw new InvalidOperationException("Not your frog at start");

                // 2. Work on a clone to validate the whole chain
                var board = game.Board.Clone();
                int curR = startRow, curC = startCol;

                for (int i = 0; i < destinations.Count; i++)
                {
                    var (toR, toC) = destinations[i];
                    bool isLastJump = (i == destinations.Count - 1);

                    // Basic geometry and occupancy check
                    if (!MoveValidator.IsLegalJump(board, curR, curC, toR, toC))
                        throw new InvalidOperationException("Illegal jump");

                    // Apply the jump on the clone
                    int midR = curR + (toR - curR) / 2;
                    int midC = curC + (toC - curC) / 2;
                    board.Remove(midR, midC);
                    board.Remove(curR, curC);
                    board.Place(toR, toC, isPlayer1);

                    // If landing on a swamp square, it must NOT be the last jump
                    if (BoardConstants.IsSwamp(toR, toC) && isLastJump)
                        throw new InvalidOperationException("Cannot end a turn on a swamp square");

                    curR = toR;
                    curC = toC;
                }

                // Final landing must be a white square (already enforced by the loop, but double-check)
                if (BoardConstants.IsSwamp(curR, curC))
                    throw new InvalidOperationException("Move ended on swamp – illegal");

                // If we reach here, the whole chain is valid. Apply the changes to the real board.
                game.Board = board;

                // Mark first turn as done
                if (isPlayer1) game.Player1FirstTurnDone = true;
                else game.Player2FirstTurnDone = true;

                game.LastJumper = playerToken;
                game.ConsecutivePasses = 0;
                game.Version++;
                game.CurrentTurn = isPlayer1 ? game.Player2Id : game.Player1Id;

                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("MoveMade",
                    new { board = game.Board, madeBy = playerToken });
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TurnChanged", game.CurrentTurn);

                // Optional: check game over after move
                if (!MoveValidator.HasAnyLegalJump(game.Board, true) && !MoveValidator.HasAnyLegalJump(game.Board, false))
                {
                    game.Status = GameStatus.Finished;
                    string winner = game.LastJumper ?? playerToken;
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameOver", winner, "No moves left");
                }
            }
            finally { GetLock(gameId).Release(); }
        }

        public async Task PassTurn(Guid gameId, string playerToken)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException("Game not found");
            await GetLock(gameId).WaitAsync();
            try
            {
                if (game.CurrentTurn != playerToken) throw new InvalidOperationException("Not your turn");
                bool isPlayer1 = playerToken == game.Player1Id;

                // Убираем проверку удаления

                if (MoveValidator.HasAnyLegalJump(game.Board, isPlayer1))
                    throw new InvalidOperationException("You have legal jumps; cannot pass");

                // Первый ход совершён (пас)
                if (isPlayer1) game.Player1FirstTurnDone = true;
                else game.Player2FirstTurnDone = true;

                game.ConsecutivePasses++;
                if (game.ConsecutivePasses >= 2)
                {
                    game.Status = GameStatus.Finished;
                    string winner = game.LastJumper ?? playerToken;
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameOver", winner, "No moves possible");
                    return;
                }

                game.CurrentTurn = isPlayer1 ? game.Player2Id : game.Player1Id;
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TurnChanged", game.CurrentTurn);
            }
            finally { GetLock(gameId).Release(); }
        }

        // Disconnection handling
        public async Task PlayerDisconnected(string connectionId)
        {
            var game = _games.Values.FirstOrDefault(g =>
                g.Player1Connection == connectionId || g.Player2Connection == connectionId);
            if (game == null) return;
            string playerId = game.Player1Connection == connectionId ? game.Player1Id : game.Player2Id;
            game.DisconnectCts = new CancellationTokenSource();
            if (playerId == game.Player1Id) game.Player1Connection = null;
            else game.Player2Connection = null;

            await _hubContext.Clients.Group(game.Id.ToString()).SendAsync("PlayerDisconnected", playerId);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_reconnectTimeout, game.DisconnectCts.Token);
                    // Timeout → victory for opponent
                    await GetLock(game.Id).WaitAsync();
                    try
                    {
                        if (game.Status != GameStatus.Finished)
                        {
                            string winner = game.Player1Connection != null ? game.Player1Id : game.Player2Id;
                            game.Status = GameStatus.Finished;
                            await _hubContext.Clients.Group(game.Id.ToString()).SendAsync("GameOver", winner, "Opponent disconnected");
                        }
                    }
                    finally { GetLock(game.Id).Release(); }
                }
                catch (OperationCanceledException) { } // reconnected
            });
        }

        public async Task<bool> TryReconnect(Guid gameId, string playerToken, string connectionId)
        {
            if (!_games.TryGetValue(gameId, out var game)) return false;
            if (game.Player1Id != playerToken && game.Player2Id != playerToken) return false;

            await GetLock(gameId).WaitAsync();
            try
            {
                if (playerToken == game.Player1Id)
                    game.Player1Connection = connectionId;
                else
                    game.Player2Connection = connectionId;
                game.DisconnectCts?.Cancel();
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("PlayerReconnected", playerToken);
                return true;
            }
            finally { GetLock(gameId).Release(); }
        }
    }
}
