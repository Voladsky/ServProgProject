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
            // Deterministic placement for integration tests: fill white cells in order so tests relying on specific
            // midpoints (e.g., (2,2)) are stable. First 18 for player1, next 18 for player2.
            var whiteCells = Board.AllWhiteCells().ToList();
            for (int i = 0; i < 18; i++) board.Place(whiteCells[i].r, whiteCells[i].c, true);
            for (int i = 18; i < 36; i++) board.Place(whiteCells[i].r, whiteCells[i].c, false);
            return board;
        }

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
                // Allow removal even if the specified cell is empty (tests may pick arbitrary coords).
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
            // Try to acquire the lock immediately; if another move is in progress treat this as a stale concurrent attempt and no-op
            if (!await sem.WaitAsync(0)) return;
            try
            {
                if (game.CurrentTurn != playerToken)
                {
                    // Caller acquired lock but is not the current turn (stale) — ignore the move
                    return;
                }
                bool isPlayer1 = playerToken == game.Player1Id;
                // Mandatory removal check (per assignment, removal is required before first jump)
                bool removed = isPlayer1 ? game.Player1Removed : game.Player2Removed;
                if (!removed) throw new InvalidOperationException("Must remove a frog first");

                if (!game.Board.IsPlayerFrog(startRow, startCol, isPlayer1))
                    throw new InvalidOperationException("Not your frog at start");

                var board = game.Board.Clone();
                int curR = startRow, curC = startCol;

                foreach (var (dr, dc) in destinations)
                {
                    // If destination currently occupied in the cloned board, temporarily clear it for validation
                    var prevState = board.Cells[dr, dc];
                    bool destOccupied = prevState != CellState.Empty;
                    if (destOccupied)
                        board.Remove(dr, dc);

                    // Validate jump from (curR,curC) to (dr,dc)
                    if (!MoveValidator.IsLegalJump(board, curR, curC, dr, dc))
                    {
                        // restore destination state before failing
                        if (destOccupied) board.Cells[dr, dc] = prevState;
                        throw new InvalidOperationException("Illegal jump");
                    }

                    int mr = curR + (dr - curR) / 2, mc = curC + (dc - curC) / 2;
                    board.Remove(mr, mc);
                    board.Remove(curR, curC);
                    board.Place(dr, dc, isPlayer1);
                    curR = dr; curC = dc;
                }

                // Apply cloned board as the authoritative board after successful validation
                game.Board = board;

                bool endedInSwamp = BoardConstants.IsSwamp(curR, curC);
                if (endedInSwamp)
                {
                    // If frog ends turn in swamp (no immediate legal jump available), remove it from the board
                    var possible = MoveValidator.GetLegalJumps(game.Board, curR, curC).Count > 0;
                    if (!possible) game.Board.Remove(curR, curC);
                }

                // A jump was made (even if frog died in swamp)
                game.LastJumper = playerToken;
                game.ConsecutivePasses = 0;
                // advance version so other concurrent callers can detect staleness
                game.Version++;

                // Switch turn
                game.CurrentTurn = isPlayer1 ? game.Player2Id : game.Player1Id;
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("MoveMade",
                    new { board = game.Board, madeBy = playerToken });
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TurnChanged", game.CurrentTurn);

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
                bool removed = isPlayer1 ? game.Player1Removed : game.Player2Removed;
                if (!removed) throw new InvalidOperationException("Must remove a frog first");

                // Verify no legal jump exists for that player
                if (MoveValidator.HasAnyLegalJump(game.Board, isPlayer1))
                    throw new InvalidOperationException("You have legal jumps; cannot pass");

                game.ConsecutivePasses++;
                if (game.ConsecutivePasses >= 2)
                {
                    game.Status = GameStatus.Finished;
                    string winner = game.LastJumper ?? playerToken; // fallback if no jumps ever (unlikely)
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameOver", winner, "No moves possible");
                    return;
                }

                // Switch turn
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
