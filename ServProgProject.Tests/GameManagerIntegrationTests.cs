using Xunit;
using ServProgProject.Models;
using ServProgProject.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ServProgProject.Tests
{
    public class GameManagerIntegrationTests
    {
        private Mock<IHubContext<Hubs.GameHub>> CreateMockHubContext()
        {
            var mockHubContext = new Mock<IHubContext<Hubs.GameHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();

            mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);
            mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

            return mockHubContext;
        }

        [Fact]
        public async Task CreateGame_ReturnsValidGameIdAndToken()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);

            // Act
            var (gameId, token) = await manager.CreateGame();

            // Assert
            Assert.NotEqual(Guid.Empty, gameId);
            Assert.NotNull(token);
            Assert.NotEmpty(token);
        }

        [Fact]
        public async Task JoinGame_SucceedsWithValidGameId()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, _) = await manager.CreateGame();

            // Act
            var token2 = await manager.JoinGame(gameId);

            // Assert
            Assert.NotNull(token2);
            Assert.NotEmpty(token2);
        }

        [Fact]
        public async Task JoinGame_ThrowsOnInvalidGameId()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var invalidId = Guid.NewGuid();

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.JoinGame(invalidId));
        }

        [Fact]
        public async Task JoinGame_ThrowsIfAlreadyStarted()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, _) = await manager.CreateGame();
            await manager.JoinGame(gameId); // first join succeeds

            // Act & Assert - second join should fail
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.JoinGame(gameId));
        }

        [Fact]
        public async Task RemoveFrog_AllowedOnceBeforeFirstJump()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            // Manually set current turn and initial board state for testing
            game.CurrentTurn = player1Token;

            // Act
            await manager.RemoveFrog(gameId, player1Token, 1, 1); // Should succeed

            // Assert
            Assert.True(game.Player1Removed);
            Assert.True(game.Board.IsEmpty(1, 1));
        }

        [Fact]
        public async Task RemoveFrog_ThrowsIfAlreadyRemoved()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;

            // Act - first removal succeeds
            await manager.RemoveFrog(gameId, player1Token, 1, 1);

            // Act & Assert - second removal fails
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveFrog(gameId, player1Token, 2, 2));
        }

        [Fact]
        public async Task RemoveFrog_ThrowsIfNotYourTurn()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token; // player1's turn

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveFrog(gameId, player2Token, 1, 1));
        }

        [Fact]
        public async Task MakeMove_AllowedAfterRemoval()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;

            // Remove a frog first
            game.Board.Place(1, 1, true);
            game.Board.Place(3, 3, false);
            game.Board.Place(5, 5, true);
            await manager.RemoveFrog(gameId, player1Token, 7, 7); // remove at (7,7)

            // Act - make a valid jump
            var destinations = new List<(int r, int c)> { (3, 3) };
            await manager.MakeMove(gameId, player1Token, 1, 1, destinations);

            // Assert
            Assert.Equal(player2Token, game.CurrentTurn); // turn switched
            Assert.True(game.Board.IsEmpty(2, 2)); // jumped frog removed
        }

        [Fact]
        public async Task MakeMove_ThrowsBeforeRemoval()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;

            // Act & Assert - move before removal
            var destinations = new List<(int r, int c)> { (3, 3) };
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.MakeMove(gameId, player1Token, 1, 1, destinations));
        }

        [Fact]
        public async Task PassTurn_AllowedWhenNoJumps()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;
            game.Player1Removed = true; // frog removed on first turn

            // Clear board so no jumps available
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    game.Board.Remove(r, c);

            // Act
            await manager.PassTurn(gameId, player1Token);

            // Assert
            Assert.Equal(player2Token, game.CurrentTurn);
            Assert.Equal(1, game.ConsecutivePasses);
        }

        [Fact]
        public async Task PassTurn_ThrowsIfHasLegalJumps()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;
            game.Player1Removed = true;

            // Setup: player1 has a legal jump available
            game.Board.Place(1, 1, true);
            game.Board.Place(2, 2, false);
            // (no need to fill board, just need one jump available)

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PassTurn(gameId, player1Token));
        }

        [Fact]
        public async Task GameOver_AfterTwoConsecutivePasses()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            game.CurrentTurn = player1Token;
            game.Player1Removed = true;
            game.Player2Removed = true;
            game.LastJumper = player1Token; // player1 made the last jump

            // Clear board
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    game.Board.Remove(r, c);

            // Act - first pass
            await manager.PassTurn(gameId, player1Token);
            Assert.Equal(player2Token, game.CurrentTurn);

            // Second pass
            await manager.PassTurn(gameId, player2Token);

            // Assert
            Assert.Equal(GameStatus.Finished, game.Status);
            Assert.Equal(player1Token, game.LastJumper); // winner
        }

        [Fact]
        public async Task Concurrency_TwoMovesInParallel()
        {
            // Arrange
            var mockHub = CreateMockHubContext();
            var manager = new GameManager(mockHub.Object);
            var (gameId, player1Token) = await manager.CreateGame();
            var player2Token = await manager.JoinGame(gameId);

            var game = manager.GetGame(gameId);
            // Setup board with jumps for both players
            game.Board.Place(1, 1, true);
            game.Board.Place(2, 2, false);
            game.Board.Place(4, 4, false);
            game.Board.Place(5, 5, true);

            game.Player1Removed = true;
            game.Player2Removed = true;
            game.CurrentTurn = player1Token;

            // Act - player1 makes a move
            var move1Task = manager.MakeMove(gameId, player1Token, 1, 1, new List<(int, int)> { (3, 3) });

            // Player2 shouldn't be able to move simultaneously (but try)
            game.CurrentTurn = player2Token; // simulate turn already switched
            var move2Task = manager.MakeMove(gameId, player2Token, 4, 4, new List<(int, int)> { (6, 6) });

            // Assert - both should complete without corruption
            await move1Task;
            await move2Task;

            Assert.Equal(player1Token, game.LastJumper); // player1 moved last in sequence
        }
    }
}
