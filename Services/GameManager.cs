using Microsoft.AspNetCore.SignalR;
using ServProgProject.Hubs;
using ServProgProject.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace ServProgProject.Services
{
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
            game.Status = GameStatus.WaitingForPlayers;
            game.Player1Removed = false;
            game.Player2Removed = false;
            _games[game.Id] = game;
            Console.WriteLine($"[CreateGame] Game {game.Id}, Player1Token: {game.Player1Id}");
            return (game.Id, game.Player1Id);
        }

        public async Task<string> JoinGame(Guid gameId)
        {
            if (!_games.TryGetValue(gameId, out var game))
                throw new KeyNotFoundException("Game not found");
            if (game.Status != GameStatus.WaitingForPlayers)
                throw new InvalidOperationException("Game not joinable");

            game.Player2Id = Guid.NewGuid().ToString();
            game.Status = GameStatus.InProgress;
            game.CurrentTurn = game.Player1Id; // creator starts
            game.Board = GenerateRandomBoard();
            game.Player1FirstTurnDone = false;
            game.Player2FirstTurnDone = false;
            game.Player1Removed = false;
            game.Player2Removed = false;

            Console.WriteLine($"[JoinGame] Game {gameId}, Player2Token: {game.Player2Id}");
            Console.WriteLine($"[JoinGame] CurrentTurn: {game.CurrentTurn}");
            return game.Player2Id;
        }

        public Game GetGame(Guid gameId) => _games[gameId];

        private Board GenerateRandomBoard()
        {
            var board = new Board();
            var whiteCells = Board.AllWhiteCells().ToList();
            var rng = new Random();
            for (int i = whiteCells.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (whiteCells[i], whiteCells[j]) = (whiteCells[j], whiteCells[i]);
            }
            for (int i = 0; i < 18; i++)
                board.Place(whiteCells[i].r, whiteCells[i].c, true);
            for (int i = 18; i < 36; i++)
                board.Place(whiteCells[i].r, whiteCells[i].c, false);
            return board;
        }

        public async Task RemoveFrog(Guid gameId, string playerToken, int row, int col)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException();
            await GetLock(gameId).WaitAsync();
            try
            {
                if (game.CurrentTurn != playerToken) throw new InvalidOperationException("Not your turn");
                bool isPlayer1 = playerToken == game.Player1Id;
                bool alreadyRemoved = isPlayer1 ? game.Player1Removed : game.Player2Removed;
                if (alreadyRemoved) throw new InvalidOperationException("Already removed");
                bool firstTurnDone = isPlayer1 ? game.Player1FirstTurnDone : game.Player2FirstTurnDone;
                if (firstTurnDone) throw new InvalidOperationException("Can only remove on first turn");

                if (!game.Board.IsEmpty(row, col))
                    game.Board.Remove(row, col);

                if (isPlayer1) game.Player1Removed = true;
                else game.Player2Removed = true;

                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("FrogRemoved", row, col, playerToken);
            }
            finally { GetLock(gameId).Release(); }
        }

        public async Task MakeMove(Guid gameId, string playerToken, int startRow, int startCol, List<(int r, int c)> destinations)
        {
            if (!_games.TryGetValue(gameId, out var game))
                throw new KeyNotFoundException("Game not found");

            var sem = GetLock(gameId);
            await sem.WaitAsync();
            try
            {
                Console.WriteLine($"[MakeMove] Game {gameId}, PlayerToken: {playerToken}, CurrentTurn: {game.CurrentTurn}");

                if (game.CurrentTurn != playerToken)
                {
                    Console.WriteLine($"[MakeMove] Not your turn");
                    throw new InvalidOperationException("Not your turn");
                }

                bool isPlayer1 = playerToken == game.Player1Id;
                Console.WriteLine($"[MakeMove] isPlayer1: {isPlayer1}");
                Console.WriteLine($"[MakeMove] Checking cell ({startRow},{startCol}) - board value: {game.Board.Cells[startRow, startCol]}");

                if (!game.Board.IsPlayerFrog(startRow, startCol, isPlayer1))
                {
                    Console.WriteLine($"[MakeMove] Not your frog at ({startRow},{startCol})");
                    throw new InvalidOperationException($"Not your frog at ({startRow},{startCol})");
                }

                var board = game.Board.Clone();
                int curR = startRow, curC = startCol;

                for (int i = 0; i < destinations.Count; i++)
                {
                    var (toR, toC) = destinations[i];
                    bool isLastJump = (i == destinations.Count - 1);

                    if (!MoveValidator.IsLegalJump(board, curR, curC, toR, toC))
                        throw new InvalidOperationException("Illegal jump geometry");

                    int midR = curR + (toR - curR) / 2;
                    int midC = curC + (toC - curC) / 2;
                    board.Remove(midR, midC);
                    board.Remove(curR, curC);
                    board.Place(toR, toC, isPlayer1);

                    if (BoardConstants.IsSwamp(toR, toC) && isLastJump)
                        throw new InvalidOperationException("Cannot end turn on swamp");

                    curR = toR;
                    curC = toC;
                }

                if (BoardConstants.IsSwamp(curR, curC))
                    throw new InvalidOperationException("Move ended on swamp – illegal");

                game.Board = board;
                if (isPlayer1) game.Player1FirstTurnDone = true;
                else game.Player2FirstTurnDone = true;

                game.LastJumper = playerToken;
                game.ConsecutivePasses = 0;
                game.Version++;
                game.CurrentTurn = isPlayer1 ? game.Player2Id : game.Player1Id;

                Console.WriteLine($"[MakeMove] Move successful. New turn: {game.CurrentTurn}");

                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("MoveMade",
                    new { board = game.Board, madeBy = playerToken });
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TurnChanged", game.CurrentTurn);

                if (!MoveValidator.HasAnyLegalJump(game.Board, true) && !MoveValidator.HasAnyLegalJump(game.Board, false))
                {
                    game.Status = GameStatus.Finished;
                    string winner = game.LastJumper ?? playerToken;
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameOver", winner, "No moves left");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakeMove] ERROR: {ex.Message}");
                throw;
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task PassTurn(Guid gameId, string playerToken)
        {
            if (!_games.TryGetValue(gameId, out var game)) throw new KeyNotFoundException();
            await GetLock(gameId).WaitAsync();
            try
            {
                if (game.CurrentTurn != playerToken) throw new InvalidOperationException("Not your turn");
                bool isPlayer1 = playerToken == game.Player1Id;
                if (MoveValidator.HasAnyLegalJump(game.Board, isPlayer1))
                    throw new InvalidOperationException("You have legal jumps; cannot pass");

                if (isPlayer1) game.Player1FirstTurnDone = true;
                else game.Player2FirstTurnDone = true;

                game.ConsecutivePasses++;
                if (game.ConsecutivePasses >= 2)
                {
                    game.Status = GameStatus.Finished;
                    string winner = game.LastJumper ?? playerToken;
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameOver", winner, "Double pass");
                    return;
                }

                game.CurrentTurn = isPlayer1 ? game.Player2Id : game.Player1Id;
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TurnChanged", game.CurrentTurn);
            }
            finally { GetLock(gameId).Release(); }
        }

        public async Task PlayerDisconnected(string connectionId) { /* keep existing */ }
        public async Task<bool> TryReconnect(Guid gameId, string playerToken, string connectionId) { /* keep existing */ return true; }
    }
}