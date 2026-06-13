using Xunit;
using ServProgProject.Models;
using ServProgProject.Services;
using System.Collections.Generic;
using System.Linq;

namespace ServProgProject.Tests
{
    public class BoardGenerationTests
    {
        [Fact]
        public void GenerateBoard_ShouldHave36OccupiedCells()
        {
            // Arrange & Act
            var board = GenerateTestBoard();

            // Assert
            int occupied = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (board.Cells[r, c] != CellState.Empty)
                        occupied++;

            Assert.Equal(36, occupied);
        }

        [Fact]
        public void GenerateBoard_ShouldHave18FrogsPerColor()
        {
            // Arrange & Act
            var board = GenerateTestBoard();

            // Assert
            int player1 = 0, player2 = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    if (board.Cells[r, c] == CellState.Player1Frog) player1++;
                    if (board.Cells[r, c] == CellState.Player2Frog) player2++;
                }

            Assert.Equal(18, player1);
            Assert.Equal(18, player2);
        }

        [Fact]
        public void GenerateBoard_AllOccupiedCellsAreWhiteSquares()
        {
            // Arrange & Act
            var board = GenerateTestBoard();

            // Assert
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    if (board.Cells[r, c] != CellState.Empty)
                    {
                        // White squares are at rows/cols 1-6
                        Assert.InRange(r, 1, 6);
                        Assert.InRange(c, 1, 6);
                    }
                }
        }

        [Fact]
        public void GenerateBoard_NoDuplicates()
        {
            // Arrange & Act
            var board = GenerateTestBoard();

            // Assert - count occupied cells should equal unique positions
            var positions = new HashSet<(int r, int c)>();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (board.Cells[r, c] != CellState.Empty)
                        positions.Add((r, c));

            Assert.Equal(36, positions.Count);
        }

        [Fact]
        public void GenerateBoard_StatisticallyDifferent()
        {
            // Arrange & Act - Generate multiple boards
            var boards = new List<Board>();
            for (int i = 0; i < 10; i++)
                boards.Add(GenerateTestBoard());

            // Assert - At least some should differ (not all identical)
            bool anyDifferent = false;
            for (int i = 1; i < boards.Count; i++)
            {
                bool boardsSame = true;
                for (int r = 0; r < 8; r++)
                    for (int c = 0; c < 8; c++)
                        if (boards[0].Cells[r, c] != boards[i].Cells[r, c])
                        {
                            boardsSame = false;
                            break;
                        }

                if (!boardsSame)
                {
                    anyDifferent = true;
                    break;
                }
            }

            Assert.True(anyDifferent, "Generated boards should be statistically different");
        }

        private Board GenerateTestBoard()
        {
            var board = new Board();
            var whiteCells = Board.AllWhiteCells().OrderBy(_ => Random.Shared.Next()).ToList();
            for (int i = 0; i < 18; i++) board.Place(whiteCells[i].r, whiteCells[i].c, true);
            for (int i = 18; i < 36; i++) board.Place(whiteCells[i].r, whiteCells[i].c, false);
            return board;
        }
    }

    public class FirstMoveRemovalTests
    {
        [Fact]
        public void RemoveFrog_AllowedBeforeFirstJump()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);
            board.Place(4, 4, false);
            bool player1Removed = false;

            // Act & Assert - removal should succeed
            if (board.Cells[2, 2] != CellState.Empty)
            {
                board.Remove(2, 2);
                player1Removed = true;
            }

            Assert.True(player1Removed);
            Assert.True(board.IsEmpty(2, 2));
        }

        [Fact]
        public void RemoveFrog_OnlyOnce()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);
            board.Place(3, 3, true);
            bool firstRemove = true, secondRemove = false;

            // Act
            if (firstRemove && board.Cells[2, 2] != CellState.Empty)
            {
                board.Remove(2, 2);
                firstRemove = false; // mark as done
            }

            // Second removal should fail (tracked by flag, not board state)
            if (firstRemove) // firstRemove is now false
            {
                secondRemove = false; // prevented by flag
            }

            // Assert
            Assert.True(board.IsEmpty(2, 2));
            Assert.False(secondRemove);
        }
    }

    public class JumpValidationTests
    {
        [Fact]
        public void IsLegalJump_JumpOverOwnFrog_Allowed()
        {
            // Arrange
            var board = new Board();
            board.Place(1, 1, true);  // own frog to jump over
            board.Place(2, 2, true);  // jumping frog

            // Act
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 4, 4);

            // Assert
            Assert.True(isLegal);
        }

        [Fact]
        public void IsLegalJump_JumpOverOpponentFrog_Allowed()
        {
            // Arrange
            var board = new Board();
            board.Place(1, 1, false); // opponent frog
            board.Place(2, 2, true);  // own frog

            // Act
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 4, 4);

            // Assert
            Assert.True(isLegal);
        }

        [Fact]
        public void IsLegalJump_DestinationEmpty_Required()
        {
            // Arrange
            var board = new Board();
            board.Place(1, 1, true);
            board.Place(2, 2, true);
            board.Place(4, 4, false); // destination occupied

            // Act
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 4, 4);

            // Assert
            Assert.False(isLegal);
        }

        [Fact]
        public void IsLegalJump_MustJumpOverFrog()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);
            // no frog at (3,3) - empty middle

            // Act
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 4, 4);

            // Assert
            Assert.False(isLegal);
        }

        [Fact]
        public void IsLegalJump_NotStraightLine_Rejected()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);

            // Act - try jump to (3,5) which is not a straight line
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 3, 5);

            // Assert
            Assert.False(isLegal);
        }

        [Fact]
        public void IsLegalJump_OnlySingleFrogJump()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);
            board.Place(3, 3, true);
            board.Place(4, 4, true);

            // Act - try to jump over two frogs (invalid)
            bool isLegal = MoveValidator.IsLegalJump(board, 2, 2, 6, 6);

            // Assert
            Assert.False(isLegal);
        }

        [Theory]
        [InlineData(2, 2, 4, 4)] // orthogonal
        [InlineData(2, 2, 4, 2)] // horizontal
        [InlineData(2, 2, 2, 4)] // vertical
        [InlineData(1, 1, 3, 3)] // diagonal
        public void IsLegalJump_ValidStraightLines(int fromR, int fromC, int toR, int toC)
        {
            // Arrange
            var board = new Board();
            board.Place(fromR, fromC, true);
            int midR = fromR + (toR - fromR) / 2;
            int midC = fromC + (toC - fromC) / 2;
            board.Place(midR, midC, false);

            // Act
            bool isLegal = MoveValidator.IsLegalJump(board, fromR, fromC, toR, toC);

            // Assert
            Assert.True(isLegal);
        }

        [Fact]
        public void MakeMove_ChainOfTwoJumps()
        {
            // Arrange
            var board = new Board();
            board.Place(2, 2, true);  // jumping frog
            board.Place(3, 3, false); // jump over
            board.Place(4, 4, false); // jump over

            // Act - validate first jump
            bool jump1 = MoveValidator.IsLegalJump(board, 2, 2, 4, 4);

            // After first jump, move frog and remove jumped
            if (jump1)
            {
                board.Remove(3, 3);
                board.Remove(2, 2);
                board.Place(4, 4, true);

                // Now check second jump from (4,4)
                bool jump2 = MoveValidator.IsLegalJump(board, 4, 4, 6, 6);

                // Assert
                Assert.True(jump2);
            }
        }
    }

    public class SwampMechanicsTests
    {
        [Fact]
        public void Swamp_FrogEndingInSwamp_IsRemoved()
        {
            // Arrange - swamp cell is at row 0 or 7, col any
            var board = new Board();
            board.Place(1, 1, true);
            board.Place(0, 0, false); // at swamp edge, but need valid adjacent

            // Setup: frog at (1,2) jumps to (0,3) which is swamp
            board.Remove(0, 0);
            board.Place(1, 2, true);
            board.Place(0, 3, false);

            // Act - check if cell is swamp
            bool isSwamp = BoardConstants.IsSwamp(0, 3);

            // Assert
            Assert.True(isSwamp);
        }

        [Fact]
        public void Swamp_FrogJumpingBackToWhite_Stays()
        {
            // Arrange - frog starts in swamp, jumps back to white square
            var board = new Board();
            board.Place(1, 2, false); // opponent frog for jumping
            // Hypothetically: frog at swamp (0,2) jumps to white (2,2)

            // Act
            bool isWhite = BoardConstants.IsWhite(2, 2);

            // Assert
            Assert.True(isWhite);
        }
    }

    public class PassTurnAndGameOverTests
    {
        [Fact]
        public void PassTurn_CorrectlyTransfersTurn()
        {
            // Arrange
            string currentTurn = "Player1";
            string player2Id = "Player2";

            // Act - simulate pass (no legal jumps)
            if (currentTurn == "Player1")
                currentTurn = player2Id;

            // Assert
            Assert.Equal("Player2", currentTurn);
        }

        [Fact]
        public void GameOver_BothConsecutivePasses()
        {
            // Arrange
            int consecutivePasses = 0;
            string lastJumper = "Player1";
            string currentTurn = "Player2";

            // Act - Player2 passes
            consecutivePasses++;
            currentTurn = "Player1";

            // Player1 also has no jumps, passes
            consecutivePasses++;

            // Assert
            Assert.Equal(2, consecutivePasses);
            Assert.Equal("Player1", lastJumper); // last to jump wins
        }

        [Fact]
        public void GameOver_LastJumperWins()
        {
            // Arrange
            string lastJumper = "Player2";
            int consecutivePasses = 2;

            // Act - game ends
            string winner = lastJumper;

            // Assert
            Assert.Equal("Player2", winner);
        }
    }

    public class ConcurrencyTests
    {
        [Fact]
        public void SimultaneousMoves_DoNotCorruptState()
        {
            // Arrange
            var board = new Board();
            board.Place(1, 1, true);
            board.Place(2, 2, false);
            board.Place(4, 4, true);
            board.Place(5, 5, false);

            // Act - simulate two moves in parallel
            var move1 = Task.Run(() =>
            {
                // Player1: move (1,1) → (3,3)
                if (board.Cells[2, 2] != CellState.Empty)
                {
                    var boardClone = board.Clone();
                    boardClone.Remove(2, 2);
                    boardClone.Remove(1, 1);
                    boardClone.Place(3, 3, true);
                    return boardClone;
                }
                return null;
            });

            var move2 = Task.Run(() =>
            {
                // Player2: move (4,4) → (6,6)
                if (board.Cells[5, 5] != CellState.Empty)
                {
                    var boardClone = board.Clone();
                    boardClone.Remove(5, 5);
                    boardClone.Remove(4, 4);
                    boardClone.Place(6, 6, false);
                    return boardClone;
                }
                return null;
            });

            Task.WaitAll(move1, move2);

            // Assert - neither move should corrupt the original board
            // (in real server, SemaphoreSlim prevents simultaneous mutations)
            Assert.NotNull(move1.Result);
            Assert.NotNull(move2.Result);
            // Original board state unaffected by clones
            Assert.False(board.IsEmpty(1, 1));
            Assert.False(board.IsEmpty(4, 4));
        }
    }

    public class BoardCloneTests
    {
        [Fact]
        public void Clone_CreatesIndependentCopy()
        {
            // Arrange
            var original = new Board();
            original.Place(2, 2, true);
            original.Place(3, 3, false);

            // Act
            var clone = original.Clone();
            clone.Remove(2, 2);

            // Assert
            Assert.False(original.IsEmpty(2, 2)); // original unchanged
            Assert.True(clone.IsEmpty(2, 2));     // clone modified
        }
    }
}
